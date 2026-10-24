# Changelog

## 1.10.0

- **What's new after an update**: once Futuretray updates itself, the editor shows what the new version
  brings. You can reread it any time from “What's new” in Settings.
- **Sync groups between computers**: Settings → “Sync between computers” → “Via OneDrive”, or any folder
  (Dropbox, a network drive). Changes made on another computer are picked up automatically, and your
  custom group pictures travel with the groups.
- **Portable version**: Futuretray.zip is now portable — unpack it anywhere, even to a USB stick. Settings
  live in the Data folder next to the program, it doesn't add itself to startup, and it updates in place.
  Any copy becomes portable if you put a portable.txt file next to the exe.

## 1.9.0

- **Search everywhere** (Win+Alt+Space): besides the apps in your groups it finds **open windows** — by
  title and by app (“tel” finds a Telegram window showing a chat), Enter switches to the window — and
  **Start menu apps**, Store apps included, even if they're in no group.
- **Add to a group right from search**: Ctrl+Enter or right-click a result → pick a group. The search
  window stays open, so you can build a group in seconds.
- Uninstallers, help files and other Start menu clutter stay out of the results.
- The search window now always fits its height to the number of results.

## 1.8.0

- **Close all of the group's apps** — finish work in one step. Windows close as if you clicked their ×,
  so apps still ask about unsaved documents.
- **Minimize everything else** — a focus mode: only the group's windows stay on screen, and its minimized
  windows come back. Windows on other virtual desktops are left alone.
- **Switching workspaces**: in the “Workspace” card choose what happens to everything else on “Open all” —
  leave it, minimize it, or close chosen groups (for example, “Work” closes “Leisure”). Apps that are also
  in the group being opened stay open.
- The commands are in the group popup and desktop tray menus and in the pinned group's jump list;
  in the group popup press Ctrl+Enter (open all), Ctrl+M (minimize everything else), Ctrl+Shift+W (close all).
- “Open all” is now in the jump list for groups without a saved layout too.

## 1.7.0

- **A layout for each monitor setup**: at home with a monitor and on the road with just the laptop, a group
  remembers a separate layout. “Arrange” and “Open all” pick the one that matches the connected monitors,
  or the closest one (same number of monitors) if nothing was saved for them. Monitors are recognised by
  position and size even if Windows renames them.
- **Open when I sign in to Windows**: turn it on in the “Workspace” card and the group starts its apps and
  arranges the windows after you sign in. Futuretray's own startup is switched on along with it.
- **Launch order and delays**: “Open all” starts apps in list order, and each app can have a delay before
  it starts — for example, give the VPN time to connect before the messenger.
- “Open all” no longer starts a second copy of apps that are already open.
- A layout saved in 1.6.0 is assigned to the current monitor setup automatically.

## 1.6.0

- **Workspaces**: arrange the windows of a group's apps, click “Remember layout” in the editor — and later
  “Arrange” (or “Open all and arrange windows” in the group popup, desktop tray and jump list) starts the
  missing apps, waits for their windows and puts each one back, on another monitor and maximized included.
- Windows are found reliably: for apps whose window appears late or after a splash screen Futuretray waits
  up to 25 seconds; two copies of one app (say, two File Explorer folders) get different places.
- Places are stored as fractions of the screen, so a layout survives resolution and scaling changes.
- Windows of apps running as administrator are left alone, and Futuretray says so.
- Fixed: the hidden helper window of console apps in Windows Terminal no longer counts as an open window.

## 1.5.1

- Next to a taskbar on the **left or right**, the group popup is now laid out as a column along the taskbar
  (many apps — several columns) instead of a horizontal row. A column count set by hand is respected.
- On first use the **desktop tray** sits at the same screen edge as the taskbar instead of always on the left.
- After appearing, the tray rechecks its layout and position against the settings (a safety net for mixed scaling).

## 1.5.0

- **Jump list for a pinned group**: right-click the group's taskbar icon to see its apps in the native
  Windows menu and start any of them without opening the popup. Links, Store apps and switching to an
  open window all work. At the bottom — “Edit group…”.
- The menu updates itself whenever the group changes and disappears with a deleted group.

## 1.4.1

- Fixed: with the accent color on title bars, the app name in the editor stuck out of the colored title
  band. The title now matches the system band height, maximized included.
- The winget manifest moved to schema 1.12.

## 1.4.0

- **English interface**: the language follows Windows or can be chosen in Settings.
- **Automated tests** (85) run in GitHub Actions on every push: popup placement, hotkeys, search, detection
  of running apps, import, folder groups, old configs, translation completeness. A release doesn't ship if
  the tests fail.
- Fixed: a disabled search shortcut came back after a restart.
- Shortcuts are shown in the usual Windows order: Win + Ctrl + Alt + Shift.
- GPL-3.0 license, README in Russian and English with screenshots, a winget manifest.

## 1.3.0

- **Hotkeys for groups**: give any group a shortcut and its popup opens at the cursor from any app. If the
  shortcut is taken by Windows or another app, the editor tells you.
- **Search across all groups** (Win+Alt+Space by default): type a couple of letters — Enter. Matches word
  starts, substrings and initials (“vsc” → Visual Studio Code) and shows what's already open.
- **Drop files onto the tray**: drop a file onto an app in the desktop tray to open it there.
- **Site icons** for links instead of the browser icon (fetched from the site itself; can be turned off).
- **Import from Taskbar Groups**: groups, apps, Store apps, arguments and group pictures.
- **Folder groups**: a group watches a folder and picks up new files and shortcuts by itself.

## 1.2.0

- **Running-app indicator** in the group popup and the desktop tray: a short line — the app is open, a long
  accent-colored one — it's active right now. In the tray the line faces the screen edge.
- **Clicks work like the taskbar**: if the app is already open, Futuretray switches to its window, a second
  click minimizes it, and with several windows it cycles through them. Shift+click or middle click opens
  another copy. Can be turned off in Settings.
- The icon menu of an open app has “New window” and “Close window/all windows”.
- Recognises regular programs, shortcuts, Microsoft Store and MSIX apps (Claude, Telegram from the Store,
  the new Notepad), Squirrel apps (Discord, Element, Slack), Steam and File Explorer.

## 1.1.1

- **“Create report”** (Settings → “Something not working?”): a zip on the desktop with the error log, group
  settings and system details — monitors, scaling, taskbar position, tweakers like ExplorerPatcher.
- A “Report a problem on GitHub” button.
- Release builds moved to current GitHub Actions versions.

## 1.1.0

- **Installer**: Futuretray installs to `%LOCALAPPDATA%\Programs\Futuretray` without admin rights, shows up
  in Start and in Windows Apps, and uninstalls the usual way.
- **Auto-update**: the app finds new versions on GitHub and updates in one click (Settings → “Updates”,
  a notice in the editor and an item in the tray menu).
- **Desktop tray**: any group can sit as a panel at a screen edge or anywhere you like — on top of windows,
  auto-hiding, or reserving space like the taskbar.
- Pin dialog: removed the drag-and-drop method that doesn't work in Windows 11; the dialog notices by itself
  when the group is pinned.

## 1.0.0

- First version: app groups on the taskbar, an editor, the group popup, picking apps from Start,
  an automatic group icon, a tray icon and startup with Windows.
