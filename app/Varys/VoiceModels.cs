using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Varys;

/// <summary>
/// The speech-to-text ("voice") models — HuggingFace models the sidecar loads (Parakeet via
/// transformers for English, KB-Whisper via faster-whisper for Swedish) and caches in the HF hub
/// cache. The welcome lets the user download at least one. Repo ids mirror the sidecar config
/// (model_id / whisper_model_sv).
/// </summary>
public static class VoiceModels
{
    public sealed record Model(string Key, string Title, string Caption, string RepoId, bool FasterWhisper);

    public static readonly Model English = new(
        "en", "English (Parakeet)", "Speech-to-text for English", "nvidia/parakeet-tdt-0.6b-v3", FasterWhisper: false);

    public static readonly Model Swedish = new(
        "sv", "Swedish (KB-Whisper)", "Speech-to-text for Swedish", "KBLab/kb-whisper-large", FasterWhisper: true);

    public static readonly Model[] All = { English, Swedish };

    /// <summary>True if at least one speech model is present in the cache.</summary>
    public static bool AnyPresent() => All.Any(IsPresent);

    /// <summary>Is this model present (downloaded) in the HuggingFace hub cache?</summary>
    public static bool IsPresent(Model m)
    {
        try
        {
            var snapshots = Path.Combine(HubCacheDir(), "models--" + m.RepoId.Replace("/", "--"), "snapshots");
            if (!Directory.Exists(snapshots))
                return false;
            // A real snapshot has at least one file (HF stores blobs/symlinks under the revision).
            foreach (var snap in Directory.EnumerateDirectories(snapshots))
                if (Directory.EnumerateFiles(snap, "*", SearchOption.AllDirectories).Any())
                    return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Download a speech model via the engine's Python — HF <c>snapshot_download</c> for Parakeet,
    /// faster-whisper's <c>download_model</c> for KB-Whisper. Needs the engine to be set up first.
    /// </summary>
    public static Task<bool> DownloadAsync(Model m, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var code = m.FasterWhisper
            ? $"from faster_whisper import download_model; download_model('{m.RepoId}'); print('voice model ready')"
            : $"from huggingface_hub import snapshot_download; snapshot_download('{m.RepoId}'); print('voice model ready')";
        return EngineSetup.RunPythonAsync(new[] { "-c", code }, log, ct);
    }

    /// <summary>The HuggingFace hub cache directory (respects HF_HUB_CACHE / HF_HOME).</summary>
    private static string HubCacheDir()
    {
        var hubCache = Environment.GetEnvironmentVariable("HF_HUB_CACHE");
        if (!string.IsNullOrEmpty(hubCache))
            return hubCache;
        var hfHome = Environment.GetEnvironmentVariable("HF_HOME");
        if (!string.IsNullOrEmpty(hfHome))
            return Path.Combine(hfHome, "hub");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "huggingface", "hub");
    }
}
