<#
.SYNOPSIS
    Verifies that all .mid files in this folder open via Windows MCI sequencer
    and reports their playback length.

.DESCRIPTION
    For every .mid file matching the optional pattern, this script:
      1. Opens it as an MCI sequencer device (the same path the Noted MIDI Player
         plugin uses internally via winmm.dll's mciSendString).
      2. Queries its length in milliseconds.
      3. Reports MM:SS plus an OK / SHORT marker against MinSeconds.

    Exit code is 0 if all files open successfully, otherwise the number of
    failures.

.PARAMETER Pattern
    Filename glob to filter. Defaults to "*.mid". Examples: "Jazz*.mid",
    "Disco*.mid", "Flute*.mid".

.PARAMETER MinSeconds
    Minimum acceptable length in seconds for the OK marker. Defaults to 120
    (2 minutes). Files shorter than this are flagged "SHORT".

.EXAMPLE
    powershell -File verify.ps1
    # Verifies every .mid file in this folder.

.EXAMPLE
    powershell -File verify.ps1 "Flute*.mid"
    # Only verifies files whose name starts with "Flute".

.EXAMPLE
    powershell -File verify.ps1 -MinSeconds 90
    # Lower the threshold to 1:30 for the SHORT/OK marker.

.NOTES
    Requires Windows (uses winmm.dll). Works under Windows PowerShell 5.x or
    PowerShell 7+. The script lives in the same folder as the .mid files it
    verifies; relocating it requires only copying along.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Pattern = '*.mid',

    [int]$MinSeconds = 120
)

$ErrorActionPreference = 'Stop'

$sig = @'
[DllImport("winmm.dll", CharSet=CharSet.Unicode, EntryPoint="mciSendStringW")]
public static extern int Mci(string s, System.Text.StringBuilder r, int l, System.IntPtr h);
'@
$mci = Add-Type -MemberDefinition $sig -Name 'M' -Namespace 'NotedMci' -PassThru

$dir = Resolve-Path "$PSScriptRoot"
$files = Get-ChildItem -Path $dir -Filter $Pattern -File | Sort-Object Name
$minMs = [int64]($MinSeconds * 1000)

if ($files.Count -eq 0) {
    Write-Host "No files matched '$Pattern' in $dir" -ForegroundColor Yellow
    exit 0
}

Write-Host ("Verifying {0} file(s) in {1}" -f $files.Count, $dir)
Write-Host ("Threshold: {0}s ({1:00}:{2:D2})" -f $MinSeconds,
    [int]([math]::Floor($MinSeconds / 60)),
    [int]($MinSeconds % 60))
Write-Host ""

$totalMs = [int64]0
$okCount = 0
$shortCount = 0
$failCount = 0

foreach ($f in $files) {
    $alias = 'verify_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $cmd = 'open "' + $f.FullName + '" type sequencer alias ' + $alias
    $err = $mci::Mci($cmd, $null, 0, [IntPtr]::Zero)
    if ($err -ne 0) {
        Write-Host ("  {0,-50} OPEN FAILED  err={1}" -f $f.Name, $err) -ForegroundColor Red
        $failCount++
        continue
    }

    [void]$mci::Mci("set $alias time format milliseconds", $null, 0, [IntPtr]::Zero)
    $sb = New-Object System.Text.StringBuilder 64
    [void]$mci::Mci("status $alias length", $sb, 64, [IntPtr]::Zero)
    $lenMs = [int64]$sb.ToString().Trim()
    [void]$mci::Mci("close $alias", $null, 0, [IntPtr]::Zero)

    $totalMs += $lenMs
    $mm = [int]([math]::Floor($lenMs / 60000))
    $ss = [int]([math]::Floor(($lenMs % 60000) / 1000))

    if ($lenMs -ge $minMs) {
        $marker = 'OK'
        $color = 'Green'
        $okCount++
    }
    else {
        $marker = 'SHORT'
        $color = 'Yellow'
        $shortCount++
    }
    Write-Host ("  {0,-50} {1,2}:{2:D2}  {3}" -f $f.Name, $mm, $ss, $marker) -ForegroundColor $color
}

$totMin = [int]([math]::Floor($totalMs / 60000))
$totSec = [int]([math]::Floor(($totalMs % 60000) / 1000))

Write-Host ""
Write-Host ("Summary: {0} OK, {1} SHORT, {2} FAILED  |  combined {3:00}:{4:D2}" -f
    $okCount, $shortCount, $failCount, $totMin, $totSec)

exit $failCount
