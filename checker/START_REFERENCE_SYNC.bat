@echo off
cd /d "%~dp0"
py reference_sync_gui.py
if errorlevel 1 pause
