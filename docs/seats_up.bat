@echo off
REM seats_up.bat — re-seat local hosts (commission_seat.py) for MISSING seats only.
REM Waits for LM Studio /v1/models first — the brain must answer before chairs
REM fill (hosts exit at the resolve gate otherwise). Dedup: live hosts skipped.
REM Workflow: LM Studio on -> model server started -> run this -> six seats connect.
setlocal
set DIR=%~dp0

echo [seats_up] waiting for LM Studio at :1234 ...
set /a TRIES=0
:waitlm
curl -s --max-time 3 http://localhost:1234/v1/models 2>nul | findstr /c:"\"id\"" >nul
if not errorlevel 1 goto lmonline
set /a TRIES+=1
if %TRIES% geq 20 (
    echo [seats_up] LM Studio still dark after ~60s — start the model server, re-run.
    exit /b 1
)
timeout /t 3 /nobreak >nul
goto waitlm
:lmonline
echo [seats_up] LM Studio answering — seating missing hosts
for %%S in (CHAIR ARCHITECT INFORMANT AUDITOR BUTTON DESIGNER) do (
    wmic process where "CommandLine like '%%commission_seat.py%%' and CommandLine like '%% %%S%%'" get ProcessId 2>nul | findstr /r "[0-9]" >nul
    if errorlevel 1 (
        start "" /min pythonw -X utf8 "%DIR%commission_seat.py" %%S
        echo [seats_up] %%S host spawned
        timeout /t 2 /nobreak >nul
    ) else (
        echo [seats_up] %%S host already alive — skipped
    )
)
echo [seats_up] done — check presence_*.js lamps
endlocal
