@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Error: .NET Framework C# compiler not found at %CSC%
    exit /b 1
)
set OUT=StealAnEgg_AFK_Fishing_Bot.exe
if not "%~1"=="" set OUT=%~1
"%CSC%" /nologo /target:winexe /out:"%OUT%" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0StealAnEgg_AFK_Bot.cs"
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
echo Built Windows executable: %OUT%
