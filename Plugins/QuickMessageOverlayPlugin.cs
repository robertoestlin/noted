using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted;

public partial class MainWindow
{
    private string _quickMessagePreset1 = "GG";
    private string _quickMessagePreset2 = "AFK";
    private string _quickMessagePreset3 = "BRB";
    private string _quickMessageCustom = string.Empty;
    private string _quickMessageColorName = "Ocean Blue";

    private static readonly (string Name, string Hex)[] QuickMessageColorOptions =
    [
        ("Ocean Blue", "#62E5FF"),
        ("Lime Punch", "#B9FF66"),
        ("Sunset Orange", "#FF9962"),
        ("Neon Pink", "#FF66CC"),
        ("Classic White", "#F7FAFF")
    ];

    private void ShowQuickMessageOverlayDialog()
    {
        var dlg = new Window
        {
            Title = "Quick Message Overlay",
            Width = 560,
            Height = 360,
            MinWidth = 460,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel
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
        btnClose.Click += (_, _) => dlg.Close();
        bottom.Children.Add(btnClose);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = "Set your messages and click a button to show it full-screen.",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.DimGray
        });

        static Grid CreatePresetRow(string labelText, string initialText, out TextBox textBox)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            textBox = new TextBox
            {
                Text = initialText,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(textBox, 1);
            row.Children.Add(textBox);
            return row;
        }

        panel.Children.Add(CreatePresetRow("Preset 1", _quickMessagePreset1, out var txtPreset1));
        panel.Children.Add(CreatePresetRow("Preset 2", _quickMessagePreset2, out var txtPreset2));
        panel.Children.Add(CreatePresetRow("Preset 3", _quickMessagePreset3, out var txtPreset3));
        panel.Children.Add(CreatePresetRow("Custom", _quickMessageCustom, out var txtCustom));

        var colorRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        colorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        colorRow.Children.Add(new TextBlock
        {
            Text = "Color",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        });
        var cmbColor = new ComboBox
        {
            Margin = new Thickness(8, 0, 0, 0)
        };
        foreach (var option in QuickMessageColorOptions)
            cmbColor.Items.Add(option.Name);
        cmbColor.SelectedItem = _quickMessageColorName;
        if (cmbColor.SelectedIndex < 0)
            cmbColor.SelectedIndex = 0;
        Grid.SetColumn(cmbColor, 1);
        colorRow.Children.Add(cmbColor);
        panel.Children.Add(colorRow);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        var btnShow1 = new Button
        {
            Content = "Show Preset 1",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnShow2 = new Button
        {
            Content = "Show Preset 2",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnShow3 = new Button
        {
            Content = "Show Preset 3",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnShowCustom = new Button
        {
            Content = "Show Custom",
            Padding = new Thickness(12, 6, 12, 6)
        };
        actionRow.Children.Add(btnShow1);
        actionRow.Children.Add(btnShow2);
        actionRow.Children.Add(btnShow3);
        actionRow.Children.Add(btnShowCustom);
        panel.Children.Add(actionRow);

        root.Children.Add(panel);

        void SaveValues()
        {
            _quickMessagePreset1 = txtPreset1.Text.Trim();
            _quickMessagePreset2 = txtPreset2.Text.Trim();
            _quickMessagePreset3 = txtPreset3.Text.Trim();
            _quickMessageCustom = txtCustom.Text.Trim();
            _quickMessageColorName = cmbColor.SelectedItem as string ?? QuickMessageColorOptions[0].Name;
        }

        Brush ResolveOverlayBrush()
        {
            foreach (var option in QuickMessageColorOptions)
            {
                if (string.Equals(option.Name, _quickMessageColorName, StringComparison.Ordinal))
                    return (Brush)new BrushConverter().ConvertFromString(option.Hex)!;
            }

            return (Brush)new BrushConverter().ConvertFromString(QuickMessageColorOptions[0].Hex)!;
        }

        void ShowAndClose(string text)
        {
            SaveValues();
            var message = string.IsNullOrWhiteSpace(text) ? "..." : text.Trim();
            ShowQuickMessageOverlay(message, ResolveOverlayBrush());
            dlg.Close();
        }

        btnShow1.Click += (_, _) => ShowAndClose(txtPreset1.Text);
        btnShow2.Click += (_, _) => ShowAndClose(txtPreset2.Text);
        btnShow3.Click += (_, _) => ShowAndClose(txtPreset3.Text);
        btnShowCustom.Click += (_, _) => ShowAndClose(txtCustom.Text);

        dlg.Content = root;
        dlg.ShowDialog();
    }

    private void ShowQuickMessageOverlay(string text, Brush foreground)
    {
        MessageOverlayText.Text = text;
        MessageOverlayText.Foreground = foreground;
        MessageOverlay.Visibility = Visibility.Visible;
        MessageOverlay.Focus();
        Keyboard.Focus(MessageOverlay);
    }

    private void HideQuickMessageOverlay()
    {
        MessageOverlay.Visibility = Visibility.Collapsed;
    }

    private void MessageOverlay_DismissByMouseDown(object sender, MouseButtonEventArgs e)
    {
        HideQuickMessageOverlay();
        e.Handled = true;
    }

    private void MessageOverlay_DismissByKeyDown(object sender, KeyEventArgs e)
    {
        HideQuickMessageOverlay();
        e.Handled = true;
    }
}
