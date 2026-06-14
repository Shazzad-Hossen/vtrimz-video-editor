@echo off
echo Building VTRIMZ single-file EXE...
dotnet publish "Vtrimz\Vtrimz.csproj" -c Release -o "publish"
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

echo.
echo Done!
echo Share this ONE file: publish\VTRIMZ.exe
echo No install needed on Windows 10/11 64-bit.
echo.
pause
