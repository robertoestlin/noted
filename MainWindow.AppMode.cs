using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Noted;

public partial class MainWindow
{
    public enum AppMode
    {
        ShortTerm = 0,
        LongTerm = 1,
        Documentation = 2
    }

    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;

    private AppMode _appMode = AppMode.ShortTerm;
    private bool _ltViewBuilt;
    private bool _docViewBuilt;
    private IntPtr _keyboardModeHook = IntPtr.Zero;
    private HookProc? _keyboardModeHookProc;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private void InitializeAppMode()
    {
        // Align runtime state with XAML defaults (Short-Term visible; task panel / menu gating).
        SwitchToMode(AppMode.ShortTerm);
    }

    private void MenuAppModeShortTerm_Click(object sender, RoutedEventArgs e)
        => SwitchToMode(AppMode.ShortTerm);

    private void MenuAppModeLongTerm_Click(object sender, RoutedEventArgs e)
        => SwitchToMode(AppMode.LongTerm);

    private void MenuAppModeDocumentation_Click(object sender, RoutedEventArgs e)
        => SwitchToMode(AppMode.Documentation);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryInstallAppModeKeyboardHook();
    }

    private void TryInstallAppModeKeyboardHook()
    {
        if (_keyboardModeHook != IntPtr.Zero)
            return;
        _keyboardModeHookProc ??= OnLowLevelKeyboardHookForAppModes;
        _keyboardModeHook = SetWindowsHookEx(WhKeyboardLl, _keyboardModeHookProc, IntPtr.Zero, 0);
    }

    private void UninstallAppModeKeyboardHook()
    {
        if (_keyboardModeHook == IntPtr.Zero)
            return;
        UnhookWindowsHookEx(_keyboardModeHook);
        _keyboardModeHook = IntPtr.Zero;
    }

    private IntPtr OnLowLevelKeyboardHookForAppModes(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (IsWinKeyHeld()
                && TryMapModeSwitchVkToAppMode(vk, out var mode)
                && IsOurAppForeground())
            {
                void Switch()
                {
                    if (IsLoaded)
                        SwitchToMode(mode);
                }

                if (Dispatcher.CheckAccess())
                    Switch();
                else
                    Dispatcher.BeginInvoke(Switch);

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_keyboardModeHook, nCode, wParam, lParam);
    }

    private static bool IsWinKeyHeld()
        => (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    private static bool TryMapModeSwitchVkToAppMode(int vk, out AppMode mode)
    {
        switch (vk)
        {
            case 0x31:
            case 0x61:
                mode = AppMode.ShortTerm;
                return true;
            case 0x32:
            case 0x62:
                mode = AppMode.LongTerm;
                return true;
            case 0x33:
            case 0x63:
                mode = AppMode.Documentation;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    private bool IsOurAppForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
            return false;
        _ = GetWindowThreadProcessId(fg, out uint pid);
        return pid == Environment.ProcessId;
    }

    /// <summary>
    /// Win+1 / Win+2 / Win+3 are handled via a low-level hook while this process owns the
    /// foreground window; Windows does not deliver those chords through normal WPF key routing.
    /// </summary>
    private void TryHandleAppModeWinNumberKeys(KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Windows) == 0)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        AppMode? mode = key switch
        {
            Key.D1 or Key.NumPad1 => AppMode.ShortTerm,
            Key.D2 or Key.NumPad2 => AppMode.LongTerm,
            Key.D3 or Key.NumPad3 => AppMode.Documentation,
            _ => null
        };
        if (mode == null)
            return;

        SwitchToMode(mode.Value);
        e.Handled = true;
    }

    private void SwitchToMode(AppMode mode)
    {
        _appMode = mode;

        if (ShortTermView != null)
            ShortTermView.Visibility = mode == AppMode.ShortTerm ? Visibility.Visible : Visibility.Collapsed;
        if (LongTermView != null)
            LongTermView.Visibility = mode == AppMode.LongTerm ? Visibility.Visible : Visibility.Collapsed;
        if (DocumentationView != null)
            DocumentationView.Visibility = mode == AppMode.Documentation ? Visibility.Visible : Visibility.Collapsed;

        // Collapse the task panel visually when not in Short-Term, but preserve the user's
        // open/closed preference so it reapplies when they switch back.
        UpdateTodoPanelVisibility();

        ApplyModeGating();

        // Lazy-build LT/Doc views the first time their mode is opened so startup stays cheap.
        if (mode == AppMode.LongTerm && !_ltViewBuilt)
        {
            BuildLongTermView();
            _ltViewBuilt = true;
        }
        if (mode == AppMode.Documentation && !_docViewBuilt)
        {
            BuildDocumentationView();
            _docViewBuilt = true;
        }

        // Flush focus into the relevant view.
        if (mode == AppMode.LongTerm)
            FocusActiveLongTermPageEditor();
        else if (mode == AppMode.Documentation)
            FocusActiveDocPageEditor();
    }

    /// <summary>Enables/disables tab- and task-panel-specific menu entries based on current mode.</summary>
    private void ApplyModeGating()
    {
        bool isShortTerm = _appMode == AppMode.ShortTerm;
        bool showSelectionActionsMenu = _appMode != AppMode.Documentation;
        if (MenuItemSelectionActions != null)
            MenuItemSelectionActions.Visibility = showSelectionActionsMenu ? Visibility.Visible : Visibility.Collapsed;
        if (SeparatorBeforeSelectionActions != null)
            SeparatorBeforeSelectionActions.Visibility = showSelectionActionsMenu ? Visibility.Visible : Visibility.Collapsed;

        // Tab-bound menu items
        if (MenuItemNewTab != null)            MenuItemNewTab.IsEnabled = isShortTerm;
        if (MenuItemCloseTab != null)          MenuItemCloseTab.IsEnabled = isShortTerm;
        if (MenuItemGoToTab != null)           MenuItemGoToTab.IsEnabled = isShortTerm;
        if (MenuItemReopenClosedTab != null)   MenuItemReopenClosedTab.IsEnabled = isShortTerm;
        if (MenuItemTabCleanup != null)        MenuItemTabCleanup.IsEnabled = isShortTerm;
        if (MenuItemTabSync != null)           MenuItemTabSync.IsEnabled = isShortTerm;
        if (MenuItemRecoverTabs != null)       MenuItemRecoverTabs.IsEnabled = isShortTerm;

        // Task panel toggle (F3)
        if (MenuItemTaskPanel != null)         MenuItemTaskPanel.IsEnabled = isShortTerm;
    }

    /// <summary>Returns true if the F3 toggle (and similar tab-only commands) should currently be active.</summary>
    private bool IsTabModeActive() => _appMode == AppMode.ShortTerm;
}
