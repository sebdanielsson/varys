using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Varys;

/// <summary>
/// Detects the local <b>Ollama</b> install — which Varys uses for meeting summaries and
/// semantic search — and helps the user set it up (install via winget, pull the required
/// models). All detection is over Ollama's local HTTP API; nothing leaves the machine.
/// The model ids mirror the sidecar's config (<c>summary_model</c> / <c>embed_model</c>).
/// </summary>
public static class OllamaSetup
{
    public const string SummaryModel = "gemma4:e2b";       // sidecar config.py: summary_model
    public const string EmbedModel = "embeddinggemma";     // sidecar config.py: embed_model
    public const string DownloadUrl = "https://ollama.com/download";
    public const string WingetId = "Ollama.Ollama";

    private const string BaseUrl = "http://127.0.0.1:11434";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>True when the Ollama HTTP API answers on its default local port.</summary>
    public static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var res = await Http.GetAsync($"{BaseUrl}/api/version");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Model tags currently pulled into the local Ollama (empty if unreachable).</summary>
    public static async Task<IReadOnlyList<string>> InstalledModelsAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"{BaseUrl}/api/tags");
            using var doc = JsonDocument.Parse(json);
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var arr))
                foreach (var m in arr.EnumerateArray())
                    if (m.TryGetProperty("name", out var n) && n.GetString() is { } s)
                        names.Add(s);
            return names;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Is <paramref name="model"/> present in <paramref name="tags"/> (tolerating a :latest/:tag suffix)?</summary>
    public static bool HasModel(IEnumerable<string> tags, string model) =>
        tags.Any(t => string.Equals(t, model, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, model + ":latest", StringComparison.OrdinalIgnoreCase)
                   || (!model.Contains(':') && t.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The required models that aren't pulled yet (in display order).</summary>
    public static async Task<List<string>> MissingModelsAsync()
    {
        var have = await InstalledModelsAsync();
        return new[] { SummaryModel, EmbedModel }.Where(m => !HasModel(have, m)).ToList();
    }

    /// <summary>Locate winget (the App Installer execution alias), or null if not present.</summary>
    public static string? FindWinget() => Locate("winget.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe"));

    /// <summary>Locate the Ollama CLI (default per-user install path), or null if not present.</summary>
    public static string? FindOllama() => Locate("ollama.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe"));

    private static string? Locate(string exe, params string[] hints)
    {
        foreach (var h in hints)
            try { if (File.Exists(h)) return h; } catch { }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try { var p = Path.Combine(dir, exe); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>Run a console tool to completion, mirroring its output to the app log. Returns the exit code (-1 on failure to start).</summary>
    public static async Task<int> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        var tag = Path.GetFileNameWithoutExtension(exe);
        try
        {
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) AppLog.Write($"[{tag}] {e.Data}"); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) AppLog.Write($"[{tag}] {e.Data}"); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            AppLog.Write($"run {exe} failed: {ex.Message}");
            return -1;
        }
    }

    /// <summary>Install Ollama via winget (per-user, silent).</summary>
    public static Task<int> InstallViaWingetAsync(string winget, CancellationToken ct = default) =>
        RunAsync(winget, new[]
        {
            "install", "--id", WingetId, "-e", "--source", "winget", "--silent",
            "--accept-source-agreements", "--accept-package-agreements",
        }, ct);

    /// <summary>Pull a single model into Ollama (<c>ollama pull &lt;model&gt;</c>).</summary>
    public static Task<int> PullModelAsync(string ollama, string model, CancellationToken ct = default) =>
        RunAsync(ollama, new[] { "pull", model }, ct);

    /// <summary>Poll the API until it answers (after an install starts the service), up to <paramref name="timeout"/>.</summary>
    public static async Task<bool> WaitUntilReachableAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsReachableAsync())
                return true;
            try { await Task.Delay(1000, ct); }
            catch { return false; }
        }
        return await IsReachableAsync();
    }

    /// <summary>Open the Ollama download page in the user's default browser.</summary>
    public static void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = DownloadUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"open {DownloadUrl} failed: {ex.Message}");
        }
    }
}
