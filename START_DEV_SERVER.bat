@echo off
REM Start ProMaxx2 QA Web Dev Server
cd /d "%~dp0src\ProMaxx2.QA.Web"
echo Starting Vite Dev Server on http://192.168.200.219:5173/
echo.
npm run dev
pause
