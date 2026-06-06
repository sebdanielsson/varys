using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Varys;

/// <summary>Live transcription: controls, the running Me/Them transcript, partial line, summary.</summary>
public sealed partial class LivePage : Page
{
    public ObservableCollection<CaptionItem> Items { get; } = new();

    private bool _running;

    public LivePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;   // keep state across tab switches
        ToolTipService.SetToolTip(OpenLogsButton, AppLog.FilePath);
        App.Sidecar.Event += OnEvent;
        App.Sidecar.Log += OnLog;
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        StartStopButton.IsEnabled = false;
        try
        {
            if (!_running)
            {
                Items.Clear();
                EmptyState.Visibility = Visibility.Collapsed;
                PartialText.Text = "";
                SummaryExpander.Visibility = Visibility.Collapsed;
                BusyRing.IsActive = true;
                StatusText.Text = "starting sidecar…";
                await App.Sidecar.StartSidecarAsync();
                var lang = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
                StatusText.Text = "loading model…";
                await App.Sidecar.StartSessionAsync(lang);
                _running = true;
                StartStopButton.Content = "Stop";
            }
            else
            {
                StatusText.Text = "stopping…";
                await App.Sidecar.StopSessionAsync();
                _running = false;
                StartStopButton.Content = "Start";
                PartialText.Text = "";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "error";
            OnLog(ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
            StartStopButton.IsEnabled = true;
            SummarizeButton.IsEnabled = Items.Count > 0;
        }
    }

    private async void OnSummarizeClick(object sender, RoutedEventArgs e)
    {
        SummarizeButton.IsEnabled = false;
        BusyRing.IsActive = true;
        StatusText.Text = "summarizing…";
        try
        {
            using var doc = JsonDocument.Parse(await App.Sidecar.SummarizeAsync());
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() == "ok")
            {
                SummaryText.Text = root.GetProperty("summary").GetString() ?? "";
                SummaryExpander.Visibility = Visibility.Visible;
                SummaryExpander.IsExpanded = true;
                StatusText.Text = "summary ready";
            }
            else
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "no transcript";
                StatusText.Text = $"summary: {msg}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "summary error";
            OnLog(ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
            SummarizeButton.IsEnabled = Items.Count > 0;
        }
    }

    private void OnEvent(TranscriptEvent ev)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (ev.Type)
            {
                case "partial":
                    PartialText.Text = $"{ev.Speaker}   {ev.Text}";
                    break;
                case "final":
                    PartialText.Text = "";
                    EmptyState.Visibility = Visibility.Collapsed;
                    Items.Add(CaptionItem.For(ev.Speaker ?? "", ev.Text ?? ""));
                    Captions.ScrollIntoView(Items[^1]);
                    SummarizeButton.IsEnabled = true;
                    break;
                case "status":
                    StatusText.Text = ev.State switch
                    {
                        "listening" => $"listening · {ev.Engine}",
                        "stopped" => "saved · stopped",
                        "idle" => "idle",
                        _ => ev.State ?? "",
                    };
                    break;
            }
        });
    }

    private void OnLog(string msg)
    {
        AppLog.Write(msg);
        DispatcherQueue.TryEnqueue(() => LogText.Text = msg);
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppLog.Dir);
            if (!System.IO.File.Exists(AppLog.FilePath))
                System.IO.File.WriteAllText(AppLog.FilePath, "");
            // Reliable: launch Explorer with the log file selected (opening a bare
            // directory via UseShellExecute can fail).
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{AppLog.FilePath}\"");
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => LogText.Text = ex.Message);
        }
    }
}
