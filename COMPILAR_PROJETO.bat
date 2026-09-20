@echo off
title Compilar VZX DUMPER V2
echo [INFO] Restaurando e compilando solucao em Release...
dotnet build VZX_DUMPER_V2.sln -c Release
if %errorlevel% neq 0 (
    echo [ERRO] Falha na compilacao!
    pause
    exit /b %errorlevel%
)
echo.
echo [SUCESSO] Compilacao concluida com sucesso!
pause
