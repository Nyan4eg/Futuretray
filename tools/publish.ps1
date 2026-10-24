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

Compress-Archive -Path (Join-Path $dist 'Futuretray.exe') -DestinationPath (Join-Path $dist 'Futuretray.zip') -CompressionLevel Optimal
Get-ChildItem $dist | ForEach-Object { '{0,-16} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
