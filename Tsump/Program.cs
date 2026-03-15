using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Tsump;
using Tsump.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<MemberService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<TableAssignmentService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<TournamentAssignmentService>();
builder.Services.AddScoped<AppModeService>();
builder.Services.AddSingleton<LanguageService>();

await builder.Build().RunAsync();
