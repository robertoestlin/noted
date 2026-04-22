using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Noted.Models;

namespace Noted;

public partial class MainWindow
{
    private void ShowTabCleanupDialog()
    {
        var dlg = new Window
        {
            Title = "Tab Cleanup",
            Width = 640,
            Height = 480,
            MinWidth = 420,
            MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel();

        scroll.Content = panel;

        var staleForeground = new SolidColorBrush(Color.FromRgb(168, 96, 102));
        staleForeground.Freeze();

        void RefreshList()
        {
            panel.Children.Clear();
            var threshold = TimeSpan.FromDays(_tabCleanupStaleDays);
            var now = DateTime.UtcNow;

            bool IsStale(TabDocument d) => (now - d.LastChangedUtc) > threshold;

            var stale = _docs.Where(kv => IsStale(kv.Value))
                .OrderBy(kv => kv.Value.LastChangedUtc)
                .ToList();
            var fresh = _docs.Where(kv => !IsStale(kv.Value))
                .OrderBy(kv => kv.Value.LastChangedUtc)
                .ToList();

            if (stale.Count == 0 && fresh.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "(No tabs)", Foreground = Brushes.Gray });
                return;
            }

            void AddRow(TabItem tab, TabDocument doc, bool isStaleRow)
            {
                var age = now - doc.LastChangedUtc;
                var ageDays = Math.Max(0, (int)Math.Floor(age.TotalDays));
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var fgName = isStaleRow ? staleForeground : Brushes.Black;
                var fgDate = isStaleRow ? staleForeground : Brushes.DimGray;

                var nameBlock = new TextBlock
                {
                    Text = doc.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    Foreground = fgName,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(nameBlock, 0);

                var daysBlock = new TextBlock
                {
                    Text = ageDays == 0
                        ? "Today"
                        : ageDays == 1
                            ? "1 day"
                            : $"{ageDays} days",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    Foreground = fgDate,
                    FontSize = 12
                };
                Grid.SetColumn(daysBlock, 1);

                var dateBlock = new TextBlock
                {
                    Text = $"{doc.LastChangedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Foreground = fgDate,
                    FontSize = 12
                };
                Grid.SetColumn(dateBlock, 2);

                var btnGoTo = new Button
                {
                    Content = "Go to",
                    Padding = new Thickness(10, 4, 10, 4),
                    MinWidth = 64,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                btnGoTo.Click += (_, _) =>
                {
                    MainTabControl.SelectedItem = tab;
                };
                Grid.SetColumn(btnGoTo, 3);

                var btnRemove = new Button
                {
                    Content = "Remove",
                    Padding = new Thickness(12, 4, 12, 4),
                    MinWidth = 76
                };
                btnRemove.Click += (_, _) =>
                {
                    if (CloseTab(tab))
                        RefreshList();
                };
                Grid.SetColumn(btnRemove, 4);

                row.Children.Add(nameBlock);
                row.Children.Add(daysBlock);
                row.Children.Add(dateBlock);
                row.Children.Add(btnGoTo);
                row.Children.Add(btnRemove);
                panel.Children.Add(row);
            }

            foreach (var kv in stale)
                AddRow(kv.Key, kv.Value, isStaleRow: true);

            if (stale.Count > 0 && fresh.Count > 0)
            {
                var ruler = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(208, 208, 212)),
                    Margin = new Thickness(0, 6, 0, 14)
                };
                panel.Children.Add(ruler);
            }

            foreach (var kv in fresh)
                AddRow(kv.Key, kv.Value, isStaleRow: false);
        }

        var staleSettingsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        staleSettingsPanel.Children.Add(new TextBlock
        {
            Text = _tabCleanupStaleDays == 1
                ? "Stale after: 1 day"
                : $"Stale after: {_tabCleanupStaleDays} days",
            Foreground = Brushes.DimGray
        });
        DockPanel.SetDock(staleSettingsPanel, Dock.Top);
        root.Children.Add(staleSettingsPanel);

        RefreshList();

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var btnClose = new Button { Content = "Close", Width = 88, IsDefault = true, IsCancel = true };
        btnClose.Click += (_, _) => dlg.Close();
        closeRow.Children.Add(btnClose);
        DockPanel.SetDock(closeRow, Dock.Bottom);

        root.Children.Add(closeRow);
        root.Children.Add(scroll);
        dlg.Content = root;
        dlg.ShowDialog();
    }
}
