using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
        TitleBox.Text = title;
        CurrencyBox.Text = string.IsNullOrWhiteSpace(currency)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : currency.Trim();
        CurrencyBox.TextChanged += (_, _) => RefreshSum();
        foreach (var r in rows)
            _rows.Add(r);
        if (_rows.Count == 0)
            _rows.Add(SpreadsheetEditRowModel.CreateDefault());
        RowsGrid.ItemsSource = _rows;
        RowsGrid.PreviewKeyDown += RowsGrid_PreviewKeyDown;
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
    }

    /// <summary>Unit price is the last editable column; Tab adds a row and opens Description for typing.</summary>
    private void RowsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || Keyboard.Modifiers != ModifierKeys.None)
            return;

        var currentCol = RowsGrid.CurrentCell.Column;
        if (currentCol == null || RowsGrid.Columns.IndexOf(currentCol) != UnitPriceColumnIndex)
            return;

        e.Handled = true;
        RowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);

        var newRow = SpreadsheetEditRowModel.CreateDefault(blankDescription: true);
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
    }

    private const int DescriptionColumnIndex = 0;
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
        foreach (var row in _rows)
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
        foreach (var row in _rows)
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
    private string _description = "";
    private string _quantity = "";
    private string _unitPrice = "";

    public static SpreadsheetEditRowModel CreateDefault(bool blankDescription = false)
        => new()
        {
            Description = blankDescription ? "" : "New item",
            Quantity = "1",
            UnitPrice = "0"
        };

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
            return SpreadsheetAmountHelpers.FormatNumber(p);
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
