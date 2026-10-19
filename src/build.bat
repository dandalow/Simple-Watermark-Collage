@echo off
rem Builds ..\Watermark.exe from Watermark.cs + Rename.cs + RenameList.cs + Modern.cs with the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:app.ico /out:..\Watermark.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:System.Core.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll Watermark.cs Rename.cs RenameList.cs Modern.cs
if errorlevel 1 (
	echo.
	echo Build failed.
	pause
	exit /b 1
)
echo Built ..\Watermark.exe
