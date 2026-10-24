# Готовит манифест winget для уже опубликованного релиза:
#   pwsh tools/winget.ps1 -Version 1.4.0
# Скачивает установщик из GitHub Releases, считает SHA-256 и пишет манифесты в
# winget/manifests/n/Nyan4eg/Futuretray/<версия>/, затем проверяет их через `winget validate`.
# Отправка в microsoft/winget-pkgs — отдельным pull request (например, через wingetcreate).
param([Parameter(Mandatory)] [string]$Version)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$id = 'Nyan4eg.Futuretray'
$repo = 'https://github.com/Nyan4eg/Futuretray'
$url = "$repo/releases/download/v$Version/FuturetraySetup-$Version.exe"
$schema = '1.10.0'

$tmp = Join-Path ([IO.Path]::GetTempPath()) "FuturetraySetup-$Version.exe"
Write-Host "Скачиваю $url"
Invoke-WebRequest $url -OutFile $tmp
$sha = (Get-FileHash $tmp -Algorithm SHA256).Hash
Remove-Item $tmp

$release = Invoke-RestMethod "https://api.github.com/repos/Nyan4eg/Futuretray/releases/tags/v$Version"
$date = ([datetime]$release.published_at).ToString('yyyy-MM-dd')

$dir = Join-Path $root "winget/manifests/n/Nyan4eg/Futuretray/$Version"
New-Item -ItemType Directory -Force $dir | Out-Null

function Write-Manifest($name, $text) {
    $path = Join-Path $dir $name
    # winget-pkgs ждёт UTF-8 без BOM и переводы строк LF.
    [IO.File]::WriteAllText($path, ($text -replace "`r`n", "`n").TrimStart(), [Text.UTF8Encoding]::new($false))
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '{6F1E3C2A-8B7D-4E59-9C3A-2D5F8A1B7E44}_is1'
ReleaseDate: $date
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Nyan4eg
PublisherUrl: https://github.com/Nyan4eg
PublisherSupportUrl: $repo/issues
PackageName: Futuretray
PackageUrl: $repo
License: GPL-3.0
LicenseUrl: $repo/blob/main/LICENSE
ShortDescription: App groups on the Windows taskbar and desktop — a fast alternative to taskbar-groups.
Description: |-
  Futuretray puts groups of apps, folders and websites behind a single taskbar icon.
  Popups open in about 120 ms next to the taskbar on any side, show which apps are running,
  and can also live as a tray on the desktop. Includes hotkeys, search across groups,
  Microsoft Store apps, folder-synced groups and import from Taskbar Groups.
Moniker: futuretray
Tags:
- taskbar
- launcher
- groups
- dock
- productivity
- windows-11
ReleaseNotesUrl: $repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.ru-RU.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: ru-RU
Publisher: Nyan4eg
PackageName: Futuretray
License: GPL-3.0
ShortDescription: Группы приложений на панели задач и рабочем столе Windows — быстрая альтернатива taskbar-groups.
ManifestType: locale
ManifestVersion: $schema
"@

Write-Host "SHA-256: $sha"
winget validate --manifest $dir
