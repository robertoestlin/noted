using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noted;

public partial class MainWindow
{
    private void ShowImageToColorDialog()
    {
        var dlg = new ImageToColorWindow { Owner = this };
        dlg.Show();
    }
}

internal sealed class ImageToColorWindow : Window
{
    private readonly Image _image;
    private readonly TextBlock _hint;
    private readonly TextBox _hex;
    private readonly Slider _r;
    private readonly Slider _g;
    private readonly Slider _b;
    private readonly Border _swatch;
    private BitmapSource? _sampleSource;
    private bool _suppress;

    public ImageToColorWindow()
    {
        Title = "Image to color";
        Width = 720;
        Height = 640;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var colorPanel = new StackPanel();

        var swatchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _swatch = new Border
        {
            Width = 44,
            Height = 44,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatchRow.Children.Add(_swatch);

        var hexCol = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        hexCol.Children.Add(new TextBlock { Text = "Hex", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, FontSize = 11 });
        _hex = new TextBox { MinWidth = 120, MaxWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        _hex.TextChanged += (_, _) => OnHexTextChanged();
        hexCol.Children.Add(_hex);
        swatchRow.Children.Add(hexCol);

        colorPanel.Children.Add(swatchRow);

        _r = MakeRgbRow("R", colorPanel);
        _g = MakeRgbRow("G", colorPanel);
        _b = MakeRgbRow("B", colorPanel);
        _r.ValueChanged += (_, _) => OnRgbSliderChanged();
        _g.ValueChanged += (_, _) => OnRgbSliderChanged();
        _b.ValueChanged += (_, _) => OnRgbSliderChanged();

        var btnPicker = new Button
        {
            Content = "Graphical color picker…",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Same picker as in the Drawing plugin",
        };
        btnPicker.Click += (_, _) => OpenGraphicalPicker();
        colorPanel.Children.Add(btnPicker);

        bottom.Children.Add(colorPanel);

        var closeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnClose = new Button { Content = "Close", Width = 90, IsCancel = true, IsDefault = true };
        btnClose.Click += (_, _) => Close();
        closeRow.Children.Add(btnClose);
        bottom.Children.Add(closeRow);

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(top, Dock.Top);

        _hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "Paste a screenshot (Ctrl+V or Paste), then click the image to pick a pixel. Adjust with RGB sliders or the graphical color picker.",
        };
        top.Children.Add(_hint);

        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        var btnPaste = new Button { Content = "Paste image", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        btnPaste.Click += (_, _) => TryPasteFromClipboard();
        topRow.Children.Add(btnPaste);
        top.Children.Add(topRow);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.WhiteSmoke,
        };
        _image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _image.MouseLeftButtonDown += OnImageMouseLeftButtonDown;
        scroll.Content = _image;

        root.Children.Add(bottom);
        root.Children.Add(top);
        root.Children.Add(scroll);

        Content = root;

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => TryPasteFromClipboard(), OnPasteCanExecute));
        Activated += (_, _) => CommandManager.InvalidateRequerySuggested();

        SetUiColor(Color.FromRgb(0x21, 0x96, 0xF3));
    }

    private void OnPasteCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ClipboardContainsBitmap();

    private static bool ClipboardContainsBitmap()
    {
        try
        {
            return Clipboard.ContainsImage();
        }
        catch
        {
            return false;
        }
    }

    private static Slider MakeRgbRow(string label, Panel parent)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(lbl, 0);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(slider, 1);

        var valueTb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(valueTb, 2);

        void UpdateLabel(object? _, RoutedPropertyChangedEventArgs<double> e) => valueTb.Text = ((int)Math.Round(e.NewValue)).ToString();
        slider.ValueChanged += UpdateLabel;
        slider.Loaded += (_, _) => valueTb.Text = ((int)Math.Round(slider.Value)).ToString();

        grid.Children.Add(lbl);
        grid.Children.Add(slider);
        grid.Children.Add(valueTb);
        parent.Children.Add(grid);
        return slider;
    }

    private void TryPasteFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                MessageBox.Show(this, "The clipboard does not contain an image.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var src = Clipboard.GetImage();
            if (src == null)
            {
                MessageBox.Show(this, "Could not read an image from the clipboard.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetImageSource(src);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetImageSource(BitmapSource src)
    {
        var converted = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        try
        {
            if (!converted.IsFrozen)
                converted.Freeze();
        }
        catch
        {
            // Clipboard bitmaps may refuse Freeze; still usable for display and CopyPixels.
        }

        _sampleSource = converted;
        _image.Source = converted;
        _image.Cursor = Cursors.Cross;
        _hint.Text = "Click a pixel on the image to pick its color. Adjust with RGB sliders or the graphical color picker.";
    }

    private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_sampleSource == null)
            return;

        var pos = e.GetPosition(_image);
        var c = SamplePixel(pos);
        SetUiColor(c);
    }

    private Color SamplePixel(Point posOnImage)
    {
        var bmp = _sampleSource!;
        var pw = bmp.PixelWidth;
        var ph = bmp.PixelHeight;
        var aw = _image.ActualWidth;
        var ah = _image.ActualHeight;
        if (aw < 1 || ah < 1)
            return Colors.Black;

        var x = (int)(posOnImage.X * pw / aw);
        var y = (int)(posOnImage.Y * ph / ah);
        x = Math.Clamp(x, 0, pw - 1);
        y = Math.Clamp(y, 0, ph - 1);

        var stride = pw * 4;
        var rect = new Int32Rect(x, y, 1, 1);
        var pixels = new byte[4];
        bmp.CopyPixels(rect, pixels, stride, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private void OnHexTextChanged()
    {
        if (_suppress) return;
        if (!DrawingColorUtilities.TryParseColorString(_hex.Text, out var c) || c.A == 0)
            return;

        _suppress = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        _suppress = false;
        UpdateSwatchOnly(c);
    }

    private void OnRgbSliderChanged()
    {
        if (_suppress) return;
        var c = Color.FromRgb((byte)Math.Round(_r.Value), (byte)Math.Round(_g.Value), (byte)Math.Round(_b.Value));
        _suppress = true;
        _hex.Text = DrawingColorUtilities.FormatHexForTheme(c);
        _suppress = false;
        UpdateSwatchOnly(c);
    }

    private void UpdateSwatchOnly(Color c)
    {
        if (_swatch.Background is SolidColorBrush b && !b.IsFrozen)
            b.Color = c;
        else
            _swatch.Background = new SolidColorBrush(c);
    }

    private void SetUiColor(Color c)
    {
        if (c.A == 0)
            c = Colors.White;

        _suppress = true;
        _r.Value = c.R;
        _g.Value = c.G;
        _b.Value = c.B;
        _hex.Text = DrawingColorUtilities.FormatHexForTheme(Color.FromRgb(c.R, c.G, c.B));
        _suppress = false;
        UpdateSwatchOnly(Color.FromRgb(c.R, c.G, c.B));
    }

    private void OpenGraphicalPicker()
    {
        var currentHex = _hex.Text?.Trim();
        if (!DrawingColorUtilities.TryParseColorString(currentHex, out var parsed) || parsed.A == 0)
            parsed = Color.FromRgb(0x21, 0x96, 0xF3);

        var dlg = new DrawingColorPickerWindow(DrawingColorUtilities.FormatHexForTheme(Color.FromRgb(parsed.R, parsed.G, parsed.B)))
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultHex))
            return;

        if (DrawingColorUtilities.TryParseColorString(dlg.ResultHex, out var c) && c.A != 0)
            SetUiColor(Color.FromRgb(c.R, c.G, c.B));
    }
}
