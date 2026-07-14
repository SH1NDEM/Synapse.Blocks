using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Synapse.Blocks;
using Synapse.Blocks.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LevelStore>();
builder.Services.AddScoped<ProgressStore>();
builder.Services.AddSingleton<BlockProgramRunner>();

await builder.Build().RunAsync();
