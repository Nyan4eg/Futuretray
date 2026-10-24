using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Futuretray.Core;

namespace Futuretray.UI;

/// <summary>Поле записи сочетания клавиш: нажмите, затем нужное сочетание. Backspace — убрать, Esc — отмена.</summary>
public sealed class HotkeyBox : Button
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).UpdateText()));

    private bool _recording;

    public HotkeyBox()
    {
        // Неявный стиль Button не применяется к наследникам — подключаем явно.
        SetResourceReference(StyleProperty, typeof(Button));
        MinWidth = 190;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Ui.SetGlyph(this, "");
        ToolTip = Loc.T("Нажмите и введите сочетание. Backspace — убрать, Esc — отмена.");
        Loaded += (_, _) => UpdateText();
    }

    public string? Hotkey
    {
        get => (string?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_recording) return;
        _recording = true;
        App.Host.Hotkeys.Suspended = true;
        Content = Loc.T("Нажмите сочетание…");
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            Stop();
            return;
        }
        if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
        {
            Hotkey = null;
            Stop();
            return;
        }
        if (Core.Hotkey.IsModifierKey(key))
        {
            var preview = new Core.Hotkey(modifiers, Key.None).ToString().Replace("+None", "").Replace("None", "");
            Content = string.IsNullOrEmpty(preview) ? Loc.T("Нажмите сочетание…") : preview.Replace("+", " + ") + " + …";
            return;
        }
        // Без модификатора разрешаем только F1–F24: иначе перехватим обычный ввод.
        if (modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24)) return;

        Hotkey = new Core.Hotkey(modifiers, key).ToString();
        Stop();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_recording) Stop();
    }

    private void Stop()
    {
        _recording = false;
        App.Host.Hotkeys.Suspended = false;
        GetBindingExpression(HotkeyProperty)?.UpdateSource();
        UpdateText();
    }

    private void UpdateText()
    {
        if (_recording) return;
        Content = Core.Hotkey.TryParse(Hotkey, out var hotkey) ? hotkey.Display : Loc.T("Не задано");
    }
}
