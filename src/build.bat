@echo off
rem Builds ..\Watermark.exe from Watermark.cs with the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:app.ico /out:..\Watermark.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll Watermark.cs
if errorlevel 1 (
	echo.
	echo Build failed.
	pause
	exit /b 1
)
echo Built ..\Watermark.exe
