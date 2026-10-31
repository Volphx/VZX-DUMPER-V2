@echo off
setlocal
cd /d "%~dp0"
set "vzxVswhere=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "vzxVcvars="
if exist "%vzxVswhere%" for /f "usebackq delims=" %%I in (`"%vzxVswhere%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "vzxVcvars=%%I\VC\Auxiliary\Build\vcvarsall.bat"
if not defined vzxVcvars (
  echo MSVC C++ x64 nao encontrado. Instale Desktop development with C++ e Windows SDK.
  exit /b 1
)
call "%vzxVcvars%" x64 >nul
if errorlevel 1 exit /b 1
if not exist bin mkdir bin
cl /nologo /std:c++20 /EHsc /MT /O2 /utf-8 /LD bridge.cpp /Fo:bin\bridge.obj /Fe:bin\OffsetRvaEngine.dll /link /DYNAMICBASE /NXCOMPAT
exit /b %errorlevel%
