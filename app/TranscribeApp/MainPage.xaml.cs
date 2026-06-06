using System;
using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace TranscribeApp;

/// <summary>
/// The main content page: language + start/stop controls, a live transcript list,
/// and an updating partial line. Drives the <see cref="SidecarClient"/>.
/// </summary>
public sealed partial class MainPage : Page
{
    public ObservableCollection<CaptionItem> Items { get; } = new();

    private readonly SolidColorBrush _meBrush = new(ColorHelper.FromArgb(255, 0x4F, 0xC3, 0xF7));   // blue
    private readonly SolidColorBrush _themBrush = new(ColorHelper.FromArgb(255, 0xA5, 0xD6, 0xA7)); // green

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
            StartStopButton.IsEnabled = true;
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
                    Items.Add(new CaptionItem
                    {
                        Speaker = ev.Speaker ?? "",
                        Text = ev.Text ?? "",
                        Color = ev.Speaker == "Me" ? _meBrush : _themBrush,
                    });
                    Captions.ScrollIntoView(Items[^1]);
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
