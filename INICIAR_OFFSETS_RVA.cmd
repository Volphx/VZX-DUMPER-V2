@echo off
cd /d "%~dp0"
if exist "artifacts\win-x64\VZX-Dumper.exe" (
 start "" "artifacts\win-x64\VZX-Dumper.exe"
) else (
 dotnet run --project VZX_Dumper_UI\Il2CppDumper_ImGuiStyle.csproj -c Release
)
