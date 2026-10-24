# Собирает релиз: dist\Futuretray.exe, dist\Futuretray.zip и установщик dist\FuturetraySetup-<версия>.exe.
#   pwsh tools/release.ps1                 — версия из Futuretray.csproj
#   pwsh tools/release.ps1 -Version 1.2.0  — сначала поднять версию в csproj
#   pwsh tools/release.ps1 -Publish        — ещё и выложить GitHub Release (нужен gh и запушенный тег)
param(
    [string]$Version,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $root 'src\Futuretray\Futuretray.csproj'

if ($Version) {
    (Get-Content $csproj -Raw) -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>" | Set-Content $csproj -NoNewline
}
$Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Futuretray $Version"

& (Join-Path $PSScriptRoot 'publish.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Не найден Inno Setup 6 (ISCC.exe).' }

$dist = Join-Path $root 'dist'
& $iscc /Q "/DAppVersion=$Version" "/DSourceExe=$dist\Futuretray.exe" "/DSourceLauncher=$dist\FuturetrayLauncher.exe" (Join-Path $root 'installer\Futuretray.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $dist "FuturetraySetup-$Version.exe"
Get-ChildItem $dist -File | ForEach-Object { '{0,-28} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

if ($Publish) {
    $notes = Join-Path $env:TEMP "futuretray-notes-$Version.md"
    # Заметки к релизу — раздел этой версии из CHANGELOG.md.
    $changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
    $section = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($Version))[^\n]*\n(.*?)(?=^## |\z)").Groups[1].Value.Trim()
    Set-Content $notes ($section ? $section : "Futuretray $Version")
    gh release create "v$Version" $setup (Join-Path $dist 'Futuretray.zip') --title "Futuretray $Version" --notes-file $notes
}
