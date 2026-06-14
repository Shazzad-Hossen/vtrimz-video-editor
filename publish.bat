@echo off
echo Building VTRIMZ...
dotnet publish "Vtrimz\Vtrimz.csproj" -c Release -r win-x64 --self-contained true -o "publish"
echo.
echo Done! Run: publish\VTRIMZ.exe
echo (Keep the whole publish folder together)
pause
