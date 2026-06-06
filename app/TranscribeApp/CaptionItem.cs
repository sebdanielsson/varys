using Microsoft.UI.Xaml.Media;

namespace TranscribeApp;

/// <summary>A committed (final) caption line shown in the transcript list.</summary>
public sealed class CaptionItem
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    public SolidColorBrush Color { get; set; } = new();
}
