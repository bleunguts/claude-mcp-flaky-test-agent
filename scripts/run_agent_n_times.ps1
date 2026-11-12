# Runs the agent several times for each test and saves every log, so runs can be compared and scored.
#
# Usage (from anywhere):
#   .\scripts\run_agent_n_times.ps1 -Tests Mid_is_positive,Reset_clears_cache -Runs 3
#
# Needs Ollama running. Logs go to TestResults/agent-runs/run_<test>_<n>.txt (TestResults is git-ignored).
# After each run it prints the lines that matter: nudges, why it stopped, the tools it really called,
# what the tools measured (MEASURED), the model's claimed Failure rate, and any INCOMPLETE / UNGROUNDED
# flag. Compare the Failure rate line with the MEASURED line. Read the saved log for the full answer.
#
# Pass only the test name (-Tests Mid_is_positive), not a whole sentence: the script builds the prompt.
param(
    [Parameter(Mandatory = $true)] [string[]] $Tests,
    [int] $Runs = 3,
    [string] $OutDir = "TestResults/agent-runs"
)

# work from the repo root, wherever the script is started
Set-Location (Split-Path $PSScriptRoot -Parent)

# the agent launches the built server dll and the sandbox must be built, so build everything once
dotnet build --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed, nothing was run."; exit 1 }

New-Item -ItemType Directory -Force $OutDir | Out-Null

foreach ($test in $Tests) {
    for ($i = 1; $i -le $Runs; $i++) {
        $file = Join-Path $OutDir "run_${test}_$i.txt"
        $prompt = "Investigate the test $test."

        Write-Host ""
        Write-Host "=== $test, run $i of $Runs -> $file"

        # cmd handles the redirect, so stdout and stderr both land in the file without PowerShell
        # wrapping stderr lines as errors
        cmd /c "dotnet run --project src/FlakyDetective.Agent --no-build -- ""$prompt"" > ""$file"" 2>&1"

        Select-String -Path $file -Pattern '^(Nudge|Stopped|Tools called|MEASURED|INCOMPLETE|UNGROUNDED|INCONSISTENT|Verdict:|Failure rate:|Unhandled)' |
            ForEach-Object { $_.Line.Trim() } |
            Select-Object -Unique |
            ForEach-Object { "  $_" }
    }
}

Write-Host ""
Write-Host "Logs saved in $OutDir"
