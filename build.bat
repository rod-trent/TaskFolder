@echo off
REM TaskFolder Build Script
REM This script builds the TaskFolder application

echo ===================================
echo TaskFolder Build Script
echo ===================================
echo.

REM Check if dotnet is installed
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET 6.0 SDK or later from:
    echo https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Checking .NET version...
dotnet --version
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
echo.

REM Restore NuGet packages
echo Restoring NuGet packages...
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore packages!
    pause
    exit /b 1
)
echo.

REM Build in Release configuration
echo Building TaskFolder (Release)...
dotnet build -c Release
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
echo.

REM Publish self-contained executable
echo Publishing self-contained executable...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if %ERRORLEVEL% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)
echo.

echo ===================================
echo Build completed successfully!
echo ===================================
echo.
echo Executable location:
echo bin\Release\net6.0-windows\win-x64\publish\TaskFolder.exe
echo.
echo You can now run TaskFolder.exe
echo.

REM Ask if user wants to run the application
set /p RUNAPP="Do you want to run TaskFolder now? (Y/N): "
if /i "%RUNAPP%"=="Y" (
    start "" "bin\Release\net6.0-windows\win-x64\publish\TaskFolder.exe"
)

pause
