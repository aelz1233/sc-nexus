namespace SCNexus.Models;

public sealed record GameSessionSummary(string FileName, DateTime LastWriteTime, string Shard, string Region)
{
    public string DateDisplay => LastWriteTime.ToString("dd.MM.yyyy HH:mm");
    public string ServerDisplay => Shard == "Не определён" ? Region : $"{Region} · {Shard}";
}
