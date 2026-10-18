@echo off
rem Builds dist\TrayFolderMenu.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
rem No Visual Studio, SDK or NuGet packages are required.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: csc.exe from .NET Framework 4.x was not found.
    exit /b 1
)
if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /langversion:5 ^
    /out:dist\TrayFolderMenu.exe /win32icon:src\TrayFolderMenu.ico /win32manifest:src\app.manifest ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /r:System.Runtime.Serialization.dll /r:System.Xml.dll ^
    src\TrayFolderMenu.cs
if errorlevel 1 (
    echo ERROR: build failed.
    exit /b 1
)

rem Optional code signing with your own certificate:
rem signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 dist\TrayFolderMenu.exe

echo Built: %~dp0dist\TrayFolderMenu.exe
