using Meridian.Dashboard.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddControllers();

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
var kafkaBootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";
var sqlConnection = Environment.GetEnvironmentVariable("SQL_CONNECTION") ?? "";

builder.Services.AddSingleton<RedisDataService>(sp => new RedisDataService(redisConnection));
builder.Services.AddHostedService<PortfolioPollingService>();

// Position management services
builder.Services.AddSingleton<KafkaCommandPublisher>(sp => new KafkaCommandPublisher(kafkaBootstrap));
builder.Services.AddSingleton<SqlPositionService>(sp => new SqlPositionService(sqlConnection));

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
app.MapControllers();
app.MapBlazorHub();
app.MapHub<Meridian.Dashboard.Hubs.DashboardHub>("/dashboardhub");
app.MapFallbackToPage("/_Host");
app.Run();
