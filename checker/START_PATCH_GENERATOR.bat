@echo off
cd /d "%~dp0"
py rimworld_patch_generator.py
if errorlevel 1 pause
