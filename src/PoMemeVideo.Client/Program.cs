using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using PoMemeVideo.Client;
using PoMemeVideo.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped<CorrelationHeaderHandler>();
builder.Services.AddScoped(sp => new HttpClient(
    new CorrelationHeaderHandler(sp.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = new HttpClientHandler(),
    })
{
    BaseAddress = new Uri(apiBaseUrl),
});
builder.Services.AddScoped<BlobUploadService>();
builder.Services.AddSingleton<NavRefreshService>();
builder.Services.AddScoped<Vibe3DService>();


builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<ApiAuthenticationStateProvider>());

await builder.Build().RunAsync();