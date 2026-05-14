using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

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
    private readonly Canvas _pickOverlay;
    private readonly Ellipse _pickMarkerOuter;
    private readonly Ellipse _pickMarkerInner;
    private readonly TextBlock _hint;
    private readonly TextBox _hex;
    private readonly Slider _r;
    private readonly Slider _g;
    private readonly Slider _b;
    private readonly Border _swatch;
    private BitmapSource? _sampleSource;
    private bool _suppress;
    private double? _pickMarkerNormX;
    private double? _pickMarkerNormY;

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
            Content = CreateGraphicalPickerButtonIcon(),
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
            BorderThickness = new Thickness(1),
            ToolTip = "Full color picker (same as in Drawing)",
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
            Text = "Paste a screenshot (Ctrl+V or Paste). The center pixel is chosen automatically; click the image to move the ring. Adjust with RGB sliders or the eyedropper color-picker button.",
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
        var imageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _image.MouseLeftButtonDown += OnImageMouseLeftButtonDown;
        _pickOverlay = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        _pickMarkerOuter = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _pickMarkerInner = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _pickOverlay.Children.Add(_pickMarkerOuter);
        _pickOverlay.Children.Add(_pickMarkerInner);
        imageHost.Children.Add(_image);
        imageHost.Children.Add(_pickOverlay);
        _image.SizeChanged += (_, _) => SyncPickOverlayToImage();
        scroll.Content = imageHost;

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

    // Segoe MDL2 eyedropper (EF3C) over a spectrum strip — standard picker imagery with visible color.
    private static UIElement CreateGraphicalPickerButtonIcon()
    {
        var grid = new Grid { Width = 40, Height = 40, SnapsToDevicePixels = true };

        var spectrumBand = new Border
        {
            Height = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 4, 5),
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.OrangeRed, 0),
                    new(Colors.Gold, 0.22),
                    new(Colors.LimeGreen, 0.48),
                    new(Colors.DeepSkyBlue, 0.72),
                    new(Colors.MediumPurple, 1),
                },
                new Point(0, 0.5),
                new Point(1, 0.5)),
        };

        var glyph = new TextBlock
        {
            Text = "\uEF3C",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -4, 0, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
        };

        grid.Children.Add(spectrumBand);
        grid.Children.Add(glyph);
        return grid;
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
        _hint.Text = "The center pixel is selected. Click elsewhere on the image to pick another color. Adjust with RGB sliders or the eyedropper color-picker button.";
        HidePickMarker();
        ScheduleSampleImageCenter();
    }

    private void ScheduleSampleImageCenter()
    {
        void TryApply(int attempt)
        {
            if (_sampleSource == null)
                return;
            if (_image.ActualWidth < 1 || _image.ActualHeight < 1)
            {
                if (attempt < 12)
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => TryApply(attempt + 1)));
                return;
            }

            var cx = _image.ActualWidth * 0.5;
            var cy = _image.ActualHeight * 0.5;
            var pos = new Point(cx, cy);
            var c = SamplePixel(pos);
            SetUiColor(c);
            ShowPickMarkerAt(pos);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => TryApply(0)));
    }

    private void SyncPickOverlayToImage()
    {
        _pickOverlay.Width = _image.ActualWidth;
        _pickOverlay.Height = _image.ActualHeight;
        if (_pickMarkerNormX is not { } nx || _pickMarkerNormY is not { } ny)
            return;
        if (_image.ActualWidth < 1 || _image.ActualHeight < 1)
            return;
        ShowPickMarkerAt(new Point(nx * _image.ActualWidth, ny * _image.ActualHeight));
    }

    private void HidePickMarker()
    {
        _pickMarkerNormX = null;
        _pickMarkerNormY = null;
        _pickMarkerOuter.Visibility = Visibility.Collapsed;
        _pickMarkerInner.Visibility = Visibility.Collapsed;
    }

    private void ShowPickMarkerAt(Point centerOnImage)
    {
        if (_image.ActualWidth > 0 && _image.ActualHeight > 0)
        {
            _pickMarkerNormX = centerOnImage.X / _image.ActualWidth;
            _pickMarkerNormY = centerOnImage.Y / _image.ActualHeight;
        }

        var w = _pickMarkerOuter.Width;
        var h = _pickMarkerOuter.Height;
        var left = centerOnImage.X - w * 0.5;
        var top = centerOnImage.Y - h * 0.5;
        Canvas.SetLeft(_pickMarkerOuter, left);
        Canvas.SetTop(_pickMarkerOuter, top);
        Canvas.SetLeft(_pickMarkerInner, left);
        Canvas.SetTop(_pickMarkerInner, top);
        _pickMarkerOuter.Visibility = Visibility.Visible;
        _pickMarkerInner.Visibility = Visibility.Visible;
    }

    private void OnImageMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_sampleSource == null)
            return;

        var pos = e.GetPosition(_image);
        var c = SamplePixel(pos);
        SetUiColor(c);
        ShowPickMarkerAt(pos);
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
