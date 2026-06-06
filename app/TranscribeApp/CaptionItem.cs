using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace TranscribeApp;

/// <summary>A caption line (Me/Them) shown in the live or detail transcript.</summary>
public sealed class CaptionItem
{
    public string Speaker { get; set; } = "";
    public string Text { get; set; } = "";
    public SolidColorBrush Color { get; set; } = new();

    public static CaptionItem For(string speaker, string text)
    {
        var argb = speaker == "Me"
            ? ColorHelper.FromArgb(255, 0x1E, 0x88, 0xE5)   // blue
            : ColorHelper.FromArgb(255, 0x43, 0xA0, 0x47);  // green
        return new CaptionItem { Speaker = speaker, Text = text, Color = new SolidColorBrush(argb) };
    }
}
