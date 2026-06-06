using System;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;

namespace TranscribeApp;

/// <summary>Meeting library: browse / search, view + edit transcript and notes.</summary>
public sealed partial class MeetingsPage : Page
{
    public ObservableCollection<MeetingMeta> Meetings { get; } = new();
    public ObservableCollection<SearchHit> Results { get; } = new();
    public ObservableCollection<CaptionItem> DetailUtterances { get; } = new();

    private string? _currentId;
    private string? _currentDir;
    private string _summaryMd = "";
    private string _transcriptMd = "";
    private bool _notesEditing;
    private bool _transcriptEditing;

    private const int GlyphEdit = 0xE70F;
    private const int GlyphSave = 0xE74E;

    public MeetingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        NotesWeb.DefaultBackgroundColor = Colors.Transparent;
        NotesWeb.NavigationCompleted += OnNotesRendered;
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
            _summaryMd = detail.Summary ?? "";
            _transcriptMd = detail.TranscriptMd ?? "";
            DetailTitle.Text = detail.Meta.Title;
            DetailSubtitle.Text = detail.Meta.Subtitle + (detail.Meta.Indexed ? "   ·   indexed" : "");
            DetailUtterances.Clear();
            foreach (var u in detail.Transcript.Utterances)
                DetailUtterances.Add(CaptionItem.For(u.Speaker, u.Text));

            ResetEditModes();
            RenderNotes();
            SummaryCard.Visibility = Visibility.Visible;
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailContent.Visibility = Visibility.Visible;
        }
        catch
        {
            // leave current detail on error
        }
    }

    // --- notes / transcript edit + preview ---

    private void ResetEditModes()
    {
        _notesEditing = false;
        _transcriptEditing = false;
        NotesEdit.Visibility = Visibility.Collapsed;
        NotesWeb.Visibility = Visibility.Visible;
        TranscriptEdit.Visibility = Visibility.Collapsed;
        DetailTranscript.Visibility = Visibility.Visible;
        SetIcon(NotesEditBtn, GlyphEdit);
        SetIcon(TranscriptEditBtn, GlyphEdit);
        ToolTipService.SetToolTip(NotesEditBtn, "Edit notes");
        ToolTipService.SetToolTip(TranscriptEditBtn, "Edit transcript");
    }

    private async void RenderNotes()
    {
        try
        {
            await NotesWeb.EnsureCoreWebView2Async();
            NotesWeb.NavigateToString(MarkdownRenderer.ToHtml(_summaryMd, ActualTheme));
        }
        catch
        {
            // WebView2 runtime missing etc. — leave the card empty
        }
    }

    private async void OnNotesRendered(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            var h = await NotesWeb.ExecuteScriptAsync("document.body.scrollHeight");
            if (int.TryParse(h, out var px))
                NotesWeb.Height = Math.Clamp(px + 8, 40, 600);
        }
        catch
        {
        }
    }

    private async void OnToggleNotesEdit(object sender, RoutedEventArgs e)
    {
        if (_currentId is null)
            return;
        if (!_notesEditing)
        {
            NotesEdit.Text = _summaryMd;
            NotesEdit.Visibility = Visibility.Visible;
            NotesWeb.Visibility = Visibility.Collapsed;
            SetIcon(NotesEditBtn, GlyphSave);
            ToolTipService.SetToolTip(NotesEditBtn, "Save notes");
            _notesEditing = true;
        }
        else
        {
            _summaryMd = NotesEdit.Text;
            await App.Sidecar.SaveNotesAsync(_currentId, _summaryMd);
            NotesEdit.Visibility = Visibility.Collapsed;
            NotesWeb.Visibility = Visibility.Visible;
            RenderNotes();
            SetIcon(NotesEditBtn, GlyphEdit);
            ToolTipService.SetToolTip(NotesEditBtn, "Edit notes");
            _notesEditing = false;
            await RefreshMeetingsAsync();
        }
    }

    private async void OnToggleTranscriptEdit(object sender, RoutedEventArgs e)
    {
        if (_currentId is null)
            return;
        if (!_transcriptEditing)
        {
            TranscriptEdit.Text = _transcriptMd;
            TranscriptEdit.Visibility = Visibility.Visible;
            DetailTranscript.Visibility = Visibility.Collapsed;
            SetIcon(TranscriptEditBtn, GlyphSave);
            ToolTipService.SetToolTip(TranscriptEditBtn, "Save transcript");
            _transcriptEditing = true;
        }
        else
        {
            DetailRing.IsActive = true;
            try
            {
                await App.Sidecar.SaveTranscriptAsync(_currentId, TranscriptEdit.Text);
                TranscriptEdit.Visibility = Visibility.Collapsed;
                DetailTranscript.Visibility = Visibility.Visible;
                SetIcon(TranscriptEditBtn, GlyphEdit);
                ToolTipService.SetToolTip(TranscriptEditBtn, "Edit transcript");
                _transcriptEditing = false;
                await LoadDetailAsync(_currentId);   // reload badges from the re-derived transcript
            }
            finally
            {
                DetailRing.IsActive = false;
            }
        }
    }

    private static void SetIcon(Button button, int codepoint)
    {
        if (button.Content is FontIcon icon)
            icon.Glyph = char.ConvertFromUtf32(codepoint);
    }

    // --- search ---

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

    // --- actions ---

    private async void OnSummarizeMeeting(object sender, RoutedEventArgs e)
    {
        if (_currentId is null)
            return;
        DetailRing.IsActive = true;
        try
        {
            using var doc = JsonDocument.Parse(await App.Sidecar.SummarizeMeetingAsync(_currentId));
            if (doc.RootElement.GetProperty("status").GetString() == "ok")
            {
                _summaryMd = doc.RootElement.GetProperty("summary").GetString() ?? "";
                RenderNotes();
                await RefreshMeetingsAsync();
            }
        }
        catch
        {
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
        }
    }
}
