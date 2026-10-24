; Установщик Futuretray (Inno Setup 6).
; Сборка: tools/release.ps1 (передаёт AppVersion и SourceExe), вручную:
;   ISCC /DAppVersion=1.1.0 installer\Futuretray.iss

#define AppName "Futuretray"
#define AppRepo "https://github.com/Nyan4eg/Futuretray"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\Futuretray.exe"
#endif

[Setup]
AppId={{6F1E3C2A-8B7D-4E59-9C3A-2D5F8A1B7E44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppRepo}
AppSupportURL={#AppRepo}/issues
AppUpdatesURL={#AppRepo}/releases
VersionInfoVersion={#AppVersion}
; Без прав администратора: всё ставится в профиль пользователя.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=..\dist
OutputBaseFilename=FuturetraySetup-{#AppVersion}
SetupIconFile=..\src\Futuretray\Assets\app.ico
UninstallDisplayIcon={app}\Futuretray.exe
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Работающий Futuretray закрываем сами (см. PrepareToInstall), Restart Manager не нужен.
CloseApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ru.DesktopIcon=Создать значок на рабочем столе
en.DesktopIcon=Create a desktop shortcut
ru.LaunchApp=Запустить Futuretray
en.LaunchApp=Launch Futuretray
ru.RemoveData=Удалить также ваши группы и настройки Futuretray?%n%nЕсли вы собираетесь установить программу заново, нажмите «Нет».
en.RemoveData=Also delete your Futuretray groups and settings?%n%nChoose "No" if you plan to reinstall.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\Futuretray.exe"; AppUserModelID: "Futuretray.App"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\Futuretray.exe"; AppUserModelID: "Futuretray.App"; Tasks: desktopicon

[Run]
; Обычная установка — галочка «Запустить» на последней странице.
Filename: "{app}\Futuretray.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; Тихая установка = автообновление: перезапускаем программу и показываем, что обновились.
Filename: "{app}\Futuretray.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallRun]
; Закрывает программу, убирает автозапуск и ярлыки групп из «Пуска» и с рабочего стола.
Filename: "{app}\Futuretray.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "FuturetrayCleanup"

[Code]
// Перед установкой закрываем запущенный Futuretray, откуда бы он ни был запущен:
// новая версия сама попросит хост выйти по именованному каналу.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  try
    ExtractTemporaryFile('Futuretray.exe');
    Exec(ExpandConstant('{tmp}\Futuretray.exe'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(700);
  except
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Futuretray'), True, True, True);
end;
