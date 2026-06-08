using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Varys;

/// <summary>App settings: theme, transcription language, re-run the welcome, and version info.</summary>
public sealed partial class SettingsPage : Page
{
    private bool _ready;

    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Version" : $"Version {v.Major}.{v.Minor}.{v.Build}";

        SelectByTag(ThemeBox, AppSettings.Theme);
        SelectByTag(LanguageBox, AppSettings.Language);
        _ready = true;   // ignore the SelectionChanged events raised while initializing above
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem ci && (ci.Tag as string) == tag)
            {
                box.SelectedItem = ci;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        AppSettings.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "System";
        if (XamlRoot?.Content is FrameworkElement root)
            root.RequestedTheme = AppSettings.ElementTheme;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        AppSettings.Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
    }

    private async void OnShowWelcome(object sender, RoutedEventArgs e)
    {
        await WelcomeDialog.ShowAgainAsync(XamlRoot);
    }
}
