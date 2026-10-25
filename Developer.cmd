@echo off
setlocal DisableDelayedExpansion
set /a BAP_DEVELOPER_BOOTSTRAP_ATTEMPT=0 >nul
:bap_allocate_bootstrap
set /a BAP_DEVELOPER_BOOTSTRAP_ATTEMPT+=1 >nul
set "BAP_DEVELOPER_BOOTSTRAP=%~dp0artifacts\developer-bootstrap-%RANDOM%-%RANDOM%-%BAP_DEVELOPER_BOOTSTRAP_ATTEMPT%"
mkdir "%BAP_DEVELOPER_BOOTSTRAP%" >nul 2>&1 && goto bap_bootstrap_ready
if %BAP_DEVELOPER_BOOTSTRAP_ATTEMPT% LSS 32 goto bap_allocate_bootstrap
echo Could not allocate a fresh developer bootstrap directory after 32 attempts. 1>&2
exit /b 1
:bap_bootstrap_ready
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /noconfig /utf8output /target:exe /platform:x64 /reference:"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\System.dll" /out:"%BAP_DEVELOPER_BOOTSTRAP%\BetterAstralParty.Developer.Bootstrap.exe" "%~dp0developer-tools\Bootstrap.cs"
if errorlevel 1 exit /b 1
"%BAP_DEVELOPER_BOOTSTRAP%\BetterAstralParty.Developer.Bootstrap.exe" "%~dp0." %*
exit /b %ERRORLEVEL%
