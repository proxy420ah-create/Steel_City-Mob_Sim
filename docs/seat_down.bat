@echo off
REM seat_down.bat SEAT — kill one seat host (ear dies with it under the job bind,
REM or orphans+flags ORPHAN-EAR on pre-bind code). Revive with seats_up.bat.
if "%1"=="" (echo usage: seat_down.bat SEAT  ^(CHAIR^|ARCHITECT^|INFORMANT^|AUDITOR^|BUTTON^|DESIGNER^) & exit /b 1)
powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -match 'commission_seat\.py.*\s+%1\b'} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Output ('[seat_down] killed ' + $_.ProcessId) }"
