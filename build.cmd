@echo off
rem Builds bin\SwitchPeek.exe and the installer bin\SwitchPeekSetup.exe
rem with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found. .NET Framework 4.x is required.
    exit /b 1
)
cd /d "%~dp0"
if not exist bin mkdir bin

set "COMMON=/nologo /target:winexe /optimize /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /win32icon:assets\SwitchPeek.ico /resource:assets\SwitchPeek.ico,SwitchPeek.ico"

"%CSC%" %COMMON% /out:bin\SwitchPeek.exe src\SwitchPeek.cs src\Shared.cs
if errorlevel 1 exit /b 1

"%CSC%" %COMMON% /resource:bin\SwitchPeek.exe,SwitchPeek.exe /out:bin\SwitchPeekSetup.exe src\Setup.cs src\Shared.cs
if errorlevel 1 exit /b 1

echo Built bin\SwitchPeek.exe and bin\SwitchPeekSetup.exe
