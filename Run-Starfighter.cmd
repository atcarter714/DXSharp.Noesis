@echo off
pushd "%~dp0"
dotnet run --project samples\Starfighter -c Release -- %*
if errorlevel 1 pause
popd
