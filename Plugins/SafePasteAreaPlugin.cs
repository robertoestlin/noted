using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private const string SafePasteDataFileName = "safe-paste.dat";
    private readonly List<SafePasteSavedEntry> _safePasteSavedEntries = [];
    private readonly List<SafePasteKeyRecord> _safePasteKeyRecords = [];

    private sealed class SafePasteSavedEntry
    {
        public string Identifier { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
    }

    private sealed class SafePasteEncryptedEntry
    {
        public string Identifier { get; set; } = string.Empty;
        public string TextCipher { get; set; } = string.Empty;
        public string TextNonce { get; set; } = string.Empty;
        public string TextTag { get; set; } = string.Empty;
        public string SecretCipher { get; set; } = string.Empty;
        public string SecretNonce { get; set; } = string.Empty;
        public string SecretTag { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
    }

    private sealed class SafePasteLegacyEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        public DateTime SavedAtUtc { get; set; }
    }

    private List<SafePasteKeyRecord> BuildSafePasteKeyRecordsSnapshot()
        => _safePasteKeyRecords
            .Where(record =>
                !string.IsNullOrWhiteSpace(record.Identifier) &&
                !string.IsNullOrWhiteSpace(record.Key))
            .Select(record => new SafePasteKeyRecord
            {
                Identifier = record.Identifier,
                Key = record.Key
            })
            .ToList();

    private static string GenerateSafePasteKey()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string GenerateSafePasteIdentifier()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private bool TryGetKeyBytesForIdentifier(string identifier, out byte[] keyBytes)
    {
        keyBytes = [];
        var keyHex = _safePasteKeyRecords
            .FirstOrDefault(record => string.Equals(record.Identifier, identifier, StringComparison.Ordinal))
            ?.Key;
        if (string.IsNullOrWhiteSpace(keyHex))
            return false;

        try
        {
            keyBytes = Convert.FromHexString(keyHex);
            return keyBytes.Length == 32;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEncryptWithKey(string plainText, byte[] keyBytes, out string cipher, out string nonce, out string tag)
    {
        cipher = string.Empty;
        nonce = string.Empty;
        tag = string.Empty;
        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            var nonceBytes = RandomNumberGenerator.GetBytes(12);
            var cipherBytes = new byte[plaintextBytes.Length];
            var tagBytes = new byte[16];
            using var aes = new AesGcm(keyBytes, 16);
            aes.Encrypt(nonceBytes, plaintextBytes, cipherBytes, tagBytes);
            cipher = Convert.ToBase64String(cipherBytes);
            nonce = Convert.ToBase64String(nonceBytes);
            tag = Convert.ToBase64String(tagBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDecryptWithKey(string cipher, string nonce, string tag, byte[] keyBytes, out string plainText)
    {
        plainText = string.Empty;
        try
        {
            var cipherBytes = Convert.FromBase64String(cipher ?? string.Empty);
            var nonceBytes = Convert.FromBase64String(nonce ?? string.Empty);
            var tagBytes = Convert.FromBase64String(tag ?? string.Empty);
            var plaintextBytes = new byte[cipherBytes.Length];
            using var aes = new AesGcm(keyBytes, 16);
            aes.Decrypt(nonceBytes, cipherBytes, tagBytes, plaintextBytes);
            plainText = Encoding.UTF8.GetString(plaintextBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveSafePasteData(JsonSerializerOptions options)
    {
        try
        {
            var encryptedEntries = new List<SafePasteEncryptedEntry>();
            foreach (var entry in _safePasteSavedEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.Identifier))
                    continue;
                if (!TryGetKeyBytesForIdentifier(entry.Identifier, out var keyBytes))
                    continue;
                if (!TryEncryptWithKey(entry.Text, keyBytes, out var textCipher, out var textNonce, out var textTag))
                    continue;
                if (!TryEncryptWithKey(entry.Secret, keyBytes, out var secretCipher, out var secretNonce, out var secretTag))
                    continue;

                encryptedEntries.Add(new SafePasteEncryptedEntry
                {
                    Identifier = entry.Identifier,
                    TextCipher = textCipher,
                    TextNonce = textNonce,
                    TextTag = textTag,
                    SecretCipher = secretCipher,
                    SecretNonce = secretNonce,
                    SecretTag = secretTag,
                    SavedAtUtc = entry.SavedAtUtc
                });
            }

            var path = Path.Combine(_backupFolder, SafePasteDataFileName);
            _windowSettingsStore.Save(path, encryptedEntries, options);
        }
        catch
        {
            // Non-critical persistence.
        }
    }

    private void LoadSafePasteData(IEnumerable<SafePasteKeyRecord>? safePasteKeyRecords, IEnumerable<string>? legacySafePasteKeys)
    {
        _safePasteSavedEntries.Clear();
        _safePasteKeyRecords.Clear();
        try
        {
            var seenIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in safePasteKeyRecords ?? [])
            {
                var identifier = (record.Identifier ?? string.Empty).Trim();
                var key = (record.Key ?? string.Empty).Trim();
                if (identifier.Length == 0 || key.Length == 0 || !seenIdentifiers.Add(identifier))
                    continue;
                _safePasteKeyRecords.Add(new SafePasteKeyRecord
                {
                    Identifier = identifier,
                    Key = key
                });
            }

            // Legacy migration: old settings kept only keys, no identifiers.
            foreach (var legacyKey in legacySafePasteKeys ?? [])
            {
                var key = (legacyKey ?? string.Empty).Trim();
                if (key.Length == 0 || _safePasteKeyRecords.Any(record => string.Equals(record.Identifier, key, StringComparison.Ordinal)))
                    continue;
                _safePasteKeyRecords.Add(new SafePasteKeyRecord
                {
                    Identifier = key,
                    Key = key
                });
            }

            var path = Path.Combine(_backupFolder, SafePasteDataFileName);
            var loaded = _windowSettingsStore.Load<List<SafePasteEncryptedEntry>>(path);
            if (loaded != null && loaded.Count > 0)
            {
                foreach (var entry in loaded)
                {
                    var identifier = (entry.Identifier ?? string.Empty).Trim();
                    if (identifier.Length == 0 || !TryGetKeyBytesForIdentifier(identifier, out var keyBytes))
                        continue;
                    if (!TryDecryptWithKey(entry.TextCipher, entry.TextNonce, entry.TextTag, keyBytes, out var text))
                        continue;
                    if (!TryDecryptWithKey(entry.SecretCipher, entry.SecretNonce, entry.SecretTag, keyBytes, out var secret))
                        continue;

                    _safePasteSavedEntries.Add(new SafePasteSavedEntry
                    {
                        Identifier = identifier,
                        Text = text,
                        Secret = secret,
                        SavedAtUtc = entry.SavedAtUtc == default ? DateTime.UtcNow : entry.SavedAtUtc
                    });
                }
                return;
            }

            // Legacy migration: old .dat had plaintext Key/Text/Secret.
            var legacy = _windowSettingsStore.Load<List<SafePasteLegacyEntry>>(path);
            if (legacy == null || legacy.Count == 0)
                return;

            foreach (var entry in legacy)
            {
                var legacyIdentifier = (entry.Key ?? string.Empty).Trim();
                if (legacyIdentifier.Length == 0)
                    continue;
                if (_safePasteKeyRecords.All(record => !string.Equals(record.Identifier, legacyIdentifier, StringComparison.Ordinal)))
                {
                    _safePasteKeyRecords.Add(new SafePasteKeyRecord
                    {
                        Identifier = legacyIdentifier,
                        Key = legacyIdentifier
                    });
                }

                _safePasteSavedEntries.Add(new SafePasteSavedEntry
                {
                    Identifier = legacyIdentifier,
                    Text = entry.Text ?? string.Empty,
                    Secret = entry.Secret ?? string.Empty,
                    SavedAtUtc = entry.SavedAtUtc == default ? DateTime.UtcNow : entry.SavedAtUtc
                });
            }
        }
        catch
        {
            // Non-critical persistence.
        }
    }

    private void ShowSafePasteAreaDialog()
    {
        var dlg = new Window
        {
            Title = "Safe Paste Area",
            Width = 960,
            Height = 700,
            MinWidth = 760,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var plainValue = string.Empty;
        var secretValue = string.Empty;
        var clipboardCopyVersion = 0;
        var plainVisible = true;
        var secretVisible = false;
        var suppressPlainTextSync = false;
        var savedSecrets = _safePasteSavedEntries;

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

        var centerPanel = new StackPanel();

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
        secretHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        secretHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        secretHeader.Children.Add(new TextBlock
        {
            Text = "Secret Paste Area (masked)",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var btnOpenContainsCheck = new Button
        {
            Content = "⊂",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            ToolTip = "Open contains check"
        };
        Grid.SetColumn(btnOpenContainsCheck, 1);
        secretHeader.Children.Add(btnOpenContainsCheck);
        var btnOpenSecretCompare = new Button
        {
            Content = "=",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            ToolTip = "Open secret comparison"
        };
        Grid.SetColumn(btnOpenSecretCompare, 2);
        secretHeader.Children.Add(btnOpenSecretCompare);
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
        Grid.SetColumn(btnToggleSecretVisible, 3);
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

        centerPanel.Children.Add(body);

        var savedSection = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        savedSection.Children.Add(new TextBlock
        {
            Text = "Saved Secrets",
            FontWeight = FontWeights.SemiBold
        });
        savedSection.Children.Add(new TextBlock
        {
            Text = "Save creates a new key for the current text + secret snapshot.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 6)
        });
        var saveButtonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var btnSaveSecrets = new Button
        {
            Content = "Save (generate key)",
            Padding = new Thickness(14, 6, 14, 6)
        };
        var btnRemoveSavedSecret = new Button
        {
            Content = "Remove selected save",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        saveButtonsRow.Children.Add(btnSaveSecrets);
        saveButtonsRow.Children.Add(btnRemoveSavedSecret);
        savedSection.Children.Add(saveButtonsRow);
        var lstSavedSecrets = new ListBox
        {
            Height = 110,
            MinHeight = 90,
            MaxHeight = 140
        };
        savedSection.Children.Add(lstSavedSecrets);
        centerPanel.Children.Add(savedSection);
        root.Children.Add(centerPanel);

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

        void ShowContainsCheckDialog()
        {
            var containsWindow = new Window
            {
                Title = "Contains check",
                Width = 460,
                Height = 240,
                MinWidth = 430,
                MinHeight = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg
            };

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Type text to check if it exists in the secret area.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var txtContainsNeedle = new TextBox
            {
                AcceptsReturn = false,
                MinHeight = 28,
                FontFamily = new FontFamily("Consolas, Courier New")
            };
            panel.Children.Add(txtContainsNeedle);

            panel.Children.Add(new TextBlock
            {
                Text = $"Secret length: {secretValue.Length} characters",
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 6, 0, 0)
            });

            var resultRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var txtContainsResultIcon = new TextBlock
            {
                Text = "•",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DimGray,
                Width = 26,
                VerticalAlignment = VerticalAlignment.Center
            };
            var txtContainsResult = new TextBlock
            {
                Text = "Enter text to check.",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            resultRow.Children.Add(txtContainsResultIcon);
            resultRow.Children.Add(txtContainsResult);
            panel.Children.Add(resultRow);

            var closeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var btnCloseContainsWindow = new Button
            {
                Content = "Close",
                IsCancel = true,
                Width = 90
            };
            closeRow.Children.Add(btnCloseContainsWindow);
            panel.Children.Add(closeRow);

            void UpdateContainsResult()
            {
                var valueToCheck = txtContainsNeedle.Text ?? string.Empty;
                if (secretValue.Length == 0)
                {
                    txtContainsResultIcon.Text = "•";
                    txtContainsResultIcon.Foreground = Brushes.DimGray;
                    txtContainsResult.Text = "Paste a secret first.";
                    return;
                }

                if (valueToCheck.Length == 0)
                {
                    txtContainsResultIcon.Text = "•";
                    txtContainsResultIcon.Foreground = Brushes.DimGray;
                    txtContainsResult.Text = "Enter text to check.";
                    return;
                }

                var contains = secretValue.Contains(valueToCheck, StringComparison.Ordinal);
                txtContainsResultIcon.Text = contains ? "✔" : "✖";
                txtContainsResultIcon.Foreground = contains ? Brushes.ForestGreen : Brushes.IndianRed;
                txtContainsResult.Text = contains
                    ? "The text is contained in the secret."
                    : "The text is not contained in the secret.";
            }

            txtContainsNeedle.TextChanged += (_, _) => UpdateContainsResult();
            btnCloseContainsWindow.Click += (_, _) => containsWindow.Close();
            containsWindow.Loaded += (_, _) => txtContainsNeedle.Focus();
            containsWindow.Content = panel;
            UpdateContainsResult();
            containsWindow.ShowDialog();
        }

        void ShowSecretComparisonDialog()
        {
            var compareWindow = new Window
            {
                Title = "Secret comparison",
                Width = 460,
                Height = 300,
                MinWidth = 430,
                MinHeight = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg
            };

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = "Paste two secrets to check if they match.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Secret 1:",
                Margin = new Thickness(0, 0, 0, 2)
            });
            var txtCompareA = new PasswordBox
            {
                MinHeight = 28,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(txtCompareA);

            panel.Children.Add(new TextBlock
            {
                Text = "Secret 2:",
                Margin = new Thickness(0, 0, 0, 2)
            });
            var txtCompareB = new PasswordBox
            {
                MinHeight = 28,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(txtCompareB);

            var compareResultRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var txtCompareResultIcon = new TextBlock
            {
                Text = "•",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DimGray,
                Width = 26,
                VerticalAlignment = VerticalAlignment.Center
            };
            var txtCompareResult = new TextBlock
            {
                Text = "Enter secrets to compare.",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            compareResultRow.Children.Add(txtCompareResultIcon);
            compareResultRow.Children.Add(txtCompareResult);
            panel.Children.Add(compareResultRow);

            var compareCloseRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var btnCloseCompareWindow = new Button
            {
                Content = "Close",
                IsCancel = true,
                Width = 90
            };
            compareCloseRow.Children.Add(btnCloseCompareWindow);
            panel.Children.Add(compareCloseRow);

            void UpdateCompareResult()
            {
                var a = txtCompareA.Password ?? string.Empty;
                var b = txtCompareB.Password ?? string.Empty;

                if (a.Length == 0 && b.Length == 0)
                {
                    txtCompareResultIcon.Text = "•";
                    txtCompareResultIcon.Foreground = Brushes.DimGray;
                    txtCompareResult.Text = "Enter secrets to compare.";
                    return;
                }

                if (a.Length == 0 || b.Length == 0)
                {
                    txtCompareResultIcon.Text = "•";
                    txtCompareResultIcon.Foreground = Brushes.DimGray;
                    txtCompareResult.Text = "Enter both secrets to compare.";
                    return;
                }

                var match = string.Equals(a, b, StringComparison.Ordinal);
                txtCompareResultIcon.Text = match ? "✔" : "✖";
                txtCompareResultIcon.Foreground = match ? Brushes.ForestGreen : Brushes.IndianRed;
                txtCompareResult.Text = match
                    ? "The secrets match."
                    : "The secrets do not match.";
            }

            txtCompareA.PasswordChanged += (_, _) => UpdateCompareResult();
            txtCompareB.PasswordChanged += (_, _) => UpdateCompareResult();
            btnCloseCompareWindow.Click += (_, _) => compareWindow.Close();
            compareWindow.Loaded += (_, _) => txtCompareA.Focus();
            compareWindow.Content = panel;
            UpdateCompareResult();
            compareWindow.ShowDialog();
        }

        void RefreshSavedSecretsList()
        {
            lstSavedSecrets.Items.Clear();
            for (var i = 0; i < savedSecrets.Count; i++)
            {
                var item = savedSecrets[i];
                var localSavedAt = item.SavedAtUtc.ToLocalTime();
                lstSavedSecrets.Items.Add(
                    $"{i + 1}. Id: {item.Identifier} | text: {item.Text.Length} | secret: {item.Secret.Length} | saved: {localSavedAt:yyyy-MM-dd HH:mm:ss}");
            }

            btnRemoveSavedSecret.IsEnabled = lstSavedSecrets.SelectedIndex >= 0;
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
        btnOpenContainsCheck.Click += (_, _) => ShowContainsCheckDialog();
        btnOpenSecretCompare.Click += (_, _) => ShowSecretComparisonDialog();

        lstSavedSecrets.SelectionChanged += (_, _) =>
        {
            var selectedIndex = lstSavedSecrets.SelectedIndex;
            btnRemoveSavedSecret.IsEnabled = selectedIndex >= 0;
            if (selectedIndex < 0 || selectedIndex >= savedSecrets.Count)
                return;

            var selected = savedSecrets[selectedIndex];
            plainValue = selected.Text ?? string.Empty;
            secretValue = selected.Secret ?? string.Empty;
            ApplyPlainVisibility();
            UpdatePlainIndicators();
            ApplySecretVisibility();
            UpdateSecretIndicators();
            SetStatus($"Loaded saved entry id: {selected.Identifier}");
        };

        btnSaveSecrets.Click += (_, _) =>
        {
            if (plainValue.Length == 0 && secretValue.Length == 0)
            {
                SetStatus("Nothing to save. Enter text or secret first.", Brushes.IndianRed);
                return;
            }

            var existingIdentifiers = new HashSet<string>(_safePasteKeyRecords.Select(record => record.Identifier), StringComparer.Ordinal);
            var generatedIdentifier = GenerateSafePasteIdentifier();
            while (existingIdentifiers.Contains(generatedIdentifier))
                generatedIdentifier = GenerateSafePasteIdentifier();

            var generatedKey = GenerateSafePasteKey();
            _safePasteKeyRecords.Add(new SafePasteKeyRecord
            {
                Identifier = generatedIdentifier,
                Key = generatedKey
            });

            savedSecrets.Add(new SafePasteSavedEntry
            {
                Identifier = generatedIdentifier,
                Text = plainValue,
                Secret = secretValue,
                SavedAtUtc = DateTime.UtcNow
            });
            RefreshSavedSecretsList();
            lstSavedSecrets.SelectedIndex = savedSecrets.Count - 1;
            SaveWindowSettings();
            SetStatus($"Saved text+secret. Generated identifier: {generatedIdentifier}");
        };

        btnRemoveSavedSecret.Click += (_, _) =>
        {
            var selectedIndex = lstSavedSecrets.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= savedSecrets.Count)
                return;

            var removedIdentifier = savedSecrets[selectedIndex].Identifier;
            savedSecrets.RemoveAt(selectedIndex);
            _safePasteKeyRecords.RemoveAll(record => string.Equals(record.Identifier, removedIdentifier, StringComparison.Ordinal));
            RefreshSavedSecretsList();
            if (savedSecrets.Count > 0)
                lstSavedSecrets.SelectedIndex = Math.Min(selectedIndex, savedSecrets.Count - 1);
            SaveWindowSettings();
            SetStatus($"Removed saved entry and key record: {removedIdentifier}");
        };

        ApplyPlainVisibility();
        UpdatePlainIndicators();
        ApplySecretVisibility();
        UpdateSecretIndicators();
        RefreshSavedSecretsList();

        btnClose.Click += (_, _) => dlg.Close();
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
