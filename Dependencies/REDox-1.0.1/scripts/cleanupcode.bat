@echo off
setlocal

 echo CleanupCode

set ROOT=%~dp0..
set "SOLUTION=%~dp0..\REDox.slnx"
set "JB_CMD=jb"
set "JB_GLOBAL=%USERPROFILE%\.dotnet\tools\jb.exe"

where jb >nul 2>&1
if errorlevel 1 (
    echo jb command was not found. Installing JetBrains ReSharper GlobalTools...

    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo ERROR: dotnet command was not found. Please install .NET SDK first.
        exit /b 1
    )

    dotnet tool install -g JetBrains.ReSharper.GlobalTools
    if errorlevel 1 (
        echo Install failed or the tool is already installed. Trying update...
        dotnet tool update -g JetBrains.ReSharper.GlobalTools
        if errorlevel 1 (
            echo ERROR: Failed to install or update JetBrains.ReSharper.GlobalTools.
            exit /b 1
        )
    )

    if exist "%JB_GLOBAL%" (
        set "JB_CMD=%JB_GLOBAL%"
    ) else (
        echo WARNING: jb was installed, but %JB_GLOBAL% was not found.
        echo Please make sure %%USERPROFILE%%\.dotnet\tools is added to PATH.
    )
)

"%JB_CMD%" cleanupcode "%SOLUTION%"

cd /d "%ROOT%"
git add --renormalize .

exit /b %ERRORLEVEL%
