using MudBlazor.Services;
using OBBX.Web.Components;
using OBBX.Shared.Services;
using OBBX.Web.Services;
using ObsWebSocket.Core;
using Serilog;
using Microsoft.Extensions.Options;
using OBBX.Shared.ViewModels;

var builder = WebApplication.CreateBuilder(args);

// Create logging and save to local appdata folder
var appDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OBBX");
Directory.CreateDirectory(appDataPath);
var logFilePath = Path.Combine(appDataPath, "OBBX-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File(logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();



builder.Services.AddSingleton<SettingsService>();
// Load settings synchronously before configuring services
var settings = Task.Run(() => new SettingsService().GetSettingsAsync()).GetAwaiter().GetResult();

// Use the library's extension method to register all dependencies
builder.Services.AddObsWebSocketClient(options =>
{
    // Ensure we have a valid hostname and port to prevent UriFormatException
    var host = string.IsNullOrWhiteSpace(settings.OBSWebSocket.Address) 
               ? "localhost" 
               : settings.OBSWebSocket.Address;
               
    var port = settings.OBSWebSocket.Port <= 0 ? 4455 : settings.OBSWebSocket.Port;

    options.ServerUri = new Uri($"ws://{host}:{port}");
    options.Password = settings.OBSWebSocket.Key ?? "";
});

builder.Services.AddSingleton<OBSWebSocketService>();
builder.Services.AddSingleton<ChallongeService>();
builder.Services.AddSingleton<CSVService>();
builder.Services.AddSingleton<MatchStateService>();
builder.Services.AddScoped<JsConsole>();
// Add device-specific services used by the OBBX.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();


builder.Services.AddTransient<DashboardViewModel>();
builder.Services.AddTransient<TablesViewModel>();
builder.Services.AddTransient<SettingsViewModel>();
builder.Services.AddTransient<PlayerDeckProfileViewModel>();
builder.WebHost.UseUrls("http://localhost:5181");
var app = builder.Build();

// Initialize CSV service from persisted settings on app start (no manual refresh needed)
var csvService = app.Services.GetRequiredService<CSVService>();
await csvService.InitializeAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

app.MapRazorComponents<App>()
.AddInteractiveServerRenderMode()
.AddAdditionalAssemblies(typeof(OBBX.Shared.Pages.Dashboard).Assembly);

if (app.Environment.IsProduction())
{
    var url = "http://localhost:5181"; 
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });
}

app.Run();