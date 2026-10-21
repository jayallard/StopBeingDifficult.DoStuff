@echo off
if "%~1"=="" (
    echo :Hello, world!
) else (
    echo :Hello, %~1!
)
