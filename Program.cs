using RoadmapTracker.Models;
using RoadmapTracker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<JsonLogService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var api = app.MapGroup("/api");

// Full Database
api.MapGet("/database", async (JsonLogService s) => Results.Ok(await s.GetFullDatabaseAsync()));

// Profiles
api.MapPost("/profiles", async (UserProfile p, JsonLogService s) =>
    Results.Ok(await s.CreateProfileAsync(p.Username, p.RoleTitle)));

api.MapPost("/profiles/{id}/activate", async (string id, JsonLogService s) =>
{
    await s.SetActiveProfileAsync(id);
    return Results.NoContent();
});

api.MapDelete("/profiles/{id}", async (string id, JsonLogService s) =>
    await s.DeleteProfileAsync(id) ? Results.NoContent() : Results.BadRequest("Cannot delete the only profile."));

// Targets (under a Profile)
api.MapPost("/profiles/{profileId}/targets", async (string profileId, LearningTarget t, JsonLogService s) =>
    Results.Ok(await s.CreateTargetAsync(profileId, t)));

api.MapDelete("/profiles/{profileId}/targets/{targetId}", async (string profileId, string targetId, JsonLogService s) =>
    await s.DeleteTargetAsync(profileId, targetId) ? Results.NoContent() : Results.NotFound());

// Logs (under a Profile and Target)
api.MapPost("/profiles/{profileId}/targets/{targetId}/logs", async (string profileId, string targetId, DailyLog log, JsonLogService s) =>
{
    await s.UpsertLogAsync(profileId, targetId, log);
    return Results.Ok(log);
});

// Export full database as a downloadable file
api.MapGet("/backup/export", async (JsonLogService s) =>
{
    var db = await s.GetFullDatabaseAsync();
    var jsonBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(db, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var fileName = $"roadmap-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
    return Results.File(jsonBytes, "application/json", fileName);
});

// Restore full database from uploaded payload
api.MapPost("/backup/restore", async (AppDatabase restoredDb, JsonLogService s) =>
{
    var success = await s.RestoreDatabaseAsync(restoredDb);
    return success ? Results.Ok(new { message = "Database restored successfully." }) : Results.BadRequest("Invalid backup format.");
});

api.MapDelete("/profiles/{profileId}/targets/{targetId}/logs/{dayNumber:int}", async (string profileId, string targetId, int dayNumber, JsonLogService s) =>
    await s.DeleteLogAsync(profileId, targetId, dayNumber) ? Results.NoContent() : Results.NotFound());

app.Run();