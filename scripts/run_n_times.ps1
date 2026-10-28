dotnet build sandbox/FlakyLab -v q
1..10 | ForEach-Object { dotnet test sandbox/FlakyLab --no-build --nologo -v q --logger "trx;LogFileName=run$_.trx" --results-directory "TestResults/run$_" | Out-Null }
Select-String -Path "TestResults/run*/*.trx" -Pattern 'testName="([^"]*)"[^>]*outcome="Failed"' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Group-Object | Select-Object Count, Name