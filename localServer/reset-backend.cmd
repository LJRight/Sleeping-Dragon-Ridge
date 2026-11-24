@echo off
setlocal
pushd "%~dp0" || exit /b 1

py -3 --version >nul 2>&1
if not errorlevel 1 goto use_launcher
python --version >nul 2>&1
if not errorlevel 1 goto use_python
echo Python 3 is required. Install Python and try again.
set "result=1"
goto finish

:use_launcher
py -3 server.py reset-backend
set "result=%errorlevel%"
goto finish

:use_python
python server.py reset-backend
set "result=%errorlevel%"

:finish
popd
pause
exit /b %result%
