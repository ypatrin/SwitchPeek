@echo off
rem Builds bin\SwitchPeek.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found. .NET Framework 4.x is required.
    exit /b 1
)
cd /d "%~dp0"
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /optimize /out:bin\SwitchPeek.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\SwitchPeek.cs
if errorlevel 1 exit /b 1
echo Built bin\SwitchPeek.exe
