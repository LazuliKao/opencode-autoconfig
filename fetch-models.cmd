@echo off
REM Fetch models list from API
REM Run F# script to get all available models

echo Running fetch-models.fsx...
echo.

dotnet fsi fetch-models.fsx

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Error: Script execution failed
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Done! You can copy the models config above to your configuration file.
echo.
pause
