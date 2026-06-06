using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Varys;

/// <summary>One transcription event from the sidecar WebSocket.</summary>
public sealed class TranscriptEvent
{
    public string Type { get; set; } = "";
    public string? State { get; set; }
    public string? Speaker { get; set; }
    public string? Text { get; set; }
    public double T0 { get; set; }
    public double T1 { get; set; }
    public string? Language { get; set; }
    public string? Engine { get; set; }
    public string? Mic { get; set; }
    public string? Loopback { get; set; }
}

/// <summary>
/// Launches and supervises the Python sidecar, drives its REST control endpoints,
/// and streams transcription events from its WebSocket.
/// </summary>
public sealed class SidecarClient : IAsyncDisposable
{
    private const string BaseUrl = "http://127.0.0.1:8765";
    private const string WsUrl = "ws://127.0.0.1:8765/ws";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private Process? _process;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private readonly JobObject _job = new();

    public event Action<TranscriptEvent>? Event;
    public event Action<string>? Log;

    /// <summary>Start (or reuse) the sidecar, wait until healthy, and connect the WebSocket.</summary>
    public async Task StartSidecarAsync(CancellationToken ct = default)
    {
        if (!await IsHealthyAsync(ct))
        {
            var dir = LocateSidecarDir();
            var venvPython = Path.Combine(dir, ".venv", "Scripts", "python.exe");
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = dir,
            };
            if (File.Exists(venvPython))
            {
                psi.FileName = venvPython;   // one process, no `uv` child-spawn / PATH dependency
            }
            else
            {
                psi.FileName = "uv";
                psi.ArgumentList.Add("run");
                psi.ArgumentList.Add("python");
            }
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("transcribe_sidecar");
            Log?.Invoke($"launching sidecar: {psi.FileName}");

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
            _process.ErrorDataReceived += (_, e) => { if (e.Data != null) Log?.Invoke(e.Data); };
            _process.Start();
            try { _job.AddProcess(_process); } catch { /* job assignment best-effort */ }
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var healthy = false;
            for (var i = 0; i < 60 && !healthy; i++)
            {
                await Task.Delay(500, ct);
                healthy = await IsHealthyAsync(ct);
            }
            if (!healthy)
                throw new TimeoutException("sidecar did not become healthy in time");
        }
        Log?.Invoke("sidecar healthy");
        if (_ws is not { State: WebSocketState.Open })
            await ConnectWsAsync();
    }

    public async Task<string> StartSessionAsync(string language)
    {
        var body = new StringContent(JsonSerializer.Serialize(new { language }), Encoding.UTF8, "application/json");
        var res = await _http.PostAsync($"{BaseUrl}/session/start", body);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> StopSessionAsync()
    {
        var res = await _http.PostAsync($"{BaseUrl}/session/stop", null);
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> SummarizeAsync()
    {
        var res = await _http.PostAsync($"{BaseUrl}/session/summarize", null);
        return await res.Content.ReadAsStringAsync();
    }

    // --- meeting library ---

    public async Task<List<MeetingMeta>> GetMeetingsAsync()
    {
        var json = await _http.GetStringAsync($"{BaseUrl}/meetings");
        return JsonSerializer.Deserialize<MeetingsResponse>(json, JsonOpts)?.Meetings ?? new();
    }

    public async Task<MeetingDetail?> GetMeetingAsync(string id)
    {
        var json = await _http.GetStringAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}");
        return JsonSerializer.Deserialize<MeetingDetail>(json, JsonOpts);
    }

    public async Task<SearchResponse> SearchAsync(string query, string mode)
    {
        var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}&mode={mode}";
        return JsonSerializer.Deserialize<SearchResponse>(await _http.GetStringAsync(url), JsonOpts) ?? new();
    }

    public async Task<string> SummarizeMeetingAsync(string id)
    {
        var res = await _http.PostAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}/summarize", null);
        return await res.Content.ReadAsStringAsync();
    }

    public Task DeleteMeetingAsync(string id) =>
        _http.DeleteAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}");

    public Task RenameMeetingAsync(string id, string title) =>
        _http.PatchAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}",
            new StringContent(JsonSerializer.Serialize(new { title }), Encoding.UTF8, "application/json"));

    public Task SaveTranscriptAsync(string id, string text) =>
        _http.PutAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}/transcript",
            new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"));

    public Task SaveNotesAsync(string id, string text) =>
        _http.PutAsync($"{BaseUrl}/meetings/{Uri.EscapeDataString(id)}/notes",
            new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"));

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync($"{BaseUrl}/health", ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task ConnectWsAsync()
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(WsUrl), CancellationToken.None);
        _wsCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_wsCts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult res;
                do
                {
                    res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (res.MessageType == WebSocketMessageType.Close)
                        return;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                } while (!res.EndOfMessage);

                var ev = JsonSerializer.Deserialize<TranscriptEvent>(sb.ToString(), JsonOpts);
                if (ev != null)
                    Event?.Invoke(ev);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke($"ws error: {ex.Message}"); }
    }

    private static string LocateSidecarDir()
    {
        var env = Environment.GetEnvironmentVariable("TRANSCRIBE_SIDECAR_DIR");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "sidecar");
            if (File.Exists(Path.Combine(candidate, "pyproject.toml")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "could not locate the sidecar/ directory; set TRANSCRIBE_SIDECAR_DIR");
    }

    public async ValueTask DisposeAsync()
    {
        try { _wsCts?.Cancel(); } catch { }
        try
        {
            if (_ws is { State: WebSocketState.Open })
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { }
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
        try { _job.Dispose(); } catch { }   // backstop: kills the sidecar if it somehow survived
        _http.Dispose();
    }
}
