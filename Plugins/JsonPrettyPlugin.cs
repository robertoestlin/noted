using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace Noted;

public partial class MainWindow
{
    private void ShowJsonPrettyDialog()
    {
        var dlg = new Window
        {
            Title = "JSON Pretty",
            Width = 1100,
            Height = 720,
            MinWidth = 700,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true,
            IsDefault = true
        };
        closeRow.Children.Add(btnClose);
        bottom.Children.Add(status);
        bottom.Children.Add(closeRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        topPanel.Children.Add(new TextBlock
        {
            Text = "Paste JSON on the left. The pretty-printed result appears on the right. Nothing is saved when this window closes.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        var chkSyntaxHighlight = new CheckBox
        {
            Content = "Syntax highlighting",
            IsChecked = true,
            Margin = new Thickness(0, 4, 0, 0)
        };
        topPanel.Children.Add(chkSyntaxHighlight);
        DockPanel.SetDock(topPanel, Dock.Top);
        root.Children.Add(topPanel);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var lblIn = new TextBlock
        {
            Text = "Input (raw JSON)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetColumn(lblIn, 0);
        Grid.SetRow(lblIn, 0);
        grid.Children.Add(lblIn);

        var lblOut = new TextBlock
        {
            Text = "Output (pretty)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetColumn(lblOut, 2);
        Grid.SetRow(lblOut, 0);
        grid.Children.Add(lblOut);

        var txtIn = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(txtIn, 0);
        Grid.SetRow(txtIn, 1);
        grid.Children.Add(txtIn);

        var editorOut = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Padding = new Thickness(4)
        };
        editorOut.Options.HighlightCurrentLine = false;
        editorOut.SyntaxHighlighting = JsonSyntaxHighlighting.Value;
        var editorBorder = new Border
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            Child = editorOut
        };
        Grid.SetColumn(editorBorder, 2);
        Grid.SetRow(editorBorder, 1);
        grid.Children.Add(editorBorder);

        root.Children.Add(grid);

        var statusClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        statusClearTimer.Tick += (_, _) =>
        {
            statusClearTimer.Stop();
            status.Text = string.Empty;
        };

        void SetStatus(string message, Brush? brush = null, bool autoClear = false)
        {
            statusClearTimer.Stop();
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
            if (autoClear)
                statusClearTimer.Start();
        }

        void Format()
        {
            var raw = txtIn.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                editorOut.Text = string.Empty;
                SetStatus("Waiting for JSON input.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                editorOut.Text = JsonSerializer.Serialize(doc.RootElement, options);
                SetStatus("Formatted OK.", autoClear: true);
            }
            catch (JsonException ex)
            {
                editorOut.Text = string.Empty;
                var location = ex.LineNumber.HasValue
                    ? $" (line {ex.LineNumber + 1}, pos {ex.BytePositionInLine + 1})"
                    : string.Empty;
                SetStatus($"Invalid JSON{location}: {ex.Message}", Brushes.IndianRed);
            }
            catch (Exception ex)
            {
                editorOut.Text = string.Empty;
                SetStatus($"Error: {ex.Message}", Brushes.IndianRed);
            }
        }

        void ApplyHighlightingToggle()
        {
            editorOut.SyntaxHighlighting = chkSyntaxHighlight.IsChecked == true
                ? JsonSyntaxHighlighting.Value
                : null;
        }

        txtIn.TextChanged += (_, _) => Format();
        chkSyntaxHighlight.Checked += (_, _) => ApplyHighlightingToggle();
        chkSyntaxHighlight.Unchecked += (_, _) => ApplyHighlightingToggle();
        btnClose.Click += (_, _) => dlg.Close();
        dlg.Closed += (_, _) => statusClearTimer.Stop();

        SetStatus("Waiting for JSON input.");
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
