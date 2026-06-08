using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Varys;

/// <summary>
/// Provisions the Python transcription engine — Python 3.13 + CUDA PyTorch + the ASR stack.
/// It is not a single binary: uv assembles it from the PyTorch CUDA index, PyPI, and a git
/// build of transformers. Both the sidecar launcher and the first-run welcome use this to
/// detect whether the engine is ready and to build it on demand (into a per-user venv, so no
/// elevation is needed even when the app is installed in Program Files).
/// </summary>
public static class EngineSetup
{
    /// <summary>Locate the sidecar source directory (the one containing pyproject.toml), or null.</summary>
    public static string? FindSidecarDir()
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
        return null;
    }

    /// <summary>Per-user environment directory built by uv (no admin required).</summary>
    public static string VenvDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Varys", "venv");

    /// <summary>The engine's python.exe if it's already built (dev .venv preferred), else null.</summary>
    public static string? ReadyPython()
    {
        var sidecar = FindSidecarDir();
        if (sidecar != null)
        {
            var devPython = Path.Combine(sidecar, ".venv", "Scripts", "python.exe");
            if (File.Exists(devPython))
                return devPython;
        }
        var python = Path.Combine(VenvDir, "Scripts", "python.exe");
        return File.Exists(python) ? python : null;
    }

    /// <summary>True when the engine is built and ready to launch.</summary>
    public static bool IsReady => ReadyPython() != null;

    /// <summary>Locate uv.exe (bundled next to the app, a per-user install, or PATH), or null.</summary>
    public static string? FindUv()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "uv.exe"),                 // bundled next to the app (installed/release)
            Path.Combine(baseDir, "sidecar", "uv.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "uv.exe"),
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir))
                candidates.Add(Path.Combine(dir, "uv.exe"));
        foreach (var c in candidates)
            try { if (File.Exists(c)) return c; } catch { }
        return null;
    }

    /// <summary>
    /// Build the engine with <c>uv sync</c> into the per-user venv (downloads PyTorch + the ASR
    /// stack). Returns the python.exe path on success, or null on failure. Output streams to the
    /// app log and to <paramref name="log"/>.
    /// </summary>
    public static async Task<string?> BuildAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        var sidecar = FindSidecarDir();
        if (sidecar is null)
        {
            log?.Report("Could not locate the engine source (sidecar).");
            return null;
        }
        var uv = FindUv();
        if (uv is null)
        {
            log?.Report("Could not find uv to build the engine.");
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = uv,
            WorkingDirectory = sidecar,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["UV_PROJECT_ENVIRONMENT"] = VenvDir;
        psi.ArgumentList.Add("sync");
        try
        {
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) { AppLog.Write($"[uv] {e.Data}"); log?.Report(e.Data); } };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) { AppLog.Write($"[uv] {e.Data}"); log?.Report(e.Data); } };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            if (p.ExitCode == 0 && ReadyPython() is { } py)
                return py;
            log?.Report("uv sync did not complete — see the log.");
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write($"engine build failed: {ex.Message}");
            log?.Report(ex.Message);
            return null;
        }
    }
}
