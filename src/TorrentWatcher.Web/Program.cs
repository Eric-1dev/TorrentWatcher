using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using TorrentWatcher.Application.Abstractions;
using TorrentWatcher.Application.Exceptions;
using TorrentWatcher.Application.Services;
using TorrentWatcher.Application.Settings;
using TorrentWatcher.Infrastructure;
using TorrentWatcher.Infrastructure.Persistence;
using TorrentWatcher.Infrastructure.Rutracker;
using TorrentWatcher.Web.Health;
using TorrentWatcher.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ISettingsProvider, SettingsProvider>();
builder.Services.AddSingleton<SettingsWriteGate>();
builder.Services.AddSingleton<RutrackerSessionService>();
builder.Services.AddSingleton<IRutrackerSessionProvider>(
    provider => provider.GetRequiredService<RutrackerSessionService>());
builder.Services.AddSingleton<ScanStatusStore>();
builder.Services.AddSingleton<ScanCoordinator>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<TorrentService>();
builder.Services.AddScoped<TorrentScanService>();
builder.Services.AddScoped<TorrentApplyService>();
builder.Services.AddScoped<ITorrentUpdateApplier>(
    provider => provider.GetRequiredService<TorrentApplyService>());
builder.Services.AddScoped<SystemStatusService>();
builder.Services.AddScoped<CloudflareClearanceService>();
builder.Services.AddScoped<ICloudflareClearanceProvider>(
    provider => provider.GetRequiredService<CloudflareClearanceService>());
builder.Services.AddHostedService<ScanScheduler>();

builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

string connectionString =
    builder.Configuration.GetConnectionString("Default") ?? "Data Source=torrentwatcher.db";
string torrentsDirectory =
    builder.Configuration["Storage:TorrentsDirectory"] ?? Path.Combine("data", "torrents");
string keysDirectory =
    builder.Configuration["Security:KeysDirectory"] ?? Path.Combine("data", "keys");

var browserOptions = new RutrackerBrowserOptions();
builder.Configuration
    .GetSection(RutrackerBrowserOptions.SectionName)
    .Bind(browserOptions);

builder.Services.AddInfrastructure(connectionString, torrentsDirectory, keysDirectory, browserOptions);

var app = builder.Build();

// Миграции накатываются на старте: контейнер должен подниматься на чистом /data.
using (IServiceScope scope = app.Services.CreateScope())
{
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Снапшот настроек нужен планировщику и запросам сразу, не дожидаясь первого PUT.
    SettingsService settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
    await settings.InitializeAsync(CancellationToken.None);
}

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

// index.html не кешируем, хешированные ассеты Vite — навсегда.
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache, no-store, must-revalidate"
                : "public, max-age=31536000, immutable";
    },
};

app.UseDefaultFiles();
app.UseStaticFiles(staticFileOptions);

app.MapOpenApi();
app.MapScalarApiReference("/api/docs", options =>
{
    options.WithTitle("TorrentWatcher API");
    options.WithOpenApiRoutePattern("/openapi/{documentName}.json");
});

app.MapControllers();
app.MapHealthChecks("/api/health");

// Неизвестный /api/* должен давать 404, а не index.html от SPA-fallback.
app.MapFallback("/api/{**path}", (HttpContext context) =>
{
    var problem = new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Не найдено",
        Detail = $"Эндпоинт {context.Request.Path} не найден.",
        Instance = context.Request.Path,
    };

    problem.Extensions["code"] = ErrorCodes.EndpointNotFound;
    problem.Extensions["traceId"] = context.TraceIdentifier;

    return Results.Json(
        problem,
        statusCode: StatusCodes.Status404NotFound,
        contentType: "application/problem+json");
});

app.MapFallbackToFile("index.html", staticFileOptions);

app.Run();

/// <summary>Маркер для интеграционных тестов (`WebApplicationFactory&lt;Program&gt;`).</summary>
public partial class Program { }
