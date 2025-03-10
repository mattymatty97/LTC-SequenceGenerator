@echo off
setlocal enabledelayedexpansion

echo Mermaid to SVG Converter Launcher
echo ---------------------------------

:: Check if any files were dropped
if "%~1"=="" (
  echo No files were dropped.
  echo Please drag and drop .mmd files onto this batch file.
  echo.
  pause
  exit /b
)

:: Create a temporary file to hold the list of files
set "tempFile=%TEMP%\mmd_files_list.txt"
if exist "%tempFile%" del "%tempFile%"

:: Process all dropped files
echo Preparing to process files...
for %%F in (%*) do (
  echo "%%~fF" >> "%tempFile%"
)

:: Run PowerShell script with the file list
echo Running converter...
echo.
powershell.exe -File "%~dp0mermaid.ps1" -FilesListPath "%tempFile%"

:: Clean up
if exist "%tempFile%" del "%tempFile%"

echo.
echo Process completed.
pause