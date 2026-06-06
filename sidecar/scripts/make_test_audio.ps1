# Generate clean speech WAVs for the smoke test using Windows SAPI TTS.
# Output: sidecar/samples/test_en.wav (+ test_sv.wav if a Swedish voice is installed).
param([string]$OutDir = (Join-Path $PSScriptRoot "..\samples"))
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Speech
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Save-Speech([string]$text, [string]$path, [string]$culture) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    if ($culture) {
        $voice = $synth.GetInstalledVoices() |
            Where-Object { $_.VoiceInfo.Culture.Name -like "$culture*" -and $_.Enabled } |
            Select-Object -First 1
        if (-not $voice) {
            Write-Host "No '$culture' voice installed; skipping $(Split-Path $path -Leaf)"
            $synth.Dispose(); return
        }
        $synth.SelectVoice($voice.VoiceInfo.Name)
    }
    $synth.SetOutputToWaveFile($path)
    $synth.Speak($text)
    $synth.Dispose()
    Write-Host "wrote $path"
}

Save-Speech "Hello, this is a transcription smoke test recorded on the desktop machine." (Join-Path $OutDir "test_en.wav") "en"
Save-Speech "Hej, det här är ett transkriberingstest som spelats in på skrivbordsdatorn." (Join-Path $OutDir "test_sv.wav") "sv"
Get-ChildItem $OutDir -Filter *.wav | Select-Object Name, Length
