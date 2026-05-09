using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Noted;

public partial class SpreadsheetEditWindow : Window
{
    private readonly ObservableCollection<SpreadsheetEditRowModel> _rows = [];

    public SpreadsheetEditWindow(string title, string currency, IEnumerable<SpreadsheetEditRowModel> rows)
    {
        InitializeComponent();
        PreviewKeyDown += SpreadsheetEditWindow_PreviewKeyDown;
        TitleBox.Text = title;
        CurrencyBox.Text = string.IsNullOrWhiteSpace(currency)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : currency.Trim();
        CurrencyBox.TextChanged += (_, _) => RefreshSum();
        foreach (var r in rows)
            _rows.Add(r.Clone());
        if (_rows.Count == 0)
            _rows.Add(SpreadsheetEditRowModel.CreateDefault());
        RowsGrid.ItemsSource = _rows;
        RowsGrid.PreviewKeyDown += RowsGrid_PreviewKeyDown;
        RowsGrid.CellEditEnding += RowsGrid_CellEditEnding;
        foreach (var row in _rows)
            row.PropertyChanged += Row_PropertyChanged;
        _rows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (SpreadsheetEditRowModel item in e.NewItems)
                    item.PropertyChanged += Row_PropertyChanged;
            }
            RefreshSum();
        };
        RefreshSum();
        Loaded += SpreadsheetEditWindow_Loaded;
    }

    private void SpreadsheetEditWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SpreadsheetEditWindow_Loaded;
        TryBeginEditSinglePlaceholderDescription();
    }

    /// <summary>
    /// One default row ("Item #1"): focus Description and select text so typing replaces it immediately.
    /// </summary>
    private void TryBeginEditSinglePlaceholderDescription()
    {
        if (_rows.Count != 1)
            return;
        var row = _rows[0];
        if (!string.Equals(row.Description.Trim(), SpreadsheetEditRowModel.DefaultDescriptionLabel, StringComparison.Ordinal))
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                RowsGrid.ScrollIntoView(row);
                RowsGrid.Focus();
                RowsGrid.SelectedItem = row;
                RowsGrid.CurrentCell = new DataGridCellInfo(row, RowsGrid.Columns[DescriptionColumnIndex]);
                RowsGrid.BeginEdit();

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        if (Keyboard.FocusedElement is TextBox tb)
                            tb.SelectAll();
                    }));
            }));
    }

    /// <summary>Unit price is the last editable column; Tab adds a row and opens Description for typing.</summary>
    private void RowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            var currentCol = RowsGrid.CurrentCell.Column;
            if (currentCol == null || RowsGrid.Columns.IndexOf(currentCol) != UnitPriceColumnIndex)
                return;

            e.Handled = true;
            RowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);

            var newRow = SpreadsheetEditRowModel.CreateDefault(blankRowForTab: true);
            newRow.PropertyChanged += Row_PropertyChanged;
            _rows.Add(newRow);
            RowsGrid.SelectedItem = newRow;
            RefreshSum();

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    RowsGrid.ScrollIntoView(newRow);
                    RowsGrid.Focus();
                    RowsGrid.CurrentCell = new DataGridCellInfo(newRow, RowsGrid.Columns[DescriptionColumnIndex]);
                    RowsGrid.BeginEdit();
                }));
            return;
        }
    }

    private static string NormalizeCommaDecimalSeparator(string text)
        => text.Replace(',', '.');

    private void RowsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        int colIndex = RowsGrid.Columns.IndexOf(e.Column);
        if (colIndex != QuantityColumnIndex && colIndex != UnitPriceColumnIndex)
            return;

        if (e.EditingElement is not TextBox tb || e.Row.Item is not SpreadsheetEditRowModel row)
            return;

        string normalized = NormalizeCommaDecimalSeparator(tb.Text);
        if (string.Equals(normalized, tb.Text, StringComparison.Ordinal))
            return;

        tb.Text = normalized;
        if (colIndex == QuantityColumnIndex)
            row.Quantity = normalized;
        else
            row.UnitPrice = normalized;
    }

    private void SpreadsheetEditWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (Keyboard.FocusedElement is Button)
            return;

        e.Handled = true;
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        Ok_Click(this, new RoutedEventArgs());
    }

    private const int DescriptionColumnIndex = 0;
    private const int QuantityColumnIndex = 1;
    private const int UnitPriceColumnIndex = 2;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpreadsheetEditRowModel.Quantity)
            or nameof(SpreadsheetEditRowModel.UnitPrice)
            or nameof(SpreadsheetEditRowModel.Description))
            RefreshSum();
    }

    private void RefreshSum()
    {
        decimal sum = 0;
        foreach (var row in _rows)
        {
            if (!SpreadsheetAmountHelpers.TrySafeAdd(sum, row.LineTotalValue, out sum))
            {
                SumBlock.Text = "Sum: (too large)";
                return;
            }
        }

        SumBlock.Text =
            $"Sum: {SpreadsheetAmountHelpers.FormatSumWithCurrency(sum, EffectiveCurrency)}";
    }

    private string EffectiveCurrency =>
        string.IsNullOrWhiteSpace(CurrencyBox.Text)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : CurrencyBox.Text.Trim();

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        var row = SpreadsheetEditRowModel.CreateDefault();
        row.PropertyChanged += Row_PropertyChanged;
        _rows.Add(row);
        RowsGrid.SelectedItem = row;
        RefreshSum();
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not SpreadsheetEditRowModel row || _rows.Count <= 1)
            return;
        row.PropertyChanged -= Row_PropertyChanged;
        _rows.Remove(row);
        RefreshSum();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var activeRows = _rows.Where(r => !r.IsEffectivelyBlank).ToList();
        if (activeRows.Count == 0)
        {
            MessageBox.Show(
                this,
                "Add at least one row with a quantity and unit price.",
                "Edit spreadsheet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        foreach (var row in activeRows)
        {
            if (!SpreadsheetAmountHelpers.TryParseDecimal(row.Quantity, out _))
            {
                MessageBox.Show(
                    this,
                    "Enter a valid quantity for each row.",
                    "Edit spreadsheet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!SpreadsheetAmountHelpers.TryParseDecimal(row.UnitPrice, out _))
            {
                MessageBox.Show(
                    this,
                    "Enter a valid unit price for each row.",
                    "Edit spreadsheet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!SpreadsheetAmountHelpers.TryParseDecimal(row.Quantity, out var q)
                || !SpreadsheetAmountHelpers.TryParseDecimal(row.UnitPrice, out var u)
                || !SpreadsheetAmountHelpers.TrySafeMultiply(q, u, out _))
            {
                MessageBox.Show(
                    this,
                    "Quantity × unit price is too large in one or more rows.",
                    "Edit spreadsheet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        decimal sum = 0;
        foreach (var row in activeRows)
        {
            if (!SpreadsheetAmountHelpers.TrySafeAdd(sum, row.LineTotalValue, out sum))
            {
                MessageBox.Show(
                    this,
                    "The grand total is too large for the spreadsheet.",
                    "Edit spreadsheet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    public string EditedTitle => TitleBox.Text.Trim();

    public string EditedCurrency => EffectiveCurrency;

    public IReadOnlyList<SpreadsheetEditRowModel> EditedRows => _rows;
}

public sealed class SpreadsheetEditRowModel : INotifyPropertyChanged
{
    /// <summary>Default description for a new row from Add row / empty spreadsheet template.</summary>
    public const string DefaultDescriptionLabel = "Item #1";

    private string _description = "";
    private string _quantity = "";
    private string _unitPrice = "";

    /// <summary>True when the row should not be written to the spreadsheet (unused Tab-new row or cleared row).</summary>
    public bool IsEffectivelyBlank =>
        string.IsNullOrWhiteSpace(Description)
        && string.IsNullOrWhiteSpace(Quantity)
        && string.IsNullOrWhiteSpace(UnitPrice);

    /// <param name="blankRowForTab">Tab past unit price adds an empty row (no default numbers).</param>
    public static SpreadsheetEditRowModel CreateDefault(bool blankRowForTab = false)
        => new()
        {
            Description = blankRowForTab ? "" : DefaultDescriptionLabel,
            Quantity = blankRowForTab ? "" : "1",
            UnitPrice = blankRowForTab ? "" : "0",
        };

    public SpreadsheetEditRowModel Clone()
    {
        var c = new SpreadsheetEditRowModel();
        c.Description = Description;
        c.Quantity = Quantity;
        c.UnitPrice = UnitPrice;
        return c;
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value ?? "");
    }

    public string Quantity
    {
        get => _quantity;
        set => SetField(ref _quantity, value ?? "");
    }

    public string UnitPrice
    {
        get => _unitPrice;
        set => SetField(ref _unitPrice, value ?? "");
    }

    public string TotalDisplay
    {
        get
        {
            if (!SpreadsheetAmountHelpers.TryParseDecimal(Quantity, out var q)
                || !SpreadsheetAmountHelpers.TryParseDecimal(UnitPrice, out var u))
                return "";
            if (!SpreadsheetAmountHelpers.TrySafeMultiply(q, u, out var p))
                return "(too large)";
            return SpreadsheetAmountHelpers.FormatMoneyAmount(p);
        }
    }

    public decimal LineTotalValue
    {
        get
        {
            if (!SpreadsheetAmountHelpers.TryParseDecimal(Quantity, out var q)
                || !SpreadsheetAmountHelpers.TryParseDecimal(UnitPrice, out var u))
                return 0;
            return SpreadsheetAmountHelpers.TrySafeMultiply(q, u, out var p) ? p : 0;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Quantity) or nameof(UnitPrice))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalDisplay)));
    }
}
