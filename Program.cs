using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoadmapTracker.Data;
using RoadmapTracker.Models;
using RoadmapTracker.Services;

var builder = WebApplication.CreateBuilder(args);

// Determine database path from DATA_DIR environment variable
var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") 
              ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "roadmap.db");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<SqliteLogService>();

var app = builder.Build();

// Ensure SQLite database and seed data are created on startup
using (var scope = app.Services.CreateScope())
{
    var service = scope.ServiceProvider.GetRequiredService<SqliteLogService>();
    await service.InitializeAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// 1. Full Database fetch
api.MapGet("/database", async (SqliteLogService s) => 
    Results.Ok(await s.GetFullDatabaseAsync()));

// 2. Profile Management
api.MapPost("/profiles", async (UserProfile p, SqliteLogService s) => 
    Results.Ok(await s.CreateProfileAsync(p.Username, p.RoleTitle)));

api.MapDelete("/profiles/{profileId}", async (string profileId, SqliteLogService s) => 
    await s.DeleteProfileAsync(profileId) ? Results.Ok() : Results.BadRequest("Cannot delete the only profile."));

api.MapPost("/profiles/{profileId}/activate", async (string profileId, SqliteLogService s) =>
{
    await s.SetActiveProfileAsync(profileId);
    return Results.Ok();
});

// 3. Learning Targets
api.MapPost("/profiles/{profileId}/targets", async (string profileId, LearningTarget t, SqliteLogService s) => 
    Results.Ok(await s.CreateTargetAsync(profileId, t)));

api.MapDelete("/profiles/{profileId}/targets/{targetId}", async (string profileId, string targetId, SqliteLogService s) => 
    await s.DeleteTargetAsync(profileId, targetId) ? Results.Ok() : Results.NotFound());

// 4. Daily Logs
api.MapPost("/profiles/{profileId}/targets/{targetId}/logs", async (string profileId, string targetId, DailyLog log, SqliteLogService s) =>
{
    await s.UpsertLogAsync(targetId, log);
    return Results.Ok();
});

api.MapDelete("/profiles/{profileId}/targets/{targetId}/logs/{dayNumber:int}", async (string profileId, string targetId, int dayNumber, SqliteLogService s) => 
    await s.DeleteLogAsync(targetId, dayNumber) ? Results.Ok() : Results.NotFound());

// 5. Backup & Restore (Compatible with JSON files)
api.MapGet("/backup/export", async (SqliteLogService s) =>
{
    var fullDb = await s.GetFullDatabaseAsync();
    var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(fullDb, new JsonSerializerOptions { WriteIndented = true });
    var fileName = $"roadmap-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
    return Results.File(jsonBytes, "application/json", fileName);
});

api.MapPost("/backup/restore", async (AppDatabase restoredDb, SqliteLogService s) =>
{
    var success = await s.RestoreDatabaseAsync(restoredDb);
    return success 
        ? Results.Ok(new { message = "Database restored successfully." }) 
        : Results.BadRequest("Invalid backup format.");
});

app.Run();
