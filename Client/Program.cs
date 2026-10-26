using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Synapse.Blocks;
using Synapse.Blocks.Graph;
using Synapse.Blocks.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LevelStore>();
builder.Services.AddScoped<ProgressStore>();
builder.Services.AddScoped<SolutionStore>();
builder.Services.AddScoped<BlockLibraryStore>();
builder.Services.AddScoped<AdminSession>();
// Реестр блоков и интерпретатор не хранят состояния между запусками.
builder.Services.AddSingleton(NodeRegistry.Default);
builder.Services.AddSingleton(sp => new GraphRunner(sp.GetRequiredService<NodeRegistry>()));

await builder.Build().RunAsync();
