using LocalRAGChat.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http;
using Microsoft.Extensions.Configuration;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddHttpClient("Api", client =>
{
    var apiUrl = builder.Configuration.GetValue<string>("ApiUrl");

    if (string.IsNullOrEmpty(apiUrl))
    {
        throw new InvalidOperationException("ApiUrl is not configured in appsettings.json.");
    }

    client.BaseAddress = new Uri(apiUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

await builder.Build().RunAsync();
