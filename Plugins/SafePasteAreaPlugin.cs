using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private void ShowSafePasteAreaDialog()
    {
        var dlg = new Window
        {
            Title = "Safe Paste Area",
            Width = 760,
            Height = 540,
            MinWidth = 620,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var plainValue = string.Empty;
        var secretValue = string.Empty;
        var clipboardCopyVersion = 0;
        var plainVisible = true;
        var secretVisible = false;
        var suppressPlainTextSync = false;

        var root = new DockPanel { Margin = new Thickness(12) };

        var infoBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 247, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(177, 215, 253)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "Safe text area: paste values here, then clipboard is cleared after paste.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkSlateGray
            }
        };
        DockPanel.SetDock(infoBox, Dock.Top);
        root.Children.Add(infoBox);

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
            IsCancel = true,
            IsDefault = true
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

        static string BuildSecretMask(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : new string('*', text.Length);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var plainPanel = new StackPanel();
        var plainHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        plainHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        plainHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        plainHeader.Children.Add(new TextBlock
        {
            Text = "Text Paste Area",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var btnTogglePlainVisible = new Button
        {
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Toggle text visibility",
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 14,
            Content = "👁"
        };
        Grid.SetColumn(btnTogglePlainVisible, 1);
        plainHeader.Children.Add(btnTogglePlainVisible);
        plainPanel.Children.Add(plainHeader);
        var txtPlain = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 250,
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = false
        };
        plainPanel.Children.Add(txtPlain);
        var txtPlainCount = new TextBlock
        {
            Text = "Characters: 0",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 6, 0, 0)
        };
        plainPanel.Children.Add(txtPlainCount);
        var plainButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnPastePlain = new Button
        {
            Content = "Paste text",
            Padding = new Thickness(14, 6, 14, 6)
        };
        var btnCopyPlain = new Button
        {
            Content = "Copy text",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        var btnClearPlain = new Button
        {
            Content = "Clear text",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        plainButtons.Children.Add(btnPastePlain);
        plainButtons.Children.Add(btnCopyPlain);
        plainButtons.Children.Add(btnClearPlain);
        plainPanel.Children.Add(plainButtons);
        Grid.SetColumn(plainPanel, 0);
        body.Children.Add(plainPanel);

        var secretPanel = new StackPanel();
        var secretHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        secretHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        secretHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        secretHeader.Children.Add(new TextBlock
        {
            Text = "Secret Paste Area (masked)",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var btnToggleSecretVisible = new Button
        {
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Toggle secret visibility",
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 14,
            Content = "🙈"
        };
        Grid.SetColumn(btnToggleSecretVisible, 1);
        secretHeader.Children.Add(btnToggleSecretVisible);
        secretPanel.Children.Add(secretHeader);
        var txtSecretMasked = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 250,
            FontFamily = new FontFamily("Consolas, Courier New"),
            IsReadOnly = true
        };
        secretPanel.Children.Add(txtSecretMasked);
        var txtSecretCount = new TextBlock
        {
            Text = "Characters: 0",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 6, 0, 0)
        };
        secretPanel.Children.Add(txtSecretCount);
        var txtSecretWarning = new TextBlock
        {
            Text = string.Empty,
            Foreground = Brushes.IndianRed,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        secretPanel.Children.Add(txtSecretWarning);
        var secretButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnPasteSecret = new Button
        {
            Content = "Paste secret",
            Padding = new Thickness(14, 6, 14, 6)
        };
        var btnCopySecret = new Button
        {
            Content = "Copy secret",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        var btnClearSecret = new Button
        {
            Content = "Clear secret",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        secretButtons.Children.Add(btnPasteSecret);
        secretButtons.Children.Add(btnCopySecret);
        secretButtons.Children.Add(btnClearSecret);
        secretPanel.Children.Add(secretButtons);
        Grid.SetColumn(secretPanel, 2);
        body.Children.Add(secretPanel);

        root.Children.Add(body);

        bool TryReadClipboardText(out string value)
        {
            value = string.Empty;
            try
            {
                if (!Clipboard.ContainsText())
                {
                    SetStatus("Clipboard does not contain text.", Brushes.IndianRed);
                    return false;
                }

                value = Clipboard.GetText();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus($"Clipboard read failed: {ex.Message}", Brushes.IndianRed);
                return false;
            }
        }

        void TryClearClipboard()
        {
            try
            {
                Clipboard.Clear();
            }
            catch
            {
                // ignore clipboard clear failures
            }
        }

        async void CopyToClipboardWithAutoClear(string value, string successMessage)
        {
            try
            {
                Clipboard.SetText(value);
            }
            catch (Exception ex)
            {
                SetStatus($"Copy failed: {ex.Message}", Brushes.IndianRed);
                return;
            }

            clipboardCopyVersion++;
            var copyVersion = clipboardCopyVersion;
            SetStatus($"{successMessage} Clipboard will clear in 10 seconds.");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
            catch
            {
                return;
            }

            if (copyVersion != clipboardCopyVersion)
                return;

            try
            {
                if (Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), value, StringComparison.Ordinal))
                {
                    Clipboard.Clear();
                    SetStatus("Clipboard cleared.");
                }
            }
            catch
            {
                // ignore clipboard clear failures
            }
        }

        void ApplyPlainVisibility()
        {
            suppressPlainTextSync = true;
            txtPlain.Text = plainVisible ? plainValue : BuildSecretMask(plainValue);
            txtPlain.IsReadOnly = !plainVisible;
            btnTogglePlainVisible.Content = plainVisible ? "👁" : "🙈";
            btnTogglePlainVisible.ToolTip = plainVisible ? "Hide text value" : "Show text value";
            suppressPlainTextSync = false;
        }

        void UpdatePlainIndicators()
        {
            txtPlainCount.Text = $"Characters: {plainValue.Length}";
            btnCopyPlain.IsEnabled = plainValue.Length > 0;
            btnClearPlain.IsEnabled = plainValue.Length > 0;
        }

        void ApplySecretVisibility()
        {
            txtSecretMasked.Text = secretVisible ? secretValue : BuildSecretMask(secretValue);
            btnToggleSecretVisible.Content = secretVisible ? "👁" : "🙈";
            btnToggleSecretVisible.ToolTip = secretVisible ? "Hide secret value" : "Show secret value";
        }

        void UpdateSecretIndicators()
        {
            txtSecretCount.Text = $"Characters: {secretValue.Length}";
            btnCopySecret.IsEnabled = secretValue.Length > 0;
            btnClearSecret.IsEnabled = secretValue.Length > 0;

            var hasWhitespace = false;
            var hasNewLine = false;
            foreach (var ch in secretValue)
            {
                if (ch == '\r' || ch == '\n')
                {
                    hasNewLine = true;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    hasWhitespace = true;
                }
            }

            if (secretValue.Length > 0 && (hasWhitespace || hasNewLine))
            {
                if (hasWhitespace && hasNewLine)
                    txtSecretWarning.Text = "Warning: secret contains whitespace characters.\nWarning: secret contains newline characters.";
                else if (hasWhitespace)
                    txtSecretWarning.Text = "Warning: secret contains whitespace characters.";
                else
                    txtSecretWarning.Text = "Warning: secret contains newline characters.";

                txtSecretWarning.Visibility = Visibility.Visible;
            }
            else
            {
                txtSecretWarning.Text = string.Empty;
                txtSecretWarning.Visibility = Visibility.Collapsed;
            }
        }

        void PastePlainFromClipboard(bool insertAtCaret = false)
        {
            if (!TryReadClipboardText(out var value))
                return;

            if (insertAtCaret && plainVisible)
            {
                txtPlain.SelectedText = value;
            }
            else
            {
                plainValue = insertAtCaret ? plainValue + value : value;
                ApplyPlainVisibility();
                UpdatePlainIndicators();
            }

        }

        void PasteSecretFromClipboard(string sourceLabel)
        {
            if (!TryReadClipboardText(out var value))
                return;

            secretValue = value;
            ApplySecretVisibility();
            UpdateSecretIndicators();
            TryClearClipboard();
            SetStatus($"Secret pasted as masked text via {sourceLabel}. Clipboard cleared.");
        }

        btnPastePlain.Click += (_, _) => PastePlainFromClipboard();
        btnPasteSecret.Click += (_, _) => PasteSecretFromClipboard("button");

        txtPlain.TextChanged += (_, _) =>
        {
            if (suppressPlainTextSync || !plainVisible)
                return;

            plainValue = txtPlain.Text ?? string.Empty;
            UpdatePlainIndicators();
        };

        txtPlain.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PastePlainFromClipboard(insertAtCaret: true);
                e.Handled = true;
            }
        };

        txtSecretMasked.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteSecretFromClipboard("Ctrl+V");
                e.Handled = true;
            }
        };

        btnCopyPlain.Click += (_, _) =>
        {
            if (plainValue.Length == 0)
                return;

            CopyToClipboardWithAutoClear(plainValue, "Text copied.");
        };

        btnClearPlain.Click += (_, _) =>
        {
            if (plainValue.Length == 0)
                return;

            plainValue = string.Empty;
            ApplyPlainVisibility();
            UpdatePlainIndicators();
            SetStatus("Text area cleared.");
        };

        btnCopySecret.Click += (_, _) =>
        {
            if (secretValue.Length == 0)
                return;

            CopyToClipboardWithAutoClear(secretValue, "Secret copied.");
        };

        btnClearSecret.Click += (_, _) =>
        {
            if (secretValue.Length == 0)
                return;

            secretValue = string.Empty;
            ApplySecretVisibility();
            UpdateSecretIndicators();
            SetStatus("Secret area cleared.");
        };

        btnTogglePlainVisible.Click += (_, _) =>
        {
            plainVisible = !plainVisible;
            ApplyPlainVisibility();
        };

        btnToggleSecretVisible.Click += (_, _) =>
        {
            secretVisible = !secretVisible;
            ApplySecretVisibility();
        };

        ApplyPlainVisibility();
        UpdatePlainIndicators();
        ApplySecretVisibility();
        UpdateSecretIndicators();

        btnClose.Click += (_, _) => dlg.Close();
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
