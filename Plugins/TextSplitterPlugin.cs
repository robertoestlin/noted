using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private void ShowTextSplitterDialog()
    {
        var doc = CurrentDoc();
        if (doc == null)
        {
            MessageBox.Show(
                this,
                "No active tab to split.",
                "Text Splitter",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editor = doc.Editor;
        var lineNumber = Math.Clamp(editor.TextArea.Caret.Line, 1, Math.Max(1, editor.Document.LineCount));
        var line = editor.Document.GetLineByNumber(lineNumber);
        var lineText = editor.Document.GetText(line);
        var newline = editor.Document.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var dlg = new Window
        {
            Title = "Text Splitter",
            Width = 1200,
            Height = 900,
            MinWidth = 900,
            MinHeight = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var status = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var btnClose = new Button
        {
            Content = "Close",
            Width = 90,
            IsCancel = true
        };
        closeRow.Children.Add(btnClose);
        bottom.Children.Add(status);
        bottom.Children.Add(closeRow);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        void SetStatus(string message, Brush? brush = null)
        {
            status.Text = message;
            status.Foreground = brush ?? Brushes.DimGray;
        }

        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = $"Current line: {lineNumber}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Line text",
            Margin = new Thickness(0, 0, 0, 4)
        });

        var txtLinePreview = new TextBox
        {
            Text = lineText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 110,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        panel.Children.Add(txtLinePreview);

        panel.Children.Add(new TextBlock
        {
            Text = "Split character(s)",
            Margin = new Thickness(0, 10, 0, 4)
        });

        var splitInputHost = new Grid
        {
            Width = 220
        };
        panel.Children.Add(splitInputHost);

        var splitCharsHighlight = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(173, 216, 230)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 20,
            Width = 0
        };
        splitInputHost.Children.Add(splitCharsHighlight);

        var txtSplitChars = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            Background = Brushes.Transparent
        };
        splitInputHost.Children.Add(txtSplitChars);

        var chkKeepOriginal = new CheckBox
        {
            Content = "Keep original line",
            Margin = new Thickness(0, 8, 0, 0),
            IsChecked = false
        };
        panel.Children.Add(chkKeepOriginal);

        panel.Children.Add(new TextBlock
        {
            Text = "Prefix for each split line",
            Margin = new Thickness(0, 10, 0, 4)
        });

        var txtPrefix = new TextBox
        {
            Width = 400,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        panel.Children.Add(txtPrefix);

        panel.Children.Add(new TextBlock
        {
            Text = "Suffix for each split line",
            Margin = new Thickness(0, 10, 0, 4)
        });

        var txtSuffix = new TextBox
        {
            Width = 400,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        panel.Children.Add(txtSuffix);

        panel.Children.Add(new TextBlock
        {
            Text = "Remove from each line",
            Margin = new Thickness(0, 10, 0, 4)
        });

        var txtRemove = new TextBox
        {
            Width = 400,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        panel.Children.Add(txtRemove);

        panel.Children.Add(new TextBlock
        {
            Text = "Preview",
            Margin = new Thickness(0, 10, 0, 4)
        });

        var txtSplitPreview = new TextBox
        {
            Text = lineText,
            IsReadOnly = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 90,
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        panel.Children.Add(txtSplitPreview);
        panel.Children.Add(new TextBlock
        {
            Text = "Output is editable. Any change to splitter inputs regenerates and overwrites it.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var btnSplit = new Button
        {
            Content = "Split Current Line",
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
            IsDefault = true
        };
        panel.Children.Add(btnSplit);

        bool TryBuildSplitReplacement(out string replacement, out int partCount)
        {
            replacement = string.Empty;
            partCount = 0;
            if (string.IsNullOrEmpty(txtSplitChars.Text))
                return false;

            var delimiterText = txtSplitChars.Text;
            static List<string> NormalizeParts(IEnumerable<string> rawParts)
            {
                var parts = new List<string>();
                foreach (var part in rawParts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        parts.Add(trimmed);
                }

                return parts;
            }

            // Prefer exact multi-character delimiter (e.g. "” ").
            var values = NormalizeParts(lineText.Split([delimiterText], StringSplitOptions.None));

            // Fallback: split by any entered delimiter character.
            if (values.Count <= 1)
            {
                var delimiters = delimiterText.ToHashSet();
                var splitAnyWhitespace = delimiterText.Contains(' ', StringComparison.Ordinal);
                var byAnyChar = new List<string>();
                var current = new StringBuilder();

                foreach (var ch in lineText)
                {
                    var isDelimiter = delimiters.Contains(ch) || (splitAnyWhitespace && char.IsWhiteSpace(ch));
                    if (isDelimiter)
                    {
                        byAnyChar.Add(current.ToString());
                        current.Clear();
                        continue;
                    }

                    current.Append(ch);
                }

                byAnyChar.Add(current.ToString());
                values = NormalizeParts(byAnyChar);
            }

            if (values.Count <= 1)
                return false;

            var removeText = txtRemove.Text ?? string.Empty;
            var prefix = txtPrefix.Text ?? string.Empty;
            var suffix = txtSuffix.Text ?? string.Empty;
            for (var i = 0; i < values.Count; i++)
            {
                var normalized = values[i];
                if (!string.IsNullOrEmpty(removeText))
                    normalized = normalized.Replace(removeText, string.Empty, StringComparison.Ordinal);

                values[i] = $"{prefix}{normalized}{suffix}";
            }

            partCount = values.Count;
            var splitResult = string.Join(newline, values);
            replacement = chkKeepOriginal.IsChecked == true
                ? string.Join(newline, [lineText, string.Empty, splitResult])
                : splitResult;
            return true;
        }

        void RefreshPreview()
        {
            void UpdateSplitInputHighlight()
            {
                var text = txtSplitChars.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    splitCharsHighlight.Width = 0;
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(txtSplitChars).PixelsPerDip;
                var typeface = new Typeface(
                    txtSplitChars.FontFamily,
                    txtSplitChars.FontStyle,
                    txtSplitChars.FontWeight,
                    txtSplitChars.FontStretch);
                var formatted = new FormattedText(
                    text,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    txtSplitChars.FontSize,
                    Brushes.Black,
                    dpi);
                var textWidth = formatted.WidthIncludingTrailingWhitespace;
                var available = Math.Max(0, txtSplitChars.ActualWidth - 8);
                splitCharsHighlight.Width = Math.Max(0, Math.Min(textWidth + 8, available));
            }

            UpdateSplitInputHighlight();

            if (string.IsNullOrEmpty(txtSplitChars.Text))
            {
                txtSplitPreview.Text = lineText;
                SetStatus("Enter one or more split characters to preview.");
                return;
            }

            if (!TryBuildSplitReplacement(out var preview, out var partCount))
            {
                txtSplitPreview.Text = lineText;
                SetStatus("Split character not found in the current line.", Brushes.IndianRed);
                return;
            }

            txtSplitPreview.Text = preview;
            SetStatus($"Preview ready: {partCount} parts.");
        }

        txtSplitChars.TextChanged += (_, _) => RefreshPreview();
        txtSplitChars.SizeChanged += (_, _) => RefreshPreview();
        txtPrefix.TextChanged += (_, _) => RefreshPreview();
        txtSuffix.TextChanged += (_, _) => RefreshPreview();
        txtRemove.TextChanged += (_, _) => RefreshPreview();
        chkKeepOriginal.Checked += (_, _) => RefreshPreview();
        chkKeepOriginal.Unchecked += (_, _) => RefreshPreview();
        RefreshPreview();

        btnSplit.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(txtSplitChars.Text))
            {
                SetStatus("Enter one or more split characters.", Brushes.IndianRed);
                txtSplitChars.Focus();
                return;
            }

            if (!TryBuildSplitReplacement(out _, out _))
            {
                SetStatus("Split character not found in the current line.", Brushes.IndianRed);
                return;
            }

            var replacement = txtSplitPreview.Text ?? string.Empty;

            editor.Document.BeginUpdate();
            try
            {
                editor.Document.Replace(line.Offset, line.Length, replacement);
            }
            finally
            {
                editor.Document.EndUpdate();
            }

            editor.CaretOffset = line.Offset;
            editor.Focus();
            dlg.Close();
        };

        btnClose.Click += (_, _) => dlg.Close();

        root.Children.Add(panel);
        dlg.Content = root;
        SetStatus("Choose one character and split the current line.");
        dlg.ShowDialog();
    }
}
