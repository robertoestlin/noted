using System.Windows;
using System.Windows.Controls;

namespace Noted;

public partial class MainWindow
{
    public enum AppMode
    {
        ShortTerm = 0,
        LongTerm = 1,
        Documentation = 2
    }

    private AppMode _appMode = AppMode.ShortTerm;
    private bool _modeComboInitialized;
    private bool _ltViewBuilt;
    private bool _docViewBuilt;

    private void InitializeModeComboBox()
    {
        if (ModeComboBox == null) return;
        ModeComboBox.Items.Clear();
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "Short-Term Notes", Tag = AppMode.ShortTerm });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "Long-Term Notes",  Tag = AppMode.LongTerm });
        ModeComboBox.Items.Add(new ComboBoxItem { Content = "Documentation",    Tag = AppMode.Documentation });
        ModeComboBox.SelectedIndex = 0;
        _modeComboInitialized = true;
        ApplyModeGating();
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_modeComboInitialized) return;
        if (ModeComboBox?.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not AppMode mode) return;
        SwitchToMode(mode);
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

        // Close the F3 task panel when leaving Short-Term so it doesn't visually
        // bleed into other modes.
        if (mode != AppMode.ShortTerm && _todoPanelVisible)
        {
            _todoPanelVisible = false;
            UpdateTodoPanelVisibility();
        }

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
