using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace TranscribeApp;

/// <summary>Meeting library: browse / search past meetings and view transcript + notes.</summary>
public sealed partial class MeetingsPage : Page
{
    public ObservableCollection<MeetingMeta> Meetings { get; } = new();
    public ObservableCollection<SearchHit> Results { get; } = new();
    public ObservableCollection<CaptionItem> DetailUtterances { get; } = new();

    private string? _currentId;
    private string? _currentDir;

    public MeetingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        try
        {
            await App.Sidecar.StartSidecarAsync();
            await RefreshMeetingsAsync();
        }
        catch
        {
            ListEmpty.Text = "Could not reach the sidecar.";
            ListEmpty.Visibility = Visibility.Visible;
        }
    }

    private async Task RefreshMeetingsAsync()
    {
        var list = await App.Sidecar.GetMeetingsAsync();
        Meetings.Clear();
        foreach (var m in list)
            Meetings.Add(m);
        if (ResultsList.Visibility != Visibility.Visible)
            ListEmpty.Visibility = Meetings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnMeetingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (MeetingsList.SelectedItem is MeetingMeta m)
            await LoadDetailAsync(m.Id);
    }

    private async void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchHit h)
            await LoadDetailAsync(h.Meeting.Id);
    }

    private async Task LoadDetailAsync(string id)
    {
        try
        {
            var detail = await App.Sidecar.GetMeetingAsync(id);
            if (detail is null || string.IsNullOrEmpty(detail.Meta.Id))
                return;
            _currentId = detail.Meta.Id;
            _currentDir = detail.Dir;
            DetailTitle.Text = detail.Meta.Title;
            DetailSubtitle.Text = detail.Meta.Subtitle + (detail.Meta.Indexed ? "   ·   indexed" : "");
            DetailUtterances.Clear();
            foreach (var u in detail.Transcript.Utterances)
                DetailUtterances.Add(CaptionItem.For(u.Speaker, u.Text));
            DetailSummary.Text = detail.Summary;
            var hasSummary = !string.IsNullOrWhiteSpace(detail.Summary);
            SummaryCard.Visibility = hasSummary ? Visibility.Visible : Visibility.Collapsed;
            SummarizeBtn.Label = hasSummary ? "Re-summarize" : "Summarize";
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailContent.Visibility = Visibility.Visible;
        }
        catch
        {
            // ignore; leave current detail
        }
    }

    private async void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var q = (sender.Text ?? "").Trim();
        if (q.Length == 0)
        {
            ShowMeetings();
            return;
        }
        SearchRing.IsActive = true;
        try
        {
            var resp = await App.Sidecar.SearchAsync(q, SemanticToggle.IsOn ? "semantic" : "keyword");
            Results.Clear();
            foreach (var h in resp.Hits)
                Results.Add(h);
            MeetingsList.Visibility = Visibility.Collapsed;
            ResultsList.Visibility = Visibility.Visible;
            ListEmpty.Text = resp.Status == "ok" ? "No matches." : (resp.Message ?? "Search failed.");
            ListEmpty.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            ListEmpty.Text = "Search failed.";
            ListEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            SearchRing.IsActive = false;
        }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(sender.Text))
            ShowMeetings();
    }

    private void ShowMeetings()
    {
        ResultsList.Visibility = Visibility.Collapsed;
        MeetingsList.Visibility = Visibility.Visible;
        ListEmpty.Text = "No meetings yet.";
        ListEmpty.Visibility = Meetings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSummarizeMeeting(object sender, RoutedEventArgs e)
    {
        if (_currentId is null)
            return;
        DetailRing.IsActive = true;
        try
        {
            using var doc = JsonDocument.Parse(await App.Sidecar.SummarizeMeetingAsync(_currentId));
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() == "ok")
            {
                DetailSummary.Text = root.GetProperty("summary").GetString() ?? "";
                SummaryCard.Visibility = Visibility.Visible;
                SummarizeBtn.Label = "Re-summarize";
                await RefreshMeetingsAsync();   // refresh the has-summary glyph
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            DetailRing.IsActive = false;
        }
    }

    private async void OnDeleteMeeting(object sender, RoutedEventArgs e)
    {
        if (_currentId is null)
            return;
        var dialog = new ContentDialog
        {
            Title = "Delete meeting?",
            Content = "It will be moved to the trash folder (recoverable).",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        await App.Sidecar.DeleteMeetingAsync(_currentId);
        _currentId = null;
        DetailContent.Visibility = Visibility.Collapsed;
        DetailEmpty.Visibility = Visibility.Visible;
        await RefreshMeetingsAsync();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentDir))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _currentDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }
}
