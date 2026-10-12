@echo off
rem Gera a pasta "dist", com o app e o .NET juntos, pronta para copiar para outro PC.
dotnet publish "%~dp0app" -c Release -r win-x64 --self-contained true -o "%~dp0dist" -nologo
pause
