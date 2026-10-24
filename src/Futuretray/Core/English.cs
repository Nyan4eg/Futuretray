namespace Futuretray.Core;

/// <summary>Английский перевод интерфейса. Ключ — русский текст из кода и XAML.</summary>
internal static class English
{
    public static readonly Dictionary<string, string> Strings = new()
    {
        // ----- Редактор: боковая панель и шапка -----
        ["Группы"] = "Groups",
        ["Новая группа (Ctrl+N)"] = "New group (Ctrl+N)",
        ["Настройки"] = "Settings",
        ["Название группы"] = "Group name",
        ["Закрепить на панели задач"] = "Pin to taskbar",
        ["Открыть группу"] = "Open group",
        ["Ещё"] = "More",

        // ----- Приложения группы -----
        ["Приложения"] = "Apps",
        ["Добавить приложение"] = "Add app",
        ["Файл"] = "File",
        ["Папка"] = "Folder",
        ["Ссылка"] = "Link",
        ["Группа следит за папкой — новые файлы и ярлыки появляются здесь сами"] = "This group follows a folder — new files and shortcuts appear here automatically",
        ["Открыть"] = "Open",
        ["Отвязать"] = "Unlink",
        ["Перетащите сюда программы, ярлыки, папки, файлы или ссылки из браузера"] = "Drop apps, shortcuts, folders, files or browser links here",
        ["Порядок меняется перетаскиванием. Delete — удалить выбранное."] = "Drag to reorder. Delete removes the selected item.",
        ["Сменить иконку"] = "Change icon",
        ["Название"] = "Name",
        ["Расположение"] = "Location",
        ["Обзор…"] = "Browse…",
        ["Аргументы"] = "Arguments",
        ["Необязательно"] = "Optional",
        ["Рабочая папка"] = "Working folder",
        ["По умолчанию — папка программы"] = "Defaults to the app's folder",
        ["От имени администратора"] = "Run as administrator",
        ["Запустить"] = "Run",
        ["Вернуть иконку"] = "Restore icon",
        ["Открыть расположение"] = "Open file location",
        ["Удалить"] = "Remove",

        // ----- Быстрый доступ -----
        ["Сочетание клавиш"] = "Keyboard shortcut",
        ["Открывает группу у курсора из любой программы. Дальше — стрелки, цифры 1–9 или набор названия."] =
            "Opens the group next to the cursor from any app. Then use arrows, digits 1–9 or type a name.",

        // ----- Рабочее пространство -----
        ["Рабочее пространство"] = "Workspace",
        ["Расставьте окна программ группы как вам удобно и нажмите «Запомнить расположение». Потом «Разложить» — или «Открыть все» в окне группы — запустит недостающие программы в порядке списка и вернёт окна на свои места. Для каждого набора мониторов расположение запоминается отдельно."] =
            "Arrange the windows of this group's apps the way you like and click “Remember layout”. Later, “Arrange” — or “Open all” in the group popup — starts the missing apps in list order and puts every window back in its place. Each set of monitors gets its own layout.",
        ["Запомнить расположение"] = "Remember layout",
        ["Разложить"] = "Arrange",
        ["Забыть"] = "Forget",
        ["Раскладываю окна… недостающие программы запускаются"] = "Arranging windows… starting the missing apps",
        ["Расположение окон забыто"] = "Window layout forgotten",
        ["Расположение ещё не запомнено"] = "No layout remembered yet",
        ["Для этих мониторов ({0}) запомнены места окон: {1} из {2}"] = "For these monitors ({0}), window places remembered: {1} of {2}",
        ["Для этих мониторов ({0}) ничего не запомнено — «Разложить» возьмёт ближайшее из запомненных"] =
            "Nothing remembered for these monitors ({0}) — “Arrange” will use the closest saved layout",
        [" · Другие наборы мониторов: {0}"] = " · Other monitor sets: {0}",
        ["Расположение для этих мониторов забыто"] = "Layout for these monitors forgotten",
        ["Открывать при входе в Windows"] = "Open when I sign in to Windows",
        ["Группа сама откроется и разложит окна после входа. Паузы между программами задаются в свойствах каждой."] =
            "The group opens and arranges its windows by itself after you sign in. Set pauses between apps in each app's properties.",
        ["Включён автозапуск Futuretray — без него группа не откроется при входе"] =
            "Futuretray now starts with Windows — without that the group can't open at sign-in",
        ["Пауза перед запуском"] = "Delay before start",
        ["Свернуть остальное"] = "Minimize everything else",
        ["Закрыть все программы группы"] = "Close all of the group's apps",
        ["Другие окна при открытии"] = "Other windows on open",
        ["Что делать с остальным при «Открыть все»: например, «Работа» сворачивает всё лишнее или закрывает «Отдых»."] =
            "What happens to everything else on “Open all”: for example, “Work” minimizes the rest or closes “Leisure”.",
        ["Не трогать"] = "Leave",
        ["Свернуть"] = "Minimize",
        ["Закрыть группы"] = "Close groups",
        ["Других групп пока нет"] = "There are no other groups yet",
        ["Какие группы закрывать (программы, которые есть и в этой группе, останутся открытыми):"] =
            "Which groups to close (apps that are also in this group stay open):",
        ["В окне группы: Ctrl+Enter — открыть все, Ctrl+M — свернуть остальное, Ctrl+Shift+W — закрыть все программы группы."] =
            "In the group popup: Ctrl+Enter opens all, Ctrl+M minimizes everything else, Ctrl+Shift+W closes all of the group's apps.",
        ["При «Открыть все»: сколько подождать после предыдущей программы группы — например, пока подключится VPN"] =
            "For “Open all”: how long to wait after the previous app in the group — for example, until the VPN connects",
        ["{0} с"] = "{0} s",
        ["Сразу"] = "None",
        ["Запущено программ: {0}"] = "Apps started: {0}",
        [" · для этих мониторов места не запомнены — взяты с {0}"] = " · nothing saved for these monitors — used the layout from {0}",
        ["Ни одна программа группы не открыта — откройте их, расставьте окна и нажмите ещё раз"] =
            "None of this group's apps are open — open them, arrange the windows and click again",
        ["Запомнено окон: {0}"] = "Windows remembered: {0}",
        [" · не открыты: {0}"] = " · not open: {0}",
        [" · свёрнуты, пропущены: {0}"] = " · minimized, skipped: {0}",
        ["Разложено окон: {0}"] = "Windows arranged: {0}",
        [" · не дождался окна: {0}"] = " · window never appeared: {0}",
        [" · Windows не дала подвинуть (запущены от администратора): {0}"] = " · Windows refused to move them (running as administrator): {0}",
        ["Открыть все и разложить окна"] = "Open all and arrange windows",

        // ----- Внешний вид -----
        ["Внешний вид"] = "Appearance",
        ["Цвет группы"] = "Group color",
        ["Подложка значка и оттенок всплывающего окна"] = "Icon background and popup tint",
        ["Свой цвет в формате #RRGGBB"] = "Custom color as #RRGGBB",
        ["Значок на панели задач"] = "Taskbar icon",
        ["«Авто» собирает значок из первых четырёх приложений"] = "“Auto” builds the icon from the first four apps",
        ["Подложка"] = "Background",
        ["Авто"] = "Auto",
        ["Приложение"] = "App",
        ["Своя картинка"] = "Custom image",
        ["Выбрать картинку…"] = "Choose image…",
        ["Размер значков"] = "Icon size",
        ["Маленькие"] = "Small",
        ["Средние"] = "Medium",
        ["Крупные"] = "Large",
        ["Подписи под значками"] = "Labels under icons",
        ["Без подписей название видно во всплывающей подсказке"] = "Without labels, the name shows in a tooltip",
        ["Колонок в окне"] = "Columns",
        ["Фон окна"] = "Window background",
        ["Акрил и Mica работают в Windows 11 22H2 и новее"] = "Acrylic and Mica need Windows 11 22H2 or later",
        ["Акрил"] = "Acrylic",
        ["Сплошной"] = "Solid",
        ["Оттенок окна цветом группы"] = "Tint the window with the group color",
        ["Закрывать окно после запуска"] = "Close after launching",
        ["Ctrl+клик — не закрывать окно группы. Shift+клик или средняя кнопка — открыть ещё одну копию программы"] =
            "Ctrl+click keeps the group open. Shift+click or middle click opens another copy of the app",

        // ----- Трей на рабочем столе -----
        ["Трей на рабочем столе"] = "Desktop tray",
        ["Группа всегда под рукой: отдельная панель у края экрана, без клика по панели задач. Правой кнопкой по трею — быстрые настройки."] =
            "Keep the group at hand as its own bar at the screen edge — no taskbar click needed. Right-click the tray for quick settings.",
        ["Положение"] = "Position",
        ["Слева"] = "Left",
        ["Сверху"] = "Top",
        ["Справа"] = "Right",
        ["Снизу"] = "Bottom",
        ["Свободно"] = "Free",
        ["Перетаскивайте трей за ручку. Отпустите его у края экрана — он прилипнет к этому краю."] =
            "Drag the tray by its handle. Release it near a screen edge and it snaps to that edge.",
        ["Выравнивание вдоль края"] = "Alignment along the edge",
        ["В начале"] = "Start",
        ["По центру"] = "Center",
        ["В конце"] = "End",
        ["Монитор"] = "Monitor",
        ["Поверх всех окон"] = "Always on top",
        ["Иначе трей живёт на рабочем столе под окнами и не пропадает по Win+D. В полноэкранных играх и видео трей сам уходит назад."] =
            "Otherwise the tray lives on the desktop below windows and survives Win+D. It steps back in fullscreen games and videos.",
        ["Прятать за край экрана"] = "Auto-hide at the screen edge",
        ["Трей уезжает за край и выезжает, когда вы подводите к нему мышь"] = "The tray slides away and comes back when you move the mouse to the edge",
        ["Резервировать место на экране"] = "Reserve screen space",
        ["Как панель задач: развёрнутые окна не будут заходить под трей. Не сочетается с автоскрытием."] =
            "Like the taskbar: maximized windows won't cover the tray. Can't be combined with auto-hide.",
        ["Предпросмотр"] = "Preview",
        ["Перетащите, чтобы переместить. Отпустите у края экрана — трей прилипнет к нему."] =
            "Drag to move. Release near a screen edge to snap the tray to it.",
        ["Свободно (перетаскивать)"] = "Free (drag to move)",
        ["Убрать с рабочего стола"] = "Remove from desktop",

        // ----- Настройки -----
        ["Тема"] = "Theme",
        ["«Системная» следует настройкам Windows: окно редактора — теме приложений, окна групп — теме панели задач"] =
            "“System” follows Windows: the editor uses the app theme, group popups use the taskbar theme",
        ["Системная"] = "System",
        ["Светлая"] = "Light",
        ["Тёмная"] = "Dark",
        ["Язык"] = "Language",
        ["Язык интерфейса Futuretray. «Как в Windows» — русский для русской Windows, иначе английский."] =
            "Futuretray interface language. “Like Windows” picks Russian on Russian Windows, English otherwise.",
        ["Как в Windows"] = "Like Windows",
        ["Язык сменится после перезапуска Futuretray"] = "The language changes after Futuretray restarts",
        ["Перезапустить"] = "Restart",
        ["Запускать вместе с Windows"] = "Start with Windows",
        ["Futuretray тихо ждёт в фоне, и группы открываются мгновенно — даже первый раз после включения ПК"] =
            "Futuretray waits quietly in the background so groups open instantly — even the first time after boot",
        ["Значок в области уведомлений"] = "Notification area icon",
        ["Быстрый доступ к редактору и группам из трея"] = "Quick access to the editor and groups from the system tray",
        ["Переключаться на уже открытую программу"] = "Switch to apps that are already open",
        ["Как на панели задач: клик по запущенной программе показывает её окно, повторный — сворачивает, при нескольких окнах — перебирает их. Чёрточка под значком — программа открыта, длинная — активна. Shift+клик всегда запускает новую копию."] =
            "Like the taskbar: clicking a running app shows its window, clicking again minimizes it, and several windows are cycled. A dash under the icon means the app is open, a long one means it's active. Shift+click always starts a new copy.",
        ["Поиск по всем группам"] = "Search all groups",
        ["Сочетание открывает строку поиска: набрали пару букв — Enter. Ещё — в меню значка в трее."] =
            "The shortcut opens a search box: type a few letters and press Enter. Also available from the tray icon menu.",
        ["Значки сайтов для ссылок"] = "Website icons for links",
        ["Futuretray один раз заглядывает на сам сайт за его значком и хранит его у вас. Без этого у ссылок значок браузера."] =
            "Futuretray fetches the icon from the website itself once and keeps it locally. Otherwise links show the browser icon.",
        ["Проверять обновления автоматически"] = "Check for updates automatically",
        ["Futuretray заглядывает в GitHub Releases пару раз в день и предлагает обновиться в один клик"] =
            "Futuretray checks GitHub Releases a couple of times a day and offers a one-click update",
        ["Что нового"] = "What's new",
        ["Проверить сейчас"] = "Check now",
        ["Что-то работает не так?"] = "Something not working?",
        ["«Собрать отчёт» сохранит на рабочий стол zip с журналом ошибок, настройками групп (там видны пути к вашим программам) и сведениями о системе — отправьте его разработчику."] =
            "“Create report” saves a zip to your desktop with the error log, group settings (they include paths to your apps) and system details — send it to the developer.",
        ["Собрать отчёт"] = "Create report",
        ["Сообщить о проблеме на GitHub"] = "Report an issue on GitHub",
        ["Резервная копия"] = "Backup",
        ["Сохраните группы в файл, чтобы перенести их на другой компьютер"] = "Save your groups to a file to move them to another PC",
        ["Экспорт групп…"] = "Export groups…",
        ["Импорт групп…"] = "Import groups…",
        ["Из Taskbar Groups…"] = "From Taskbar Groups…",
        ["Папка с данными"] = "Data folder",
        ["Закрыть Futuretray полностью"] = "Quit Futuretray completely",

        // ----- Пустой экран -----
        ["Соберите приложения в группы"] = "Gather your apps into groups",
        ["Группа — это один значок на панели задач, который по клику раскрывает набор ваших программ, папок и сайтов."] =
            "A group is a single taskbar icon that opens a set of your apps, folders and websites.",
        ["Создать первую группу"] = "Create your first group",
        ["Перенести группы из Taskbar Groups"] = "Bring groups over from Taskbar Groups",

        // ----- Закрепление -----
        ["Закрепите группу на панели задач"] = "Pin the group to the taskbar",
        ["Windows не даёт программам закрепляться самостоятельно, поэтому последний шаг — за вами. Это займёт пару секунд."] =
            "Windows doesn't let apps pin themselves, so the last step is yours. It takes a couple of seconds.",
        ["Способ 1 — через Проводник"] = "Option 1 — File Explorer",
        ["Показать ярлык"] = "Show shortcut",
        ["1. Нажмите «Показать ярлык» — откроется папка, ярлык группы уже будет выделен.\n2. Щёлкните по нему правой кнопкой → «Закрепить на панели задач».\nЕсли такого пункта нет — выберите «Показать дополнительные параметры» (или Shift + правая кнопка)."] =
            "1. Click “Show shortcut” — a folder opens with the group shortcut selected.\n2. Right-click it → “Pin to taskbar”.\nIf the item isn't there, choose “Show more options” (or Shift + right-click).",
        ["Способ 2 — через меню «Пуск»"] = "Option 2 — Start menu",
        ["Добавить в «Пуск»"] = "Add to Start",
        ["Закрепить нужно один раз: когда вы меняете приложения, название или значок, закреплённая группа обновится сама."] =
            "You only pin once: when you change apps, the name or the icon, the pinned group updates by itself.",
        ["Жду, пока значок появится на панели задач…"] = "Waiting for the icon to appear on the taskbar…",
        ["Готово"] = "Done",
        ["Эта группа уже закреплена. Если значок пропал с панели, закрепите его ещё раз любым способом ниже."] =
            "This group is already pinned. If the icon disappeared from the taskbar, pin it again using either option below.",
        ["Затем нажмите Win, начните вводить «{0}», щёлкните по найденной группе правой кнопкой → «Закрепить на панели задач»."] =
            "Then press Win, start typing “{0}”, right-click the group in the results → “Pin to taskbar”.",
        ["Готово! «{0}» закреплена на панели задач"] = "Done! “{0}” is pinned to the taskbar",
        ["Готово! Группа закреплена на панели задач"] = "Done! The group is pinned to the taskbar",
        ["Уже закреплена на панели задач"] = "Already pinned to the taskbar",
        ["«{0}» добавлена в меню «Пуск». В поиске «Пуска» она может появиться через несколько секунд"] =
            "“{0}” was added to the Start menu. It may take a few seconds to show up in Start search",

        // ----- Выбор приложений и ссылки -----
        ["Добавить приложения"] = "Add apps",
        ["Всё из меню «Пуск», включая приложения Microsoft Store. Можно выбрать несколько."] =
            "Everything from the Start menu, including Microsoft Store apps. You can pick several.",
        ["Поиск приложений"] = "Search apps",
        ["Собираю список приложений…"] = "Collecting installed apps…",
        ["Отмена"] = "Cancel",
        ["Добавить"] = "Add",
        ["Добавить ссылку"] = "Add link",
        ["Сайт откроется в браузере по умолчанию. Подойдут и ссылки вида steam://, discord://, tg://"] =
            "The site opens in your default browser. Links like steam://, discord://, tg:// work too",
        ["Название (необязательно)"] = "Name (optional)",
        ["Ничего не найдено"] = "Nothing found",
        ["Ничего не выбрано"] = "Nothing selected",
        ["Выбрано: {0}"] = "Selected: {0}",
        ["Добавлено: {0} {1}"] = "Added {0} {1}",
        ["элемент"] = "item",
        ["элементов"] = "items",

        // ----- Окно поиска -----
        ["Futuretray — поиск"] = "Futuretray — search",
        ["Программа, окно или приложение…"] = "Search groups, open windows and apps…",
        ["Программа открыта"] = "App is open",
        ["Enter — открыть  ·  Ctrl+Enter — добавить в группу  ·  Shift+Enter — новая копия  ·  Esc — закрыть"] =
            "Enter — open  ·  Ctrl+Enter — add to group  ·  Shift+Enter — new copy  ·  Esc — close",
        ["Окно · {0}"] = "Window · {0}",

        // ----- Синхронизация, портативная версия, «Что нового» -----
        ["Синхронизация между компьютерами"] = "Sync between computers",
        ["Храните группы в папке OneDrive, Dropbox или на сетевом диске — и они будут одинаковыми на всех ваших компьютерах. Расположение окон у каждого набора мониторов своё."] =
            "Keep your groups in a OneDrive or Dropbox folder or on a network drive, and they'll be the same on all your computers. Window layouts stay separate for each monitor setup.",
        ["Через OneDrive"] = "Via OneDrive",
        ["Выбрать папку…"] = "Choose a folder…",
        ["Другая папка…"] = "Another folder…",
        ["Открыть папку"] = "Open folder",
        ["Отключить"] = "Turn off",
        ["В этой папке уже есть группы"] = "This folder already has groups",
        ["Скорее всего, их сохранил Futuretray на другом компьютере. Взять их — группы этого компьютера заменятся ими (Futuretray перезапустится). Заменить — в папку запишутся группы этого компьютера."] =
            "Most likely Futuretray saved them on another computer. Use them — this computer's groups are replaced with them (Futuretray restarts). Replace — this computer's groups are written to the folder.",
        ["Заменить моими"] = "Replace with mine",
        ["Взять из папки"] = "Use the folder's",
        ["Группы изменились на другом компьютере"] = "Groups changed on another computer",
        ["Группы хранятся только на этом компьютере"] = "Groups are stored on this computer only",
        ["Группы синхронизируются через папку {0}"] = "Groups sync through the folder {0}",
        ["Папка для синхронизации групп"] = "Folder to sync groups through",
        ["Готово: группы сохранены в папку синхронизации"] = "Done: groups are saved to the sync folder",
        ["Не удалось включить синхронизацию: "] = "Couldn't turn on sync: ",
        ["Не удалось записать в папку {0}"] = "Couldn't write to the folder {0}",
        [" · не удалось запустить: {0}"] = " · couldn't start: {0}",
        ["Синхронизация выключена — группы остались на этом компьютере"] = "Sync is off — the groups stay on this computer",
        ["Портативная версия: настройки, значки и журнал хранятся рядом с программой — {0}"] =
            "Portable version: settings, icons and the log are kept next to the program — {0}",
        ["В архиве обновления нет Futuretray.exe"] = "The update archive has no Futuretray.exe",
        ["Нет права записи в папку программы — скачайте новую версию вручную"] =
            "No permission to write to the program folder — download the new version manually",
        ["Что нового в Futuretray {0}"] = "What's new in Futuretray {0}",
        ["Все изменения"] = "All changes",
        ["Понятно"] = "Got it",
        ["Приложение из меню «Пуск»"] = "App from the Start menu",
        ["Приложение · {0}"] = "App · {0}",
        ["Переключиться"] = "Switch to",
        ["Добавить в группу"] = "Add to group",
        ["Сначала создайте группу в редакторе"] = "Create a group in the editor first",
        ["«{0}» уже есть в группе «{1}»"] = "“{0}” is already in “{1}”",
        ["«{0}» добавлено в группу «{1}»"] = "Added “{0}” to “{1}”",

        // ----- Трей Windows и уведомления -----
        ["Готово! Futuretray обновлён до версии {0}"] = "Done! Futuretray has been updated to version {0}",
        ["Эта группа больше не существует. Открепите её значок с панели задач."] = "This group no longer exists. Unpin its icon from the taskbar.",
        ["Futuretray — группы на панели задач"] = "Futuretray — taskbar groups",
        ["Открыть Futuretray"] = "Open Futuretray",
        ["Этого приложения больше нет в группе — список переходов обновится сам."] =
            "This app is no longer in the group — the jump list will update by itself.",
        ["Поиск по группам"] = "Search groups",
        ["Обновить до версии {0}"] = "Update to version {0}",
        ["Выйти"] = "Quit",

        // ----- Сочетания клавиш -----
        ["Пробел"] = "Space",
        ["Не удалось разобрать сочетание"] = "Couldn't read this shortcut",
        ["Это сочетание уже назначено другой группе"] = "This shortcut is already used by another group",
        ["Сочетание уже занято Windows или другой программой — выберите другое"] = "This shortcut is taken by Windows or another app — pick a different one",
        ["Нажмите и введите сочетание. Backspace — убрать, Esc — отмена."] = "Click and press a shortcut. Backspace clears, Esc cancels.",
        ["Нажмите сочетание…"] = "Press a shortcut…",
        ["Не задано"] = "Not set",

        // ----- Элементы и группы -----
        ["Приложение Microsoft Store"] = "Microsoft Store app",
        ["Диск {0}"] = "Drive {0}",
        ["Эта программа не умеет открывать файлы"] = "This app can't open files",
        ["Не найдено: {0}"] = "Not found: {0}",
        ["Приложение Windows"] = "Windows app",
        ["Программа"] = "Program",
        ["Ярлык"] = "Shortcut",
        ["Новая группа"] = "New group",
        ["Группы приложений на панели задач"] = "App groups on the taskbar",
        ["Группа"] = "Group",
        ["Пусто"] = "Empty",
        ["приложение"] = "app",
        ["приложений"] = "apps",
        ["Группа пуста.\nДобавьте приложения в редакторе."] = "This group is empty.\nAdd apps in the editor.",
        ["{0} — можно перетащить сюда файлы, чтобы открыть их в этой программе"] = "{0} — drop files here to open them in this app",
        ["Запустить от имени администратора"] = "Run as administrator",
        ["Открыть все"] = "Open all",
        ["Изменить группу…"] = "Edit group…",
        ["Новое окно"] = "New window",
        ["Закрыть все окна ({0})"] = "Close all windows ({0})",
        ["Закрыть окно"] = "Close window",
        ["{0} — нет совпадений"] = "{0} — no matches",

        // ----- Обновления -----
        ["Скачанный файл повреждён (контрольная сумма не совпала). Попробуйте ещё раз."] = "The download is corrupted (checksum mismatch). Please try again.",
        ["Версия {0}"] = "Version {0}",
        ["Проверяю…"] = "Checking…",
        ["У вас последняя версия — {0}"] = "You have the latest version — {0}",
        ["Доступна версия {0} (у вас {1})"] = "Version {0} is available (you have {1})",
        ["Не удалось проверить обновления: нет связи с GitHub"] = "Couldn't check for updates: no connection to GitHub",
        ["Скачиваю версию {0}… {1:P0}"] = "Downloading version {0}… {1:P0}",
        ["Устанавливаю…"] = "Installing…",
        ["Не удалось скачать обновление. Попробуйте позже."] = "Couldn't download the update. Please try again later.",
        ["Обновить до {0}"] = "Update to {0}",
        ["Доступна новая версия Futuretray — {0}"] = "A new version of Futuretray is available — {0}",
        ["Обновить"] = "Update",
        ["Версия {0} · группы приложений на панели задач"] = "Version {0} · app groups on the taskbar",

        // ----- Меню группы и действия редактора -----
        ["{0} · основной"] = "{0} · primary",
        ["Дублировать"] = "Duplicate",
        ["Синхронизировать с папкой…"] = "Sync with a folder…",
        ["Отвязать от папки"] = "Unlink from folder",
        ["Показать ярлык в Проводнике"] = "Show shortcut in File Explorer",
        ["Добавить в меню «Пуск»"] = "Add to Start menu",
        ["Ярлык на рабочем столе"] = "Desktop shortcut",
        ["Ярлык группы создан на рабочем столе"] = "Group shortcut created on the desktop",
        ["Удалить группу"] = "Delete group",
        ["Папка, за которой будет следить группа"] = "Folder for the group to follow",
        ["Группа синхронизирована с папкой: всё новое в ней появится здесь само"] = "The group now follows the folder: anything new there will show up here",
        ["Группа больше не следит за папкой — уже добавленные элементы остались"] = "The group no longer follows the folder — items already added stay",
        ["Папка программы Taskbar Groups (где лежат config и Shortcuts)"] = "Taskbar Groups folder (the one with config and Shortcuts)",
        ["Группы Taskbar Groups не найдены — выберите папку, где лежит config"] = "No Taskbar Groups found — choose the folder that contains config",
        ["Перенесено групп: {0}. Закрепите их на панели задач заново — старые значки Taskbar Groups можно открепить"] =
            "Groups imported: {0}. Pin them to the taskbar again — you can unpin the old Taskbar Groups icons",
        ["Не удалось прочитать группы: "] = "Couldn't read the groups: ",
        [" — копия"] = " — copy",
        ["Группа «{0}» удалена"] = "Group “{0}” deleted",
        ["Отменить"] = "Undo",
        ["«{0}» удалено из группы"] = "“{0}” removed from the group",
        ["Выберите программы, ярлыки или файлы"] = "Choose apps, shortcuts or files",
        ["Программы и ярлыки|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1;*.appref-ms|Все файлы|*.*"] =
            "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.ps1;*.appref-ms|All files|*.*",
        ["Выберите папки"] = "Choose folders",
        ["Выберите файл"] = "Choose a file",
        ["Все файлы|*.*"] = "All files|*.*",
        ["Выберите картинку или значок"] = "Choose an image or icon",
        ["Картинки и значки|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.exe;*.dll|Все файлы|*.*"] =
            "Images and icons|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.exe;*.dll|All files|*.*",
        ["Отпустите, чтобы добавить в группу"] = "Release to add to the group",
        ["Цвет акцента Windows"] = "Windows accent color",
        ["Сначала добавьте приложения"] = "Add some apps first",
        ["Экспорт групп"] = "Export groups",
        ["Futuretray-группы.json"] = "Futuretray-groups.json",
        ["Сохранено групп: {0}"] = "Groups saved: {0}",
        ["Не удалось сохранить: "] = "Couldn't save: ",
        ["Импорт групп"] = "Import groups",
        ["Futuretray (*.json)|*.json|Все файлы|*.*"] = "Futuretray (*.json)|*.json|All files|*.*",
        ["В файле нет групп"] = "There are no groups in this file",
        ["Импортировано групп: {0}"] = "Groups imported: {0}",
        ["Не удалось прочитать файл: "] = "Couldn't read the file: ",
        ["Отчёт сохранён на рабочий стол — отправьте этот zip разработчику"] = "The report is on your desktop — send this zip to the developer",
        ["Не удалось собрать отчёт: "] = "Couldn't create the report: ",
    };
}
