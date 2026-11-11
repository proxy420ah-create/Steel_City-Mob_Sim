@echo off
REM seats_down.bat — kill ALL six seat hosts AND their ears in one sweep.
REM Mailbox pulses (--mailbox) are spared — they belong to the inbox daemon.
REM Use before seats_up.bat to refresh every seat onto current code.
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object {($_.CommandLine -match 'commission_seat\.py' -or ($_.CommandLine -match 'commission_(watch|pulse)\.py' -and $_.CommandLine -notmatch '--mailbox')) -and $_.CommandLine -match '\s(CHAIR|ARCHITECT|INFORMANT|AUDITOR|BUTTON|DESIGNER)\b'} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Output ('[seats_down] killed ' + $_.ProcessId) }"
echo [seats_down] done — lamps go dark inside one TTL
