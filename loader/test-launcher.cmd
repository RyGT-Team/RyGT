@echo off
rem FEAT-GTS-290: builds RyGTLauncher.exe (GT Sport and GT7) and runs the launcher's checks: the unified launcher's
rem (UnifiedLauncherTests.cs), GT7's Set up (GT7SetupTests.cs, from ryGT7's test-gt7.cmd), logging and settings
rem (LoggingTests.cs) and the render scale (shader-cache-tests\RenderScaleTests.cs).
rem Usage: test-launcher.cmd <output folder> [GT7SetupTests arguments: <ELF> <verified ELF> or --offline ...]
setlocal
set OUT=%~1
if "%OUT%"=="" exit /b 2
if not exist "%OUT%" mkdir "%OUT%"
for %%I in ("%OUT%") do set OUT=%%~fI
set TEMP=%OUT%\temp
set TMP=%TEMP%
if not exist "%TEMP%" mkdir "%TEMP%"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Xml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Security.dll
rem The launcher's sources, as in build.cmd (a file missing here fails the test build).
set SOURCES="%~dp0RenderResolution.cs" "%~dp0ProSettings.cs" "%~dp0VulkanDevices.cs" "%~dp0Version.cs" "%~dp0GameTarget.cs" "%~dp0RyGTLoader.cs" "%~dp0GtsPages.cs" "%~dp0Gt7Pages.cs" "%~dp0ControlsForm.cs" "%~dp0PortsForm.cs" "%~dp0Logging.cs" "%~dp0WheelForm.cs" "%~dp0FeelForm.cs" "%~dp0CustomEnvironment.cs" "%~dp0AdvancedPage.cs" "%~dp0Updater.cs" "%~dp0ObsoleteSettings.cs" "%~dp0GT7Setup.cs" "%~dp0Bindings.cs" "%~dp0ControlsPage.cs" "%~dp0Wheel.cs" "%~dp0DlssDownload.cs" "%~dp0Online.cs" "%~dp0Trophies.cs"

call "%~dp0build.cmd" "%OUT%"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /main:RyGT.UnifiedLauncherTests /resource:"%~dp0ryGT.ico",ryGT.ico /out:"%OUT%\UnifiedLauncherTests.exe" %REFS% %SOURCES% "%~dp0UnifiedLauncherTests.cs"
if errorlevel 1 exit /b 1
"%OUT%\UnifiedLauncherTests.exe"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /main:RyGT.GT7SetupTests /resource:"%~dp0ryGT.ico",ryGT.ico /resource:"%~dp0trophy_assets\bronze.png",trophy.bronze.png /resource:"%~dp0trophy_assets\silver.png",trophy.silver.png /resource:"%~dp0trophy_assets\gold.png",trophy.gold.png /resource:"%~dp0trophy_assets\platinum.png",trophy.platinum.png /resource:"%~dp0trophy_assets\trophy.wav",trophy.trophy.wav /resource:"%~dp0trophy_assets\platinum.wav",trophy.platinum.wav /resource:"%~dp0trophy_assets\font.ttf",trophy.font.ttf /resource:"%~dp0trophy_assets\font-LICENSE.txt",trophy.font-LICENSE.txt /out:"%OUT%\GT7SetupTests.exe" %REFS% %SOURCES% "%~dp0GT7SetupTests.cs"
if errorlevel 1 exit /b 1
"%OUT%\GT7SetupTests.exe" %2 %3 %4 %5 %6 %7
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /out:"%OUT%\LoggingTests.exe" "%~dp0Logging.cs" "%~dp0LoggingTests.cs"
if errorlevel 1 exit /b 1
"%OUT%\LoggingTests.exe"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /out:"%OUT%\RenderScaleTests.exe" /r:System.Drawing.dll "%~dp0RenderResolution.cs" "%~dp0shader-cache-tests\RenderScaleTests.cs"
if errorlevel 1 exit /b 1
"%OUT%\RenderScaleTests.exe"
exit /b %errorlevel%
