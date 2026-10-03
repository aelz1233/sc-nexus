using System;

namespace SCNexus.Models;

public sealed record ContractEarning(string MissionId, string Name, decimal Amount,
    DateTimeOffset CompletedAt, double Minutes, bool AutoDetected = false, string[]? RelatedMissionIds = null, bool Excluded = false)
{
    public string Display => $"{CompletedAt.LocalDateTime:g} · {MissionState.FormatName(Name, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en")} · +{Amount:N0} aUEC · {(AutoDetected ? "Game.log" : System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "Confirmed" : "Подтверждено")}";
}
