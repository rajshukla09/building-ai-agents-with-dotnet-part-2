using EnterpriseArchitectureAssessment.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
var b = WebAssemblyHostBuilder.CreateDefault(args);
b.RootComponents.Add<App>("#app");
b.Services.AddScoped(
    _ => new HttpClient { BaseAddress = new Uri(b.Configuration["ApiBaseUrl"] ?? b.HostEnvironment.BaseAddress) });
await b.Build().RunAsync();
