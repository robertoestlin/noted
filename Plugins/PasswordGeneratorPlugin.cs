using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private static string GenerateSecurePassword(int length, bool useUpper, bool useLower, bool useDigit, bool useSymbol)
    {
        var pools = new List<char[]>();
        if (useUpper)
            pools.Add("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        if (useLower)
            pools.Add("abcdefghijklmnopqrstuvwxyz".ToCharArray());
        if (useDigit)
            pools.Add("0123456789".ToCharArray());
        if (useSymbol)
            pools.Add("!@#$%^&*()-_=+[]{}|;:,.?/".ToCharArray());

        if (pools.Count == 0)
            throw new InvalidOperationException("Select at least one character set.");

        var allowed = new List<char>();
        foreach (var p in pools)
            allowed.AddRange(p);

        var result = new char[length];
        if (length >= pools.Count)
        {
            var poolOrder = Enumerable.Range(0, pools.Count).ToArray();
            Shuffle(poolOrder);
            for (var i = 0; i < pools.Count; i++)
            {
                var p = pools[poolOrder[i]];
                result[i] = p[RandomNumberGenerator.GetInt32(0, p.Length)];
            }

            for (var i = pools.Count; i < length; i++)
                result[i] = allowed[RandomNumberGenerator.GetInt32(0, allowed.Count)];

            Shuffle(result.AsSpan());
        }
        else
        {
            for (var i = 0; i < length; i++)
                result[i] = allowed[RandomNumberGenerator.GetInt32(0, allowed.Count)];
        }

        return new string(result);
    }

    private static void Shuffle(Span<int> span)
    {
        for (var i = span.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(0, i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }

    private static void Shuffle(Span<char> span)
    {
        for (var i = span.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(0, i + 1);
            (span[i], span[j]) = (span[j], span[i]);
        }
    }

    private static (int Score, string Label) EstimatePasswordStrength(string password, int poolSize, int setCount)
    {
        if (string.IsNullOrEmpty(password) || poolSize < 2)
            return (0, "");

        var bits = password.Length * Math.Log2(poolSize);
        var varietyBonus = setCount >= 3 ? 8 : setCount == 2 ? 0 : -4;
        var score = (int)Math.Clamp((bits + varietyBonus) * 1.2, 0, 100);

        var label = score < 35 ? "Weak" : score < 60 ? "Fair" : score < 80 ? "Strong" : "Very strong";
        return (score, label);
    }

    private void ShowPasswordGeneratorDialog()
    {
        var dlg = new Window
        {
            Title = "Password Generator",
            Width = 480,
            Height = 470,
            MinWidth = 380,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        string? currentPassword = null;
        string? currentPart1 = null;
        string? currentPart2 = null;
        const int TwoPartPasswordLength = 40;

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
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

        var main = new StackPanel { Orientation = Orientation.Vertical };

        var lblPassword = new TextBlock
        {
            Text = "Password",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        main.Children.Add(lblPassword);

        var pwdContainer = new Grid();
        pwdContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pwdContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pwdBox = new PasswordBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 28,
            Focusable = false,
            Visibility = Visibility.Visible
        };
        Grid.SetColumn(pwdBox, 0);
        pwdContainer.Children.Add(pwdBox);

        var txtPlain = new TextBox
        {
            FontFamily = new FontFamily("Consolas, Courier New"),
            MinHeight = 28,
            IsReadOnly = true,
            Visibility = Visibility.Collapsed,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(txtPlain, 0);
        pwdContainer.Children.Add(txtPlain);

        var chkShow = new CheckBox
        {
            Content = "Show password",
            Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        main.Children.Add(pwdContainer);
        main.Children.Add(chkShow);

        var strengthRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var strengthBar = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            MinWidth = 200
        };
        var strengthLabel = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray
        };
        strengthRow.Children.Add(strengthBar);
        strengthRow.Children.Add(strengthLabel);
        main.Children.Add(strengthRow);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var btnGenerate = new Button
        {
            Content = "Generate",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnCopy = new Button
        {
            Content = "Copy password",
            Padding = new Thickness(16, 6, 16, 6),
            IsEnabled = false
        };
        btnRow.Children.Add(btnGenerate);
        btnRow.Children.Add(btnCopy);
        main.Children.Add(btnRow);

        var btnRowTwoPart = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnGenerateTwoPart = new Button
        {
            Content = "Generate two-part (40)",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnCopyPart1 = new Button
        {
            Content = "Copy part 1",
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            IsEnabled = false
        };
        var btnCopyPart2 = new Button
        {
            Content = "Copy part 2",
            Padding = new Thickness(16, 6, 16, 6),
            IsEnabled = false
        };
        btnRowTwoPart.Children.Add(btnGenerateTwoPart);
        btnRowTwoPart.Children.Add(btnCopyPart1);
        btnRowTwoPart.Children.Add(btnCopyPart2);
        main.Children.Add(btnRowTwoPart);

        var lblLength = new TextBlock
        {
            Text = "Password length",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 4)
        };
        main.Children.Add(lblLength);

        var lengthRow = new Grid();
        lengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sliderLength = new Slider
        {
            Minimum = 4,
            Maximum = 128,
            Value = 20,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var txtLengthValue = new TextBox
        {
            Text = "20",
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 40,
            MaxWidth = 52,
            MaxLength = 3,
            TextAlignment = TextAlignment.Right
        };

        void CommitLengthField()
        {
            var s = txtLengthValue.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(s) ||
                !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                txtLengthValue.Text = ((int)sliderLength.Value).ToString(CultureInfo.InvariantCulture);
                return;
            }

            sliderLength.Value = Math.Clamp(v, (int)sliderLength.Minimum, (int)sliderLength.Maximum);
            txtLengthValue.Text = ((int)sliderLength.Value).ToString(CultureInfo.InvariantCulture);
        }

        Grid.SetColumn(sliderLength, 0);
        Grid.SetColumn(txtLengthValue, 1);
        lengthRow.Children.Add(sliderLength);
        lengthRow.Children.Add(txtLengthValue);
        main.Children.Add(lengthRow);

        sliderLength.ValueChanged += (_, _) =>
        {
            txtLengthValue.Text = ((int)sliderLength.Value).ToString(CultureInfo.InvariantCulture);
        };

        txtLengthValue.LostFocus += (_, _) => CommitLengthField();
        txtLengthValue.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitLengthField();
                e.Handled = true;
            }
        };

        var lblChars = new TextBlock
        {
            Text = "Characters used",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        };
        main.Children.Add(lblChars);

        var chkUpper = new CheckBox { Content = "Uppercase", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        var chkLower = new CheckBox { Content = "Lowercase", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        var chkNumber = new CheckBox { Content = "Numbers", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        var chkSymbol = new CheckBox { Content = "Symbols", IsChecked = true };
        var charRow = new StackPanel { Orientation = Orientation.Horizontal };
        charRow.Children.Add(chkUpper);
        charRow.Children.Add(chkLower);
        charRow.Children.Add(chkNumber);
        charRow.Children.Add(chkSymbol);
        main.Children.Add(charRow);

        void ApplyShowPassword()
        {
            var show = chkShow.IsChecked == true;
            if (show)
            {
                txtPlain.Text = currentPassword ?? "";
                txtPlain.Visibility = Visibility.Visible;
                pwdBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                pwdBox.Password = currentPassword ?? "";
                pwdBox.Visibility = Visibility.Visible;
                txtPlain.Visibility = Visibility.Collapsed;
            }
        }

        void UpdateStrengthDisplay()
        {
            if (string.IsNullOrEmpty(currentPassword))
            {
                strengthBar.Value = 0;
                strengthLabel.Text = "";
                return;
            }

            var poolSize = 0;
            var setCount = 0;
            if (chkUpper.IsChecked == true)
            {
                poolSize += 26;
                setCount++;
            }

            if (chkLower.IsChecked == true)
            {
                poolSize += 26;
                setCount++;
            }

            if (chkNumber.IsChecked == true)
            {
                poolSize += 10;
                setCount++;
            }

            if (chkSymbol.IsChecked == true)
            {
                poolSize += "!@#$%^&*()-_=+[]{}|;:,.?/".Length;
                setCount++;
            }

            var (score, label) = EstimatePasswordStrength(currentPassword, poolSize, setCount);
            strengthBar.Value = score;
            strengthLabel.Text = label;
        }

        void ClearTwoPartState()
        {
            currentPart1 = null;
            currentPart2 = null;
            btnCopyPart1.IsEnabled = false;
            btnCopyPart2.IsEnabled = false;
        }

        void RunGenerate()
        {
            CommitLengthField();

            if (chkUpper.IsChecked != true && chkLower.IsChecked != true &&
                chkNumber.IsChecked != true && chkSymbol.IsChecked != true)
            {
                SetStatus("Select at least one character set.", Brushes.IndianRed);
                return;
            }

            var len = (int)sliderLength.Value;
            try
            {
                currentPassword = GenerateSecurePassword(
                    len,
                    chkUpper.IsChecked == true,
                    chkLower.IsChecked == true,
                    chkNumber.IsChecked == true,
                    chkSymbol.IsChecked == true);

                ClearTwoPartState();
                chkShow.IsChecked = false;
                pwdBox.Password = currentPassword;
                txtPlain.Text = currentPassword;
                ApplyShowPassword();
                btnCopy.IsEnabled = true;
                UpdateStrengthDisplay();
                SetStatus("Password generated.");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, Brushes.IndianRed);
            }
        }

        void RunGenerateTwoPart()
        {
            if (chkUpper.IsChecked != true && chkLower.IsChecked != true &&
                chkNumber.IsChecked != true && chkSymbol.IsChecked != true)
            {
                SetStatus("Select at least one character set.", Brushes.IndianRed);
                return;
            }

            sliderLength.Value = TwoPartPasswordLength;
            txtLengthValue.Text = TwoPartPasswordLength.ToString(CultureInfo.InvariantCulture);

            try
            {
                currentPassword = GenerateSecurePassword(
                    TwoPartPasswordLength,
                    chkUpper.IsChecked == true,
                    chkLower.IsChecked == true,
                    chkNumber.IsChecked == true,
                    chkSymbol.IsChecked == true);

                var half = TwoPartPasswordLength / 2;
                currentPart1 = currentPassword.Substring(0, half);
                currentPart2 = currentPassword.Substring(half);

                chkShow.IsChecked = false;
                pwdBox.Password = currentPassword;
                txtPlain.Text = currentPassword;
                ApplyShowPassword();
                btnCopy.IsEnabled = true;
                btnCopyPart1.IsEnabled = true;
                btnCopyPart2.IsEnabled = true;
                UpdateStrengthDisplay();
                SetStatus("Two-part password generated (length 40, 20 + 20).");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, Brushes.IndianRed);
            }
        }

        chkShow.Checked += (_, _) => ApplyShowPassword();
        chkShow.Unchecked += (_, _) => ApplyShowPassword();

        btnGenerate.Click += (_, _) => RunGenerate();
        btnGenerateTwoPart.Click += (_, _) => RunGenerateTwoPart();

        btnCopy.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(currentPassword))
                return;
            try
            {
                Clipboard.SetText(currentPassword);
                SetStatus("Copied to clipboard.");
            }
            catch (Exception ex)
            {
                SetStatus($"Copy failed: {ex.Message}", Brushes.IndianRed);
            }
        };

        btnCopyPart1.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(currentPart1))
                return;
            try
            {
                Clipboard.SetText(currentPart1);
                SetStatus("Part 1 copied to clipboard.");
            }
            catch (Exception ex)
            {
                SetStatus($"Copy failed: {ex.Message}", Brushes.IndianRed);
            }
        };

        btnCopyPart2.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(currentPart2))
                return;
            try
            {
                Clipboard.SetText(currentPart2);
                SetStatus("Part 2 copied to clipboard.");
            }
            catch (Exception ex)
            {
                SetStatus($"Copy failed: {ex.Message}", Brushes.IndianRed);
            }
        };

        btnClose.Click += (_, _) => dlg.Close();

        root.Children.Add(main);
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
