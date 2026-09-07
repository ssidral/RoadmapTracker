namespace RoadmapTracker.Models;

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class UserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Username { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public List<LearningTarget> Targets { get; set; } = new();
}

public class LearningTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string UserProfileId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int TargetDays { get; set; } = 30;
    public string Description { get; set; } = string.Empty;
    public List<DailyLog> Logs { get; set; } = new();
}

public class DailyLog
{
    public int Id { get; set; }
    public string LearningTargetId { get; set; } = string.Empty;
    public int DayNumber { get; set; }
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public string Topic { get; set; } = string.Empty;
    public int TheoryMinutes { get; set; }
    public int LabMinutes { get; set; }
    public bool GitPushed { get; set; }
    public string Notes { get; set; } = string.Empty;
}

// Payload contract for Backup/Restore compatibility
public class AppDatabase
{
    public string ActiveProfileId { get; set; } = string.Empty;
    public List<UserProfile> Profiles { get; set; } = new();
}