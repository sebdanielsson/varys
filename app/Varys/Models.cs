using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Varys;

public sealed class MeetingMeta
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("language")] public string Language { get; set; } = "";
    [JsonPropertyName("engine")] public string Engine { get; set; } = "";
    [JsonPropertyName("duration_s")] public double DurationS { get; set; }
    [JsonPropertyName("utterances")] public int Utterances { get; set; }
    [JsonPropertyName("has_summary")] public bool HasSummary { get; set; }
    [JsonPropertyName("indexed")] public bool Indexed { get; set; }

    [JsonIgnore]
    public string FriendlyDate =>
        DateTime.TryParse(Created, out var d) ? d.ToString("MMM d  HH:mm") : Created;

    [JsonIgnore]
    public string DurationLabel
    {
        get { var t = TimeSpan.FromSeconds(DurationS); return $"{(int)t.TotalMinutes}:{t.Seconds:00}"; }
    }

    [JsonIgnore]
    public string Subtitle => $"{FriendlyDate}   ·   {DurationLabel}   ·   {Language.ToUpperInvariant()}";

    // Segoe Fluent: notes (E8A5) if summarized, otherwise a message glyph (E8BD).
    [JsonIgnore]
    public string Glyph => char.ConvertFromUtf32(HasSummary ? 0xE8A5 : 0xE8BD);
}

public sealed class MeetingsResponse
{
    [JsonPropertyName("meetings")] public List<MeetingMeta> Meetings { get; set; } = new();
}

public sealed class TranscriptUtterance
{
    [JsonPropertyName("speaker")] public string Speaker { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("start")] public double Start { get; set; }
}

public sealed class TranscriptDoc
{
    [JsonPropertyName("utterances")] public List<TranscriptUtterance> Utterances { get; set; } = new();
}

public sealed class MeetingDetail
{
    [JsonPropertyName("meta")] public MeetingMeta Meta { get; set; } = new();
    [JsonPropertyName("transcript")] public TranscriptDoc Transcript { get; set; } = new();
    [JsonPropertyName("transcript_md")] public string TranscriptMd { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("dir")] public string Dir { get; set; } = "";
}

public sealed class SearchHit
{
    [JsonPropertyName("meeting")] public MeetingMeta Meeting { get; set; } = new();
    [JsonPropertyName("speaker")] public string? Speaker { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("score")] public double Score { get; set; }
}

public sealed class SearchResponse
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("hits")] public List<SearchHit> Hits { get; set; } = new();
}
