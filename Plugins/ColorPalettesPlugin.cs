using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Services;

namespace Noted;

public partial class MainWindow
{
    private void ShowColorPalettesDialog()
    {
        var dlg = new ColorPalettesWindow { Owner = this };
        dlg.ShowDialog();
    }
}

internal sealed class ColorPalettesWindow : Window
{
    private readonly ColorPaletteService _service = new();
    private readonly ListBox _palettesList;
    private readonly DataGrid _colorsGrid;
    private readonly Button _btnRename;
    private readonly Button _btnDelete;
    private readonly Button _btnAddColor;
    private readonly Button _btnEditColor;
    private readonly Button _btnRemoveColor;
    private readonly Button _btnMoveUp;
    private readonly Button _btnMoveDown;
    private readonly TextBlock _emptyHint;
    private List<ColorPalette> _palettes = new();

    public ColorPalettesWindow()
    {
        Title = "Color palettes";
        Width = 720;
        Height = 500;
        MinWidth = 600;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };

        // Bottom close row.
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var btnClose = new Button { Content = "Close", Width = 90, IsCancel = true, IsDefault = true };
        btnClose.Click += (_, _) => Close();
        bottom.Children.Add(btnClose);
        root.Children.Add(bottom);

        // Two-pane grid.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left pane: palettes.
        var leftPane = new DockPanel();
        leftPane.Children.Add(BuildLeftHeader());

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
        };
        DockPanel.SetDock(leftButtons, Dock.Bottom);

        var btnNew = new Button { Content = "+ New", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        btnNew.Click += (_, _) => CreatePalette();
        _btnRename = new Button { Content = "Rename", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        _btnRename.Click += (_, _) => RenameSelectedPalette();
        _btnDelete = new Button { Content = "Delete", Padding = new Thickness(8, 3, 8, 3) };
        _btnDelete.Click += (_, _) => DeleteSelectedPalette();
        leftButtons.Children.Add(btnNew);
        leftButtons.Children.Add(_btnRename);
        leftButtons.Children.Add(_btnDelete);
        leftPane.Children.Add(leftButtons);

        _palettesList = new ListBox();
        _palettesList.SelectionChanged += (_, _) =>
        {
            ReloadColorsGrid();
            UpdateButtonStates();
        };
        leftPane.Children.Add(_palettesList);
        Grid.SetColumn(leftPane, 0);
        grid.Children.Add(leftPane);

        // Right pane: colors of selected palette.
        var rightPane = new DockPanel();

        var rightHeader = new TextBlock
        {
            Text = "Colors",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(rightHeader, Dock.Top);
        rightPane.Children.Add(rightHeader);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(rightButtons, Dock.Bottom);

        _btnAddColor = new Button { Content = "+ Add color", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        _btnAddColor.Click += (_, _) => AddColorToSelectedPalette();
        _btnEditColor = new Button { Content = "Edit…", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        _btnEditColor.Click += (_, _) => EditSelectedColor();
        _btnRemoveColor = new Button { Content = "Remove", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        _btnRemoveColor.Click += (_, _) => RemoveSelectedColor();
        _btnMoveUp = new Button { Content = "Move up", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        _btnMoveUp.Click += (_, _) => MoveSelectedColor(-1);
        _btnMoveDown = new Button { Content = "Move down", Padding = new Thickness(8, 3, 8, 3) };
        _btnMoveDown.Click += (_, _) => MoveSelectedColor(1);

        rightButtons.Children.Add(_btnAddColor);
        rightButtons.Children.Add(_btnEditColor);
        rightButtons.Children.Add(_btnRemoveColor);
        rightButtons.Children.Add(_btnMoveUp);
        rightButtons.Children.Add(_btnMoveDown);
        rightPane.Children.Add(rightButtons);

        _emptyHint = new TextBlock
        {
            Text = "This palette has no colors yet. Click \"+ Add color\" to add one.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(4, 12, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        DockPanel.SetDock(_emptyHint, Dock.Top);
        rightPane.Children.Add(_emptyHint);

        _colorsGrid = BuildColorsGrid();
        _colorsGrid.SelectionChanged += (_, _) => UpdateButtonStates();
        _colorsGrid.CellEditEnding += OnGridCellEditEnding;
        rightPane.Children.Add(_colorsGrid);

        Grid.SetColumn(rightPane, 2);
        grid.Children.Add(rightPane);

        root.Children.Add(grid);

        Content = root;

        ReloadPaletteList();
    }

    private static FrameworkElement BuildLeftHeader()
    {
        var header = new TextBlock
        {
            Text = "Palettes",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(header, Dock.Top);
        return header;
    }

    private DataGrid BuildColorsGrid()
    {
        var dg = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserSortColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeight = 28,
        };

        var swatchTemplate = new DataTemplate();
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(FrameworkElement.WidthProperty, 20.0);
        borderFactory.SetValue(FrameworkElement.HeightProperty, 20.0);
        borderFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));
        borderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(ColorRow.Brush)));
        swatchTemplate.VisualTree = borderFactory;

        dg.Columns.Add(new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(40),
            CellTemplate = swatchTemplate,
            IsReadOnly = true,
        });
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Binding = new Binding(nameof(ColorRow.Name)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "Hex",
            Binding = new Binding(nameof(ColorRow.Hex)) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(140),
        });

        return dg;
    }

    private void ReloadPaletteList()
    {
        var previouslySelected = (_palettesList.SelectedItem as PaletteRow)?.Name;
        _palettes = _service.LoadPalettes();
        var ordered = _palettes
            .OrderBy(p => _service.IsDefaultPalette(p.Name) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PaletteRow { Name = p.Name, IsDefault = _service.IsDefaultPalette(p.Name) })
            .ToList();

        _palettesList.ItemsSource = ordered;
        _palettesList.DisplayMemberPath = nameof(PaletteRow.DisplayName);

        if (previouslySelected != null)
        {
            var match = ordered.FirstOrDefault(p => string.Equals(p.Name, previouslySelected, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                _palettesList.SelectedItem = match;
        }
        if (_palettesList.SelectedItem == null && ordered.Count > 0)
            _palettesList.SelectedIndex = 0;

        ReloadColorsGrid();
        UpdateButtonStates();
    }

    private void ReloadColorsGrid()
    {
        var selected = _palettesList.SelectedItem as PaletteRow;
        if (selected == null)
        {
            _colorsGrid.ItemsSource = null;
            _emptyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var palette = _palettes.FirstOrDefault(p => string.Equals(p.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
        var rows = (palette?.Colors ?? new List<NamedColor>())
            .Select(c => new ColorRow { Name = c.Name, Hex = c.Hex })
            .ToList();

        _colorsGrid.ItemsSource = rows;
        _emptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtonStates()
    {
        var selected = _palettesList.SelectedItem as PaletteRow;
        var hasSelection = selected != null;
        var canModify = hasSelection && !selected!.IsDefault;

        _btnRename.IsEnabled = canModify;
        _btnRename.ToolTip = canModify ? null : "The default palette cannot be renamed.";
        _btnDelete.IsEnabled = canModify;
        _btnDelete.ToolTip = canModify ? null : "The default palette cannot be deleted.";

        _btnAddColor.IsEnabled = hasSelection;

        var rowSelected = _colorsGrid.SelectedItem is ColorRow;
        _btnEditColor.IsEnabled = hasSelection && rowSelected;
        _btnRemoveColor.IsEnabled = hasSelection && rowSelected;

        var rows = _colorsGrid.ItemsSource as IList<ColorRow>;
        var idx = rowSelected ? rows!.IndexOf((ColorRow)_colorsGrid.SelectedItem) : -1;
        _btnMoveUp.IsEnabled = hasSelection && rowSelected && idx > 0;
        _btnMoveDown.IsEnabled = hasSelection && rowSelected && idx >= 0 && idx < rows!.Count - 1;
    }

    private void CreatePalette()
    {
        var name = PromptForText("New palette", "Palette name", "");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!_service.CreatePalette(name))
        {
            MessageBox.Show(this, $"A palette named \"{name.Trim()}\" already exists.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ReloadPaletteList();
        SelectPaletteByName(name.Trim());
    }

    private void RenameSelectedPalette()
    {
        if (_palettesList.SelectedItem is not PaletteRow row || row.IsDefault) return;

        var newName = PromptForText("Rename palette", "New name", row.Name);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName.Trim(), row.Name, StringComparison.Ordinal))
            return;

        if (!_service.RenamePalette(row.Name, newName))
        {
            MessageBox.Show(this, "Could not rename: name is empty, already in use, or this palette cannot be renamed.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ReloadPaletteList();
        SelectPaletteByName(newName.Trim());
    }

    private void DeleteSelectedPalette()
    {
        if (_palettesList.SelectedItem is not PaletteRow row || row.IsDefault) return;

        var result = MessageBox.Show(this,
            $"Delete palette \"{row.Name}\" and all its colors?",
            Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        if (!_service.DeletePalette(row.Name))
            return;

        ReloadPaletteList();
    }

    private void AddColorToSelectedPalette()
    {
        if (_palettesList.SelectedItem is not PaletteRow row) return;

        var pick = new DrawingColorPickerWindow(null) { Owner = this };
        if (pick.ShowDialog() != true || string.IsNullOrWhiteSpace(pick.ResultHex))
            return;

        var name = PromptForText("Name this color", "Display name", "");
        if (string.IsNullOrWhiteSpace(name))
            return;

        _service.AddOrUpdateColor(row.Name, new NamedColor { Name = name, Hex = pick.ResultHex! });
        ReloadPaletteAndKeepSelection();
    }

    private void EditSelectedColor()
    {
        if (_palettesList.SelectedItem is not PaletteRow row) return;
        if (_colorsGrid.SelectedItem is not ColorRow color) return;

        var pick = new DrawingColorPickerWindow(color.Hex) { Owner = this };
        if (pick.ShowDialog() != true || string.IsNullOrWhiteSpace(pick.ResultHex))
            return;

        var palettes = _service.LoadPalettes();
        var palette = palettes.FirstOrDefault(p => string.Equals(p.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        var entry = palette?.Colors.FirstOrDefault(c => string.Equals(c.Name, color.Name, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;

        entry.Hex = pick.ResultHex!;
        _service.SavePalettes(palettes);
        ReloadPaletteAndKeepSelection();
    }

    private void RemoveSelectedColor()
    {
        if (_palettesList.SelectedItem is not PaletteRow row) return;
        if (_colorsGrid.SelectedItem is not ColorRow color) return;

        _service.RemoveColor(row.Name, color.Name);
        ReloadPaletteAndKeepSelection();
    }

    private void MoveSelectedColor(int delta)
    {
        if (_palettesList.SelectedItem is not PaletteRow row) return;
        if (_colorsGrid.SelectedItem is not ColorRow color) return;

        var palettes = _service.LoadPalettes();
        var palette = palettes.FirstOrDefault(p => string.Equals(p.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (palette == null) return;
        var idx = palette.Colors.FindIndex(c => string.Equals(c.Name, color.Name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        var target = idx + delta;
        if (target < 0 || target >= palette.Colors.Count) return;

        var moved = palette.Colors[idx];
        palette.Colors.RemoveAt(idx);
        palette.Colors.Insert(target, moved);
        _service.SavePalettes(palettes);

        ReloadPaletteAndKeepSelection();
        if (_colorsGrid.ItemsSource is IList<ColorRow> rows)
        {
            var newSel = rows.FirstOrDefault(r => string.Equals(r.Name, moved.Name, StringComparison.OrdinalIgnoreCase));
            if (newSel != null)
                _colorsGrid.SelectedItem = newSel;
        }
    }

    private void OnGridCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (_palettesList.SelectedItem is not PaletteRow row) return;
        if (e.Row.Item is not ColorRow editedRow) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var palettes = _service.LoadPalettes();
            var palette = palettes.FirstOrDefault(p => string.Equals(p.Name, row.Name, StringComparison.OrdinalIgnoreCase));
            if (palette == null) return;

            var rows = _colorsGrid.ItemsSource as IList<ColorRow>;
            if (rows == null) return;

            var newColors = new List<NamedColor>();
            foreach (var r in rows)
            {
                if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Hex))
                    continue;
                if (!DrawingColorUtilities.TryParseColorString(r.Hex, out var parsed) || parsed.A == 0)
                    continue;
                newColors.Add(new NamedColor { Name = r.Name.Trim(), Hex = r.Hex.Trim() });
            }

            palette.Colors = newColors;
            _service.SavePalettes(palettes);
            ReloadPaletteAndKeepSelection();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ReloadPaletteAndKeepSelection()
    {
        var selectedPalette = (_palettesList.SelectedItem as PaletteRow)?.Name;
        ReloadPaletteList();
        if (selectedPalette != null)
            SelectPaletteByName(selectedPalette);
    }

    private void SelectPaletteByName(string name)
    {
        if (_palettesList.ItemsSource is not IEnumerable<PaletteRow> rows) return;
        var match = rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            _palettesList.SelectedItem = match;
    }

    private string? PromptForText(string title, string label, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock { Text = label, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) });
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancel.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        sp.Children.Add(row);
        dlg.Content = sp;
        tb.Focus();
        tb.SelectAll();
        return dlg.ShowDialog() == true ? tb.Text : null;
    }

    private sealed class PaletteRow
    {
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public string DisplayName => IsDefault ? $"{Name}  (default)" : Name;
    }

    private sealed class ColorRow
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
        public Brush Brush
        {
            get
            {
                if (!DrawingColorUtilities.TryParseColorString(Hex, out var c) || c.A == 0)
                    return Brushes.Transparent;
                var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                brush.Freeze();
                return brush;
            }
        }
    }
}
