# Runs the investigate-then-fix chain (region 12 of Program.cs) several times and keeps the evidence of each attempt.
#
# Usage (from anywhere):
#   .\scripts\run_fix_n_times.ps1 -Test Reset_clears_cache -Model qwen2.5:3b -Runs 5
#
# Needs Ollama running and the model pulled. For every attempt it saves
#   TestResults/fix-runs/<model>/fix_<test>_<n>.txt    the whole log
#   TestResults/fix-runs/<model>/fix_<test>_<n>.diff   what the agent actually changed under sandbox/ (git diff)
# (TestResults is git-ignored). The .diff file is the evidence you judge: a test that passes 20 times because its
# check was weakened is not fixed, and the pass rate cannot tell the difference.
#
# An attempt EDITS sandbox/, which is the test bed for every scored run. So this script
#   1. refuses to start if sandbox/ has uncommitted changes (it would destroy them),
#   2. runs `git checkout -- sandbox/` after every attempt (local only, nothing is pushed),
#   3. stops at once if sandbox/ is still not clean afterwards.
#
# -SelfTest does not run the agent. It appends a harmless comment to a sandbox file instead, to check that the diff is
# captured and the restore works, without using any model time.
param(
    [string] $Test = "Reset_clears_cache",
    [string] $Model = "qwen2.5:3b",
    [int] $Runs = 5,
    [string] $OutDir = "",
    [switch] $SelfTest
)

# work from the repo root, wherever the script is started
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not $OutDir) { $OutDir = "TestResults/fix-runs/" + ($Model -replace '[:/\\]', '_') }

function Test-SandboxClean {
    # tracked changes and new files both count
    $status = git status --porcelain -- sandbox/
    return -not $status
}

if (-not (Test-SandboxClean)) {
    Write-Host "sandbox/ has uncommitted changes. Commit them, or restore with: git checkout -- sandbox/"
    Write-Host "Nothing was run: this script restores sandbox/ after every attempt and would destroy them."
    exit 1
}

# the agent launches the built server dll, so build everything once
dotnet build --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed, nothing was run."; exit 1 }

New-Item -ItemType Directory -Force $OutDir | Out-Null

$env:FLAKYLAB_MODE = "fix"
$env:FLAKYLAB_TEST = $Test
$env:FLAKYLAB_MODEL = $Model

for ($i = 1; $i -le $Runs; $i++) {
    $log = Join-Path $OutDir "fix_${Test}_$i.txt"
    $diff = Join-Path $OutDir "fix_${Test}_$i.diff"

    Write-Host ""
    Write-Host "=== $Test with $Model, attempt $i of $Runs -> $log"

    if ($SelfTest) {
        Add-Content -Path "sandbox/FlakyLab/FlakyLabTests.cs" -Value "// self-test edit, attempt $i"
        Set-Content -Path $log -Value "self test, the agent was not run"
    }
    else {
        # cmd handles the redirect, so stdout and stderr both land in the file without PowerShell wrapping stderr
        cmd /c "dotnet run --project src/FlakyDetective.Agent --no-build > ""$log"" 2>&1"
    }

    # keep what the agent changed, then put the sandbox back
    cmd /c "git diff -- sandbox/ > ""$diff"""
    git checkout -- sandbox/

    if (-not (Test-SandboxClean)) {
        Write-Host "RESTORE FAILED: sandbox/ is not clean after attempt $i. Stopping so nothing is scored on a changed test bed."
        git status --short -- sandbox/
        exit 1
    }

    $changed = @(Select-String -Path $diff -Pattern '^[+-][^+-]').Count
    Write-Host "  sandbox restored; the attempt changed $changed line(s), saved in $diff"

    Select-String -Path $log -Pattern '^(Fix mode|Hand-off failed|Fix task|Nudge|Stopped|MEASURED|UNVERIFIED|INCOMPLETE|APPLIED EDIT|Unhandled)' |
        ForEach-Object { $_.Line.Trim() } |
        Select-Object -Unique |
        ForEach-Object { "  $_" }
}

Remove-Item Env:FLAKYLAB_MODE, Env:FLAKYLAB_TEST, Env:FLAKYLAB_MODEL -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Logs and diffs saved in $OutDir. sandbox/ is clean."
Write-Host "Read each .diff: a pass that came from weakening or removing a check is not a fix."
