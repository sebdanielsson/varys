using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace TranscribeApp;

/// <summary>
/// Main content page: controls, the live transcript (Me/Them badges), the updating
/// partial line, and the post-meeting summary. Drives the <see cref="SidecarClient"/>.
/// </summary>
public sealed partial class MainPage : Page
{
    public ObservableCollection<CaptionItem> Items { get; } = new();

    private readonly SolidColorBrush _meBrush = new(ColorHelper.FromArgb(255, 0x1E, 0x88, 0xE5));   // blue
    private readonly SolidColorBrush _themBrush = new(ColorHelper.FromArgb(255, 0x43, 0xA0, 0x47)); // green

    private SidecarClient _client = null!;
    private bool _running;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is SidecarClient client)
        {
            _client = client;
            _client.Event += OnEvent;
            _client.Log += OnLog;
        }
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
                await _client.StartSidecarAsync();
                var lang = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
                StatusText.Text = "loading model…";
                await _client.StartSessionAsync(lang);
                _running = true;
                StartStopButton.Content = "Stop";
            }
            else
            {
                StatusText.Text = "stopping…";
                await _client.StopSessionAsync();
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
            var json = await _client.SummarizeAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString();
            if (status == "ok")
            {
                SummaryText.Text = root.GetProperty("summary").GetString() ?? "";
                SummaryExpander.Visibility = Visibility.Visible;
                SummaryExpander.IsExpanded = true;
                StatusText.Text = "summary ready";
            }
            else
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : status;
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
                    Items.Add(new CaptionItem
                    {
                        Speaker = ev.Speaker ?? "",
                        Text = ev.Text ?? "",
                        Color = ev.Speaker == "Me" ? _meBrush : _themBrush,
                    });
                    Captions.ScrollIntoView(Items[^1]);
                    SummarizeButton.IsEnabled = true;
                    break;
                case "summary":
                    SummaryText.Text = ev.Text ?? "";
                    SummaryExpander.Visibility = Visibility.Visible;
                    SummaryExpander.IsExpanded = true;
                    break;
                case "status":
                    StatusText.Text = ev.State switch
                    {
                        "listening" => $"listening · {ev.Engine}",
                        "stopped" => "stopped",
                        "idle" => "idle",
                        _ => ev.State ?? "",
                    };
                    break;
            }
        });
    }

    private void OnLog(string msg) => DispatcherQueue.TryEnqueue(() => LogText.Text = msg);
}
