using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Chess.Web.Pages.Play>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The board renders entirely client-side (RgbaImageRenderer -> canvas). The only
// fetch is the two .ttf fonts from wwwroot at startup, so an HttpClient scoped to
// the app base address is all we need.
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
