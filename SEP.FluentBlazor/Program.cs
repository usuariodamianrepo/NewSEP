using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using SEP.FluentBlazor;
using SEP.FluentBlazor.Helpers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddHttpClient("SystemApiClient", client =>
{
    client.BaseAddress = new Uri("https://localhost:7015/");
});
builder.Services.AddHttpClient("EtikitApiClient", client =>
{
    client.BaseAddress = new Uri("https://localhost:7049/");
});
builder.Services.AddScoped<GetHttpClient>();
builder.Services.AddFluentUIComponents();

await builder.Build().RunAsync();
