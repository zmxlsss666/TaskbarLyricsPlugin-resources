@echo off
setlocal

:: 1. 强制 CMD 切换到 UTF-8 编码（65001），匹配日志编码
chcp 65001 >nul

set LOG_PATH=%APPDATA%\TaskbarLyrics\debug.log

echo 正在实时监控 TaskbarLyrics 日志...
echo 日志路径: %LOG_PATH%
echo 按 Ctrl+C 停止监控
echo =========================================

:loop
if exist "%LOG_PATH%" (
    :: 2. 关键：PowerShell 强制以 UTF-8 读取日志，避免编码解析错误
    powershell -Command "Get-Content '%LOG_PATH%' -Tail 10 -Wait -Encoding UTF8"
) else (
    echo 日志文件不存在，等待应用程序启动...
    timeout /t 2 > nul
    goto loop
)

pause