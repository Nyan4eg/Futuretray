# Собирает dist\Futuretray.exe (один файл, .NET ставить не нужно) и dist\Futuretray.zip.
# Запуск: pwsh tools/publish.ps1
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue

# ReadyToRun без сжатия: так запуск группы самый быстрый (~120 мс). Сжатие экономит
# размер, но замедляет каждый клик по значку примерно втрое.
dotnet publish (Join-Path $root 'src\Futuretray\Futuretray.csproj') -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true -o $dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Нативный запускатель для значков групп (NativeAOT). Компоновщику нужен vswhere из установщика Visual Studio.
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish (Join-Path $root 'src\Futuretray.Launcher\Futuretray.Launcher.csproj') -c Release -r win-x64 -o (Join-Path $dist 'launcher')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Move-Item (Join-Path $dist 'launcher\FuturetrayLauncher.exe') $dist -Force
Remove-Item (Join-Path $dist 'launcher') -Recurse -Force

# Архив — портативная версия: рядом с exe лежит portable.txt, и настройки хранятся в папке Data.
$portable = Join-Path $dist 'portable.txt'
Set-Content $portable -Encoding utf8 @'
Futuretray — портативная версия.
Пока этот файл лежит рядом с Futuretray.exe, настройки, значки и журнал хранятся в папке Data рядом
с программой, а не в профиле пользователя. Удалите файл, чтобы работать как обычная установленная версия.

Futuretray — portable version.
While this file sits next to Futuretray.exe, settings, icons and the log are kept in the Data folder
next to the program instead of your user profile. Delete it to run as a regular installed copy.
'@
Compress-Archive -Path (Join-Path $dist 'Futuretray.exe'), (Join-Path $dist 'FuturetrayLauncher.exe'), $portable -DestinationPath (Join-Path $dist 'Futuretray.zip') -CompressionLevel Optimal
Get-ChildItem $dist | ForEach-Object { '{0,-16} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
