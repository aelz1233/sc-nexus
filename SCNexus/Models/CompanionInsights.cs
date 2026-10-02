using System.Globalization;

namespace SCNexus.Models;

public enum NexusNotificationKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record NexusNotification(
    string Key,
    string Title,
    string Message,
    NexusNotificationKind Kind,
    DateTimeOffset Timestamp)
{
    public string TimeDisplay => Timestamp.LocalDateTime.ToString("HH:mm", CultureInfo.CurrentCulture);
    public string Accent => Kind switch
    {
        NexusNotificationKind.Success => "#69CFC0",
        NexusNotificationKind.Warning => "#F2C879",
        NexusNotificationKind.Error => "#FF8E86",
        _ => "#7EA9FF"
    };
}
