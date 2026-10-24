using System.Xml.Linq;

namespace Futuretray.Core;

/// <summary>
/// Импорт групп из taskbar-groups (github.com/tjackenpacken/taskbar-groups).
/// Там каждая группа — папка config\&lt;имя&gt;\ с ObjectData.xml (XML-сериализация Category)
/// и картинкой GroupImage.png / GroupIcon.ico.
/// </summary>
public static class TaskbarGroupsImport
{
    /// <summary>Где обычно лежит taskbar-groups — для подсказки в диалоге выбора папки.</summary>
    public static string? GuessFolder()
    {
        var places = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        foreach (var place in places)
        {
            try
            {
                if (!Directory.Exists(place)) continue;
                var match = Directory.GetDirectories(place)
                    .FirstOrDefault(d => Path.GetFileName(d).Replace(" ", "").Contains("taskbargroup", StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            catch
            {
                // нет доступа к папке — идём дальше
            }
        }
        return null;
    }

    /// <summary>Ищет группы в выбранной папке (папка программы, её config или папка одной группы).</summary>
    public static List<Group> Load(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "ObjectData.xml", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            IgnoreInaccessible = true,
        });
        var groups = new List<Group>();
        foreach (var file in files)
        {
            try
            {
                if (Parse(file) is { } group) groups.Add(group);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "taskbar-groups import " + file);
            }
        }
        return groups;
    }

    private static Group? Parse(string file)
    {
        var root = XDocument.Load(file).Root;
        if (root == null || root.Name.LocalName != "Category") return null;
        var dir = Path.GetDirectoryName(file)!;

        string? Value(XElement e, string name) =>
            e.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

        var group = new Group { Name = Value(root, "Name") ?? Path.GetFileName(dir) };
        if (ColorUtil.TryParse(Value(root, "ColorString"), out var color))
            group.Color = ColorUtil.ToHex(color);
        if (int.TryParse(Value(root, "Width"), out var width) && width > 0)
            group.Columns = width;

        var list = root.Elements().FirstOrDefault(x => x.Name.LocalName == "ShortcutList");
        foreach (var shortcut in list?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var path = Value(shortcut, "FilePath");
            if (string.IsNullOrWhiteSpace(path)) continue;
            var name = Value(shortcut, "name");

            LaunchItem item;
            if (string.Equals(Value(shortcut, "isWindowsApp"), "true", StringComparison.OrdinalIgnoreCase))
            {
                // У приложений Store там хранится AppUserModelID.
                item = new LaunchItem { Kind = ItemKind.ShellApp, Target = path, Name = string.IsNullOrWhiteSpace(name) ? path : name };
            }
            else
            {
                item = ItemFactory.FromPath(path);
                if (!string.IsNullOrWhiteSpace(name)) item.Name = name;
                item.Arguments = Value(shortcut, "Arguments");
                var workDir = Value(shortcut, "WorkingDirectory");
                // По умолчанию taskbar-groups подставлял папку своего exe — такая нам не нужна.
                if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir) &&
                    !File.Exists(Path.Combine(workDir, "client.exe")) && !workDir.Contains("taskbar", StringComparison.OrdinalIgnoreCase))
                    item.WorkingDirectory = workDir;
            }
            group.Items.Add(item);
        }

        // Картинку группы, выбранную в taskbar-groups, переносим как «Свою картинку».
        var image = new[] { "GroupImage.png", "GroupIcon.ico" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        if (image != null)
        {
            var target = Path.Combine(ConfigStore.UserIconsDir, Ids.New() + Path.GetExtension(image));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(image, target, true);
            group.CustomIconPath = target;
            group.IconMode = GroupIconMode.Custom;
            group.ShowIconBackground = false;
        }
        return group;
    }
}
