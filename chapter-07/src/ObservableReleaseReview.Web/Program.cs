using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ObservableReleaseReview.Web;
using ObservableReleaseReview.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseAddress = builder.Configuration["ApiBaseAddress"]
    ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseAddress)
});
builder.Services.AddScoped<ReleaseReviewApiClient>();

await builder.Build().RunAsync();
