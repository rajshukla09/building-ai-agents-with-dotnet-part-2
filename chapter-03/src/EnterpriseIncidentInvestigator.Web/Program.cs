using EnterpriseIncidentInvestigator.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app"); builder.RootComponents.Add<HeadOutlet>("head::after");
var api = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5130/";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(api) }); builder.Services.AddMudServices();
await builder.Build().RunAsync();
