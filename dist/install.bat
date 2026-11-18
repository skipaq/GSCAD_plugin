@echo off
rem Установка GeoScheme для AutoCAD 2021-2024 (per-user, без прав администратора).
setlocal
set SRC=%~dp0GeoScheme.bundle
set DST=%APPDATA%\Autodesk\ApplicationPlugins\GeoScheme.bundle

if not exist "%SRC%\PackageContents.xml" (
  echo Не найден "%SRC%\PackageContents.xml".
  pause
  exit /b 1
)

tasklist /nh /fi "imagename eq acad.exe" 2>nul | findstr /i "acad.exe" >nul
if not errorlevel 1 (
  echo.
  echo AutoCAD запущен. Плагин загружен в память, файл занят.
  echo Закрой AutoCAD и запусти установку снова.
  echo.
  pause
  exit /b 1
)

if exist "%DST%" rmdir /s /q "%DST%"
xcopy "%SRC%" "%DST%" /e /i /q /y >nul
if errorlevel 1 (
  echo.
  echo Ошибка копирования. Проверь, закрыт ли AutoCAD.
  pause
  exit /b 1
)

echo.
echo Установлено: %DST%
echo Запусти AutoCAD - в ленте появится вкладка "ГЕО Схемы".
echo Для удаления: удали папку %DST%
pause
