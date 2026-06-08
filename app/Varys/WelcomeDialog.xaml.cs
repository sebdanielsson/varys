using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Varys;

/// <summary>
/// First-run welcome that walks the user through the one-time setup Varys can't bundle:
/// the Python transcription engine (built with uv), Ollama, and Ollama's models (downloaded
/// individually). Shown on first launch, or later whenever something is still missing.
/// </summary>
public sealed partial class WelcomeDialog : ContentDialog
{
    // Required Ollama models and what each is for (ids mirror the sidecar config).
    private static readonly (string Model, string Caption)[] RequiredModels =
    {
        (OllamaSetup.SummaryModel, "Meeting summaries & action items"),
        (OllamaSetup.EmbedModel, "Semantic search"),
    };

    private readonly Dictionary<string, (TextBlock status, Button button, ProgressBar progress)> _modelRows = new();

    // When true, every step renders in its actionable state regardless of what's actually
    // installed — for previewing the onboarding flow without a fresh machine.
    private readonly bool _preview;

    private WelcomeDialog(bool preview = false)
    {
        InitializeComponent();
        _preview = preview;
        BuildModelRows();
        Opened += async (_, _) => await RefreshAsync();
    }

    private static string WelcomedMarker => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Varys", ".welcomed");

    /// <summary>Show the welcome on first launch, or any later launch where setup is incomplete.</summary>
    public static async Task ShowIfNeededAsync(XamlRoot? root)
    {
        if (root is null)
            return;

        var firstRun = !SafeExists(WelcomedMarker);
        var engineReady = EngineSetup.IsReady;
        var ollamaReady = await OllamaSetup.IsReachableAsync();
        var modelsMissing = ollamaReady ? (await OllamaSetup.MissingModelsAsync()).Count : RequiredModels.Length;

        if (!firstRun && engineReady && ollamaReady && modelsMissing == 0)
            return;   // already welcomed and everything's in place

        var dialog = new WelcomeDialog { XamlRoot = root };
        await dialog.ShowAsync();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WelcomedMarker)!);
            File.WriteAllText(WelcomedMarker, DateTime.UtcNow.ToString("o"));
        }
        catch { /* best-effort marker */ }
    }

    /// <summary>
    /// Force-show the full onboarding flow with every step actionable, regardless of what's
    /// actually installed — for previewing the UI. Triggered by setting VARYS_PREVIEW_WELCOME=1.
    /// </summary>
    public static async Task ShowPreviewAsync(XamlRoot? root)
    {
        if (root is null)
            return;
        var dialog = new WelcomeDialog(preview: true) { XamlRoot = root };
        await dialog.ShowAsync();
    }

    /// <summary>Show the welcome on demand (e.g. from Settings), regardless of the first-run marker.</summary>
    public static async Task ShowAgainAsync(XamlRoot? root)
    {
        if (root is null)
            return;
        var dialog = new WelcomeDialog { XamlRoot = root };
        await dialog.ShowAsync();
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); }
        catch { return false; }
    }

    // --- model rows (built in code so the list stays driven by RequiredModels) ---

    private void BuildModelRows()
    {
        foreach (var (model, caption) in RequiredModels)
        {
            var name = new TextBlock { Text = model, FontWeight = FontWeights.SemiBold, FontSize = 13 };
            var cap = new TextBlock { Text = caption, FontSize = 12, Opacity = 0.7 };
            var status = new TextBlock { FontSize = 12, Opacity = 0.7 };
            var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

            var text = new StackPanel();
            text.Children.Add(name);
            text.Children.Add(cap);
            text.Children.Add(status);
            text.Children.Add(progress);

            var button = new Button { Content = "Download", VerticalAlignment = VerticalAlignment.Center, Tag = model };
            button.Click += OnDownloadModel;

            var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(text);
            row.Children.Add(button);

            ModelsHost.Children.Add(row);
            _modelRows[model] = (status, button, progress);
        }
    }

    // --- state ---

    private async Task RefreshAsync()
    {
        // Engine
        if (!_preview && EngineSetup.IsReady)
        {
            EngineStatus.Text = "Installed and ready.";
            EngineBtn.Content = "Installed";
            EngineBtn.IsEnabled = false;
        }
        else
        {
            EngineStatus.Text = "Not set up yet — downloads PyTorch and the speech runtime (a few minutes, one-time).";
            EngineBtn.Content = "Set up";
            EngineBtn.IsEnabled = true;
        }

        // Ollama (and reveal models once it's reachable)
        var ollamaReady = !_preview && await OllamaSetup.IsReachableAsync();
        if (ollamaReady)
        {
            OllamaStatus.Text = "Installed and running.";
            OllamaButtons.Visibility = Visibility.Collapsed;
            ModelsSection.Visibility = Visibility.Visible;
            await RefreshModelsAsync();
        }
        else
        {
            OllamaStatus.Text = "Not detected — needed for summaries and semantic search.";
            OllamaButtons.Visibility = Visibility.Visible;
            OllamaInstallBtn.Visibility = OllamaSetup.FindWinget() is null ? Visibility.Collapsed : Visibility.Visible;
            if (_preview)
            {
                // Preview: reveal the models step too so the whole flow is visible at once.
                ModelsSection.Visibility = Visibility.Visible;
                foreach (var (model, _) in RequiredModels)
                {
                    var (status, button, progress) = _modelRows[model];
                    status.Text = "Not downloaded";
                    button.Content = "Download";
                    button.IsEnabled = true;
                    progress.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ModelsSection.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async Task RefreshModelsAsync()
    {
        var have = await OllamaSetup.InstalledModelsAsync();
        foreach (var (model, _) in RequiredModels)
        {
            var (status, button, progress) = _modelRows[model];
            progress.Visibility = Visibility.Collapsed;
            if (OllamaSetup.HasModel(have, model))
            {
                status.Text = "Downloaded ✓";
                button.Content = "Installed";
                button.IsEnabled = false;
            }
            else
            {
                status.Text = "Not downloaded";
                button.Content = "Download";
                button.IsEnabled = true;
            }
        }
    }

    // --- actions ---

    private async void OnSetupEngine(object sender, RoutedEventArgs e)
    {
        EngineBtn.IsEnabled = false;
        EngineProgress.Visibility = Visibility.Visible;
        ShowStatus("Building the transcription engine… downloading PyTorch (a few minutes).", InfoBarSeverity.Informational);

        var python = await EngineSetup.BuildAsync(new Progress<string>(s => EngineStatus.Text = Shorten(s)));

        EngineProgress.Visibility = Visibility.Collapsed;
        if (python != null)
            ShowStatus("Transcription engine is ready.", InfoBarSeverity.Success);
        else
            ShowStatus("Engine setup didn't finish. See the log (Open logs) and try again.", InfoBarSeverity.Error);
        await RefreshAsync();
    }

    private async void OnInstallOllama(object sender, RoutedEventArgs e)
    {
        var winget = OllamaSetup.FindWinget();
        if (winget is null)
        {
            OllamaSetup.OpenDownloadPage();
            return;
        }

        SetOllamaBusy(true);
        ShowStatus("Installing Ollama… this can take a minute.", InfoBarSeverity.Informational);
        var code = await OllamaSetup.InstallViaWingetAsync(winget);
        if (code == 0)
        {
            ShowStatus("Waiting for Ollama to start…", InfoBarSeverity.Informational);
            await OllamaSetup.WaitUntilReachableAsync(TimeSpan.FromSeconds(30));
        }
        else
        {
            ShowStatus("winget didn't complete the install — try the Website button instead.", InfoBarSeverity.Error);
        }
        SetOllamaBusy(false);
        await RefreshAsync();
    }

    private void OnOpenOllamaSite(object sender, RoutedEventArgs e)
    {
        OllamaSetup.OpenDownloadPage();
        ShowStatus("Opened ollama.com — after installing, reopen Varys or click Install again.", InfoBarSeverity.Informational);
    }

    private async void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string model)
            return;
        var ollama = OllamaSetup.FindOllama();
        if (ollama is null)
        {
            ShowStatus("Ollama CLI not found yet — make sure Ollama finished installing.", InfoBarSeverity.Error);
            return;
        }

        var (status, btn, progress) = _modelRows[model];
        btn.IsEnabled = false;
        progress.Visibility = Visibility.Visible;
        status.Text = "Downloading…";
        ShowStatus($"Downloading {model}…", InfoBarSeverity.Informational);

        var code = await OllamaSetup.PullModelAsync(ollama, model);

        progress.Visibility = Visibility.Collapsed;
        if (code == 0)
        {
            status.Text = "Downloaded ✓";
            btn.Content = "Installed";
            ShowStatus($"{model} downloaded.", InfoBarSeverity.Success);
        }
        else
        {
            status.Text = "Download failed — see log";
            btn.IsEnabled = true;
            ShowStatus($"Couldn't download {model}. See the log for details.", InfoBarSeverity.Error);
        }
    }

    // --- helpers ---

    private void SetOllamaBusy(bool busy)
    {
        OllamaProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        OllamaInstallBtn.IsEnabled = !busy;
        OllamaWebBtn.IsEnabled = !busy;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }

    private static string Shorten(string s) => s.Length > 80 ? string.Concat(s.AsSpan(0, 80), "…") : s;
}
