var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
builder.Services.AddSingleton<Meridian.Dashboard.Services.RedisDataService>(sp =>
    new Meridian.Dashboard.Services.RedisDataService(redisConnection));
builder.Services.AddHostedService<Meridian.Dashboard.Services.PortfolioPollingService>();

builder.Services.AddHttpClient("Scenarios", client =>
{
    var scenariosUrl = Environment.GetEnvironmentVariable("SCENARIOS_URL") ?? "http://localhost:8000";
    client.BaseAddress = new Uri(scenariosUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapHub<Meridian.Dashboard.Hubs.DashboardHub>("/dashboardhub");
app.MapFallbackToPage("/_Host");
app.Run();
