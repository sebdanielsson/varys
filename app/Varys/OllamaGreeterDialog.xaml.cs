using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Varys;

/// <summary>
/// First-run greeter that helps the user get Ollama ready for summaries and semantic search.
/// Adapts to what's missing: installs Ollama via winget (or links to ollama.com), then pulls
/// the required models. Shown only when something is actually missing.
/// </summary>
public sealed partial class OllamaGreeterDialog : ContentDialog
{
    private enum Stage { InstallOllama, PullModels }

    private Stage _stage;
    private string? _winget;
    private string? _ollama;
    private List<string> _missing;

    private OllamaGreeterDialog(bool ollamaReachable, List<string> missingModels, string? winget, string? ollama)
    {
        InitializeComponent();
        _winget = winget;
        _ollama = ollama;
        _missing = missingModels;
        _stage = ollamaReachable ? Stage.PullModels : Stage.InstallOllama;
        Render();
    }

    /// <summary>Check what's missing and, if anything is, show the greeter. No-op when fully set up.</summary>
    public static async Task ShowIfNeededAsync(XamlRoot? root)
    {
        if (root is null)
            return;
        var reachable = await OllamaSetup.IsReachableAsync();
        var missing = reachable ? await OllamaSetup.MissingModelsAsync() : new List<string>();
        if (reachable && missing.Count == 0)
            return;   // Ollama is installed and all models are present — nothing to do.

        var dialog = new OllamaGreeterDialog(reachable, missing, OllamaSetup.FindWinget(), OllamaSetup.FindOllama())
        {
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private void Render()
    {
        if (_stage == Stage.InstallOllama)
        {
            Features.Visibility = Visibility.Visible;
            Body.Text = "Varys uses Ollama — a free, local LLM runner — for meeting summaries and "
                      + "semantic search. It isn't installed yet.";
            PrimaryButtonText = _winget is null ? "" : "Install with winget";
            SecondaryButtonText = "Get it from ollama.com";
            Footnote.Text = _winget is null
                ? "winget isn't available here — use the ollama.com button, then reopen Varys."
                : "Everything runs locally — Ollama serves a model on your machine, nothing is sent to the cloud.";
        }
        else // PullModels
        {
            Features.Visibility = Visibility.Collapsed;
            var list = string.Join("\n   •  ", _missing);
            Body.Text = $"Ollama is ready. Varys needs {(_missing.Count == 1 ? "one model" : $"{_missing.Count} models")} "
                      + $"for summaries and search:\n   •  {list}";
            PrimaryButtonText = _ollama is null ? "" : "Download models";
            SecondaryButtonText = "";
            Footnote.Text = "The models download once and are cached by Ollama for future meetings.";
        }
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            SetBusy(true);
            var done = _stage == Stage.InstallOllama
                ? await InstallOllamaAsync()
                : await PullModelsAsync();

            // Keep the dialog open unless the whole setup is finished.
            args.Cancel = !done;
            if (!done)
                Render();
        }
        finally
        {
            SetBusy(false);
            deferral.Complete();
        }
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;   // keep the dialog open so they can come back after installing
        OllamaSetup.OpenDownloadPage();
        ShowStatus("Opened ollama.com — install it, then click \"Install with winget\" again or reopen Varys.",
            InfoBarSeverity.Informational);
    }

    /// <summary>Returns true when setup is fully complete (dialog may close).</summary>
    private async Task<bool> InstallOllamaAsync()
    {
        if (_winget is null)
            return false;

        ShowStatus("Installing Ollama… this can take a minute.", InfoBarSeverity.Informational);
        var code = await OllamaSetup.InstallViaWingetAsync(_winget);
        if (code != 0)
        {
            ShowStatus("The winget install didn't complete. You can use the ollama.com button instead.",
                InfoBarSeverity.Error);
            return false;
        }

        ShowStatus("Waiting for Ollama to start…", InfoBarSeverity.Informational);
        await OllamaSetup.WaitUntilReachableAsync(TimeSpan.FromSeconds(30));

        // Advance to model-download if needed.
        _ollama = OllamaSetup.FindOllama();
        _missing = await OllamaSetup.MissingModelsAsync();
        if (_missing.Count == 0)
        {
            ShowStatus("Ollama is ready.", InfoBarSeverity.Success);
            return true;
        }
        _stage = Stage.PullModels;
        ShowStatus("Ollama installed. Next: download the models.", InfoBarSeverity.Success);
        return false;
    }

    /// <summary>Returns true when all required models are present (dialog may close).</summary>
    private async Task<bool> PullModelsAsync()
    {
        if (_ollama is null)
        {
            ShowStatus("Couldn't find the Ollama CLI. Reopen Varys after Ollama is running.",
                InfoBarSeverity.Error);
            return false;
        }

        foreach (var model in _missing.ToList())
        {
            ShowStatus($"Downloading {model}…", InfoBarSeverity.Informational);
            var code = await OllamaSetup.PullModelAsync(_ollama, model);
            if (code != 0)
            {
                ShowStatus($"Couldn't download {model}. See the log for details.", InfoBarSeverity.Error);
                _missing = await OllamaSetup.MissingModelsAsync();
                return false;
            }
        }

        _missing = await OllamaSetup.MissingModelsAsync();
        if (_missing.Count == 0)
        {
            ShowStatus("All set — summaries and search are ready.", InfoBarSeverity.Success);
            return true;
        }
        return false;
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !busy;
        IsSecondaryButtonEnabled = !busy;
        // Let the user dismiss only when idle.
        CloseButtonText = busy ? "" : "Not now";
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
