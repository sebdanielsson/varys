using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Varys;

/// <summary>
/// First-run welcome that walks the user through the one-time setup Varys can't bundle: the
/// Python transcription engine (built with uv), at least one speech-to-text model, Ollama, and a
/// language model. Shown on every launch until all of those are in place.
/// </summary>
public sealed partial class WelcomeDialog : ContentDialog
{
    // Ollama models. The summary LLM is required; the embedding model is optional (semantic search).
    private static readonly (string Model, string Caption, bool Required)[] OllamaModels =
    {
        (OllamaSetup.SummaryModel, "Summaries & action items", true),
        (OllamaSetup.EmbedModel, "Semantic search (optional)", false),
    };

    private readonly Dictionary<string, (TextBlock status, Button button, ProgressBar progress)> _languageRows = new();
    private readonly Dictionary<string, (TextBlock status, Button button, ProgressBar progress)> _voiceRows = new();

    // When true, every step renders in its actionable state regardless of what's actually
    // installed — for previewing the onboarding flow without a fresh machine.
    private readonly bool _preview;

    private WelcomeDialog(bool preview = false)
    {
        InitializeComponent();
        _preview = preview;
        BuildVoiceRows();
        BuildLanguageRows();
        Opened += async (_, _) => await RefreshAsync();
    }

    /// <summary>
    /// True when everything Varys needs is in place: the engine, at least one speech model, Ollama,
    /// and the summary language model. (The embedding model for search is optional.)
    /// </summary>
    public static async Task<bool> IsSetupCompleteAsync()
    {
        if (!EngineSetup.IsReady)
            return false;
        if (!VoiceModels.AnyPresent())
            return false;
        if (!await OllamaSetup.IsReachableAsync())
            return false;
        var have = await OllamaSetup.InstalledModelsAsync();
        return OllamaSetup.HasModel(have, OllamaSetup.SummaryModel);
    }

    /// <summary>Show the welcome on launch whenever setup is incomplete; no-op once everything's in place.</summary>
    public static async Task ShowIfNeededAsync(XamlRoot? root)
    {
        if (root is null || await IsSetupCompleteAsync())
            return;
        await new WelcomeDialog { XamlRoot = root }.ShowAsync();
    }

    /// <summary>Show the welcome on demand (e.g. from Settings), regardless of setup state.</summary>
    public static async Task ShowAgainAsync(XamlRoot? root)
    {
        if (root is null)
            return;
        await new WelcomeDialog { XamlRoot = root }.ShowAsync();
    }

    /// <summary>
    /// Force-show the full onboarding flow with every step actionable, regardless of what's
    /// actually installed — for previewing the UI. Triggered by setting VARYS_PREVIEW_WELCOME=1.
    /// </summary>
    public static async Task ShowPreviewAsync(XamlRoot? root)
    {
        if (root is null)
            return;
        await new WelcomeDialog(preview: true) { XamlRoot = root }.ShowAsync();
    }

    // --- rows (built in code so the lists stay driven by the model catalogs) ---

    private void BuildVoiceRows()
    {
        foreach (var m in VoiceModels.All)
            _voiceRows[m.Key] = AddRow(VoiceHost, m.Title, m.Caption, m.Key, OnDownloadVoice);
    }

    private void BuildLanguageRows()
    {
        foreach (var (model, caption, _) in OllamaModels)
            _languageRows[model] = AddRow(LanguageHost, model, caption, model, OnDownloadModel);
    }

    private static (TextBlock status, Button button, ProgressBar progress) AddRow(
        Panel host, string title, string caption, string tag, RoutedEventHandler onDownload)
    {
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var cap = new TextBlock { Text = caption, FontSize = 12, Opacity = 0.7 };
        var status = new TextBlock { FontSize = 12, Opacity = 0.7 };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

        var text = new StackPanel();
        text.Children.Add(name);
        text.Children.Add(cap);
        text.Children.Add(status);
        text.Children.Add(progress);

        var button = new Button { Content = "Download", VerticalAlignment = VerticalAlignment.Center, Tag = tag };
        button.Click += onDownload;

        var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(button, 1);
        row.Children.Add(text);
        row.Children.Add(button);

        host.Children.Add(row);
        return (status, button, progress);
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

        // Speech models (downloading needs the engine)
        RefreshVoice();

        // Ollama + language model
        var ollamaReady = !_preview && await OllamaSetup.IsReachableAsync();
        if (ollamaReady)
        {
            OllamaStatus.Text = "Installed and running.";
            OllamaButtons.Visibility = Visibility.Collapsed;
            LanguageSection.Visibility = Visibility.Visible;
            await RefreshLanguageAsync();
        }
        else
        {
            OllamaStatus.Text = "Not detected — needed for summaries and semantic search.";
            OllamaButtons.Visibility = Visibility.Visible;
            OllamaInstallBtn.Visibility = OllamaSetup.FindWinget() is null ? Visibility.Collapsed : Visibility.Visible;
            if (_preview)
            {
                LanguageSection.Visibility = Visibility.Visible;
                foreach (var (model, _, _) in OllamaModels)
                    SetRow(_languageRows[model], "Not downloaded", "Download", canDownload: true);
            }
            else
            {
                LanguageSection.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void RefreshVoice()
    {
        var engineReady = _preview || EngineSetup.IsReady;
        foreach (var m in VoiceModels.All)
        {
            if (!_preview && VoiceModels.IsPresent(m))
                SetRow(_voiceRows[m.Key], "Downloaded ✓", "Installed", canDownload: false);
            else
                SetRow(_voiceRows[m.Key],
                    engineReady ? "Not downloaded · a few GB" : "Set up the engine first",
                    "Download", canDownload: engineReady);
        }
    }

    private async Task RefreshLanguageAsync()
    {
        var have = await OllamaSetup.InstalledModelsAsync();
        foreach (var (model, _, _) in OllamaModels)
        {
            if (OllamaSetup.HasModel(have, model))
                SetRow(_languageRows[model], "Downloaded ✓", "Installed", canDownload: false);
            else
                SetRow(_languageRows[model], "Not downloaded", "Download", canDownload: true);
        }
    }

    private static void SetRow((TextBlock status, Button button, ProgressBar progress) row, string status, string button, bool canDownload)
    {
        row.status.Text = status;
        row.button.Content = button;
        row.button.IsEnabled = canDownload;
        row.progress.Visibility = Visibility.Collapsed;
    }

    // --- actions ---

    private async void OnSetupEngine(object sender, RoutedEventArgs e)
    {
        EngineBtn.IsEnabled = false;
        EngineProgress.Visibility = Visibility.Visible;
        ShowStatus("Building the transcription engine… downloading PyTorch (a few minutes).", InfoBarSeverity.Informational);

        var python = await EngineSetup.BuildAsync(new Progress<string>(s => EngineStatus.Text = Shorten(s)));

        EngineProgress.Visibility = Visibility.Collapsed;
        ShowStatus(python != null ? "Transcription engine is ready." : "Engine setup didn't finish. See the log (Open logs) and try again.",
            python != null ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        await RefreshAsync();
    }

    private async void OnDownloadVoice(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
            return;
        var model = VoiceModels.All.FirstOrDefault(m => m.Key == key);
        if (model is null)
            return;
        if (!EngineSetup.IsReady)
        {
            ShowStatus("Set up the transcription engine first.", InfoBarSeverity.Warning);
            return;
        }

        var row = _voiceRows[key];
        row.button.IsEnabled = false;
        row.progress.Visibility = Visibility.Visible;
        row.status.Text = "Downloading… (a few GB)";
        ShowStatus($"Downloading the {model.Title} model… (several GB, one-time).", InfoBarSeverity.Informational);

        var ok = await VoiceModels.DownloadAsync(model, new Progress<string>(s => row.status.Text = Shorten(s)));

        row.progress.Visibility = Visibility.Collapsed;
        if (ok)
        {
            SetRow(row, "Downloaded ✓", "Installed", canDownload: false);
            ShowStatus($"{model.Title} ready.", InfoBarSeverity.Success);
        }
        else
        {
            SetRow(row, "Download failed — see log", "Download", canDownload: true);
            ShowStatus($"Couldn't download {model.Title}. See the log for details.", InfoBarSeverity.Error);
        }
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

        var row = _languageRows[model];
        row.button.IsEnabled = false;
        row.progress.Visibility = Visibility.Visible;
        row.status.Text = "Downloading…";
        ShowStatus($"Downloading {model}…", InfoBarSeverity.Informational);

        var code = await OllamaSetup.PullModelAsync(ollama, model);

        row.progress.Visibility = Visibility.Collapsed;
        if (code == 0)
        {
            SetRow(row, "Downloaded ✓", "Installed", canDownload: false);
            ShowStatus($"{model} downloaded.", InfoBarSeverity.Success);
        }
        else
        {
            SetRow(row, "Download failed — see log", "Download", canDownload: true);
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
