@echo off
REM seat_panel.bat — standalone seat control/diagnosis window (Tkinter, stdlib).
REM Independent of the bus it watches — keeps telling the truth when all else is dark.
start "" /min pythonw -X utf8 "%~dp0seat_panel.py"
