@echo off
rem Gera o instalador em installer\out. Precisa do Inno Setup 6 e, em installer\deps, do PawnIO_setup.exe
rem (github.com/namazso/PawnIO.Setup) e do ffmpeg.exe com o ffmpeg-LICENSE.txt (gyan.dev, build essentials).
dotnet publish "%~dp0app" -c Release -r win-x64 --self-contained true -o "%~dp0dist" -nologo || exit /b 1
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%~dp0installer\setup.iss" || exit /b 1
echo.
echo Instalador em "%~dp0installer\out".
pause
