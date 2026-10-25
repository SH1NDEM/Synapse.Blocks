using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using EntityTagHeaderValue = Microsoft.Net.Http.Headers.EntityTagHeaderValue;
using Synapse.Blocks.Models;
using Synapse.Blocks.Server;

var builder = WebApplication.CreateBuilder(args);
var password = builder.Configuration["Admin:Password"];
if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
    throw new InvalidOperationException("Задайте Admin:Password (не менее 12 символов) через user-secrets или Admin__Password.");
var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
var username = builder.Configuration["Admin:Username"] ?? "admin";
if (string.IsNullOrWhiteSpace(username) || username.Trim().Length > 128)
    throw new InvalidOperationException("Admin:Username должен содержать от 1 до 128 символов.");
username = username.Trim();
var usernameHash = SHA256.HashData(Encoding.UTF8.GetBytes(username.ToUpperInvariant()));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<CampaignRepository>();
builder.Services.AddSingleton<CampaignMediaStore>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/wasm"]);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "synapse-admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.UseWebAssemblyDebugging();
else
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync("Не удалось выполнить запрос. Попробуйте ещё раз.");
    }));
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseResponseCompression();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Cross-origin forms cannot supply this header. No cross-origin API access is enabled.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            && context.Request.Headers["X-Synapse-Request"] != "1")
        {
            context.Response.StatusCode = 403;
            return;
        }
    }
    await next(context);
});

app.MapGet("/api/admin/session", (HttpContext context) =>
    new AdminSessionInfo(context.User.Identity?.IsAuthenticated == true, context.User.Identity?.Name));
app.MapPost("/api/admin/login", async (AdminLoginRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 128 ||
        string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 ||
        !CryptographicOperations.FixedTimeEquals(usernameHash, SHA256.HashData(Encoding.UTF8.GetBytes(request.Username.Trim().ToUpperInvariant()))) ||
        !CryptographicOperations.FixedTimeEquals(passwordHash, SHA256.HashData(Encoding.UTF8.GetBytes(request.Password))))
        return Results.Text("Неверный логин или пароль.", statusCode: 401);
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.NoContent();
}).RequireRateLimiting("login");
app.MapPost("/api/admin/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

async Task<IResult> ReadCampaign(CampaignRepository repository, HttpContext context)
{
    var campaign = await repository.ReadAsync(context.RequestAborted);
    context.Response.Headers.ETag = campaign.ETag;
    context.Response.Headers.CacheControl = "private, no-cache";
    if (context.Request.Headers.IfNoneMatch.ToString().Split(',').Any(value =>
            value.Trim() is "*" || value.Trim().Replace("W/", "", StringComparison.Ordinal) == campaign.ETag))
        return Results.StatusCode(304);
    return Results.Bytes(campaign.Json, "application/json");
}
app.MapGet("/api/levels", ReadCampaign);
app.MapGet("/api/admin/levels", ReadCampaign).RequireAuthorization();
app.MapGet("/api/media/{name}", (string name, CampaignMediaStore media, HttpContext context) =>
{
    var asset = media.Find(name);
    if (asset is null) return Results.NotFound();
    context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.File(asset.Value.Path, asset.Value.Mime, entityTag: new EntityTagHeaderValue($"\"{name}\""), enableRangeProcessing: true);
});
app.MapPut("/api/admin/levels", async (List<LevelDefinition> levels, CampaignRepository repository, HttpContext context) =>
{
    try
    {
        var etag = await repository.SaveAsync(levels, context.Request.Headers.IfMatch.ToString(), context.RequestAborted);
        context.Response.Headers.ETag = etag;
        return Results.NoContent();
    }
    catch (CampaignConflictException)
    {
        return Results.Text("Кампания изменена в другой вкладке. Экспортируйте свои правки и перезагрузите кампанию перед сохранением.", statusCode: 409);
    }
    catch (ArgumentException exception)
    {
        return Results.Text(exception.Message, statusCode: 400);
    }
}).RequireAuthorization();
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
// Decode legacy inline images and prepare the cached response before accepting visitors.
await app.Services.GetRequiredService<CampaignRepository>().ReadAsync();
app.Run();
