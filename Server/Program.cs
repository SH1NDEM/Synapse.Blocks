using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Synapse.Blocks.Models;
using Synapse.Blocks.Server;

var builder = WebApplication.CreateBuilder(args);
var password = builder.Configuration["Admin:Password"];
if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
    throw new InvalidOperationException("Задайте Admin:Password (не менее 12 символов) через user-secrets или Admin__Password.");
var passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes(password));

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<CampaignRepository>();
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
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
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
    new AdminSessionInfo(context.User.Identity?.IsAuthenticated == true));
app.MapPost("/api/admin/login", async (AdminLoginRequest request, HttpContext context) =>
{
    if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 ||
        !CryptographicOperations.FixedTimeEquals(passwordHash, SHA256.HashData(Encoding.UTF8.GetBytes(request.Password))))
        return Results.Text("Неверный пароль.", statusCode: 401);
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
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
    return Results.Bytes(campaign.Json, "application/json");
}
app.MapGet("/api/levels", ReadCampaign);
app.MapGet("/api/admin/levels", ReadCampaign).RequireAuthorization();
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
app.Run();
