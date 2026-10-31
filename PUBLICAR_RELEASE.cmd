@echo off
setlocal
cd /d "%~dp0"
dotnet publish VZX_Dumper_UI\Il2CppDumper_ImGuiStyle.csproj -c Release -r win-x64 --self-contained false -p:PublishSelfContained=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\win-x64
if errorlevel 1 exit /b 1
copy /y artifacts\win-x64\Il2CppDumper_ImGuiStyle.exe artifacts\win-x64\VZX-Dumper.exe >nul
echo Executavel: artifacts\win-x64\VZX-Dumper.exe
