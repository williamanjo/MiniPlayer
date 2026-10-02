using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace MiniPlayer.Services;

public enum HotkeyAction { PlayPause, Next, Previous, VolumeUp, VolumeDown, Mute, TogglePlayer }

/// <summary>A key combination, stored in settings as text like "Ctrl+Alt+Right".</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": mods |= ModifierKeys.Windows; break;
                default: if (Enum.TryParse<Key>(part, true, out var k)) key = k; break;
            }
        }
        return key is null || mods == ModifierKeys.None ? null : new Hotkey(mods, key.Value);
    }

    /// <summary>Settings form: "Ctrl+Alt+Right".</summary>
    public string Serialize() => string.Join("+", Parts(Key.ToString()));

    /// <summary>Display form: "Ctrl + Alt + →".</summary>
    public override string ToString() => string.Join(" + ", Parts(Key switch
    {
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Space => "Espaço",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        _ when Key >= Key.D0 && Key <= Key.D9 => ((int)(Key - Key.D0)).ToString(),
        _ => Key.ToString(),
    }));

    IEnumerable<string> Parts(string key)
    {
        if (Modifiers.HasFlag(ModifierKeys.Control)) yield return "Ctrl";
        if (Modifiers.HasFlag(ModifierKeys.Alt)) yield return "Alt";
        if (Modifiers.HasFlag(ModifierKeys.Shift)) yield return "Shift";
        if (Modifiers.HasFlag(ModifierKeys.Windows)) yield return "Win";
        yield return key;
    }
}

/// <summary>System-wide hotkeys (RegisterHotKey on a hidden message window).</summary>
public sealed class HotkeyService : IDisposable
{
    public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.PlayPause] = "Ctrl+Alt+Home",
        [HotkeyAction.Next] = "Ctrl+Alt+Right",
        [HotkeyAction.Previous] = "Ctrl+Alt+Left",
        [HotkeyAction.VolumeUp] = "Ctrl+Alt+Up",
        [HotkeyAction.VolumeDown] = "Ctrl+Alt+Down",
        [HotkeyAction.Mute] = "Ctrl+Alt+M",
        [HotkeyAction.TogglePlayer] = "Ctrl+Alt+P",
    };

    public static string Label(HotkeyAction action) => action switch
    {
        HotkeyAction.PlayPause => "Play / Pause",
        HotkeyAction.Next => "Próxima música",
        HotkeyAction.Previous => "Música anterior",
        HotkeyAction.VolumeUp => "Aumentar volume do app",
        HotkeyAction.VolumeDown => "Diminuir volume do app",
        HotkeyAction.Mute => "Mudo",
        HotkeyAction.TogglePlayer => "Mostrar / ocultar o player",
        _ => action.ToString(),
    };

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly HwndSource _window;
    readonly HashSet<HotkeyAction> _registered = [];
    readonly HashSet<HotkeyAction> _failed = [];

    public HotkeyService()
    {
        // Message-only window: receives WM_HOTKEY, never shown.
        _window = new HwndSource(new HwndSourceParameters("MiniPlayerHotkeys") { ParentWindow = new IntPtr(-3) });
        _window.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Actions whose combination is taken by another app.</summary>
    public IReadOnlySet<HotkeyAction> Failed => _failed;

    /// <summary>(Re)registers everything; settings map action name → "Ctrl+Alt+Right" (empty = off).</summary>
    public void Apply(IReadOnlyDictionary<string, string> bindings)
    {
        foreach (var action in _registered) UnregisterHotKey(_window.Handle, (int)action + 1);
        _registered.Clear();
        _failed.Clear();

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            if (!bindings.TryGetValue(action.ToString(), out var text) || Hotkey.Parse(text) is not { } hotkey) continue;
            var mods = (hotkey.Modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0)
                     | (hotkey.Modifiers.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0)
                     | (hotkey.Modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0)
                     | (hotkey.Modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);
            // Holding the volume keys should keep changing it; everything else fires once.
            if (action is not (HotkeyAction.VolumeUp or HotkeyAction.VolumeDown)) mods |= MOD_NOREPEAT;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
            if (RegisterHotKey(_window.Handle, (int)action + 1, mods, vk)) _registered.Add(action);
            else _failed.Add(action);
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var action = (HotkeyAction)(wParam.ToInt32() - 1);
            if (Enum.IsDefined(action)) Pressed?.Invoke(action);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var action in _registered) UnregisterHotKey(_window.Handle, (int)action + 1);
        _window.Dispose();
    }
}
