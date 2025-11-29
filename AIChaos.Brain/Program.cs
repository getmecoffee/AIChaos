using AIChaos.Brain.Models;
using AIChaos.Brain.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

// Configure settings
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AIChaos"));

// Register services as singletons
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CommandQueueService>();
builder.Services.AddSingleton<AiCodeGeneratorService>();
builder.Services.AddSingleton<TwitchService>();
builder.Services.AddSingleton<YouTubeService>();

// Configure CORS for local development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Console.WriteLine("========================================");
Console.WriteLine("  AI Chaos Brain - C# Edition");
Console.WriteLine("========================================");
Console.WriteLine($"  Open http://localhost:5000/ in your browser");
Console.WriteLine("  Setup: http://localhost:5000/#/setup");
Console.WriteLine("========================================");

app.Run("http://0.0.0.0:5000");
