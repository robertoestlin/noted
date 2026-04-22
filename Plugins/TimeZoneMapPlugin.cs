using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Noted;

public partial class MainWindow
{
    private sealed class TimeZoneMapCity
    {
        public required string Country { get; init; }
        public required string City { get; init; }
        public required double Latitude { get; init; }
        public required double Longitude { get; init; }
        public required string[] TimeZoneIdCandidates { get; init; }
    }

    // Major cities/countries with approximate coordinates and Windows (fallback IANA) time zone ids.
    private static readonly TimeZoneMapCity[] TimeZoneMapCities =
    [
        // Americas
        new() { Country = "USA",        City = "Los Angeles",   Latitude = 34.05,  Longitude = -118.24, TimeZoneIdCandidates = ["Pacific Standard Time", "America/Los_Angeles"] },
        new() { Country = "USA",        City = "Denver",        Latitude = 39.74,  Longitude = -104.99, TimeZoneIdCandidates = ["Mountain Standard Time", "America/Denver"] },
        new() { Country = "USA",        City = "Chicago",       Latitude = 41.88,  Longitude =  -87.63, TimeZoneIdCandidates = ["Central Standard Time", "America/Chicago"] },
        new() { Country = "USA",        City = "New York",      Latitude = 40.71,  Longitude =  -74.00, TimeZoneIdCandidates = ["Eastern Standard Time", "America/New_York"] },
        new() { Country = "USA",        City = "Anchorage",     Latitude = 61.22,  Longitude = -149.90, TimeZoneIdCandidates = ["Alaskan Standard Time", "America/Anchorage"] },
        new() { Country = "USA",        City = "Honolulu",      Latitude = 21.31,  Longitude = -157.86, TimeZoneIdCandidates = ["Hawaiian Standard Time", "Pacific/Honolulu"] },
        new() { Country = "Canada",     City = "Toronto",       Latitude = 43.65,  Longitude =  -79.38, TimeZoneIdCandidates = ["Eastern Standard Time", "America/Toronto"] },
        new() { Country = "Canada",     City = "Vancouver",     Latitude = 49.28,  Longitude = -123.12, TimeZoneIdCandidates = ["Pacific Standard Time", "America/Vancouver"] },
        new() { Country = "Mexico",     City = "Mexico City",   Latitude = 19.43,  Longitude =  -99.13, TimeZoneIdCandidates = ["Central Standard Time (Mexico)", "America/Mexico_City"] },
        new() { Country = "Brazil",     City = "São Paulo",     Latitude = -23.55, Longitude =  -46.63, TimeZoneIdCandidates = ["E. South America Standard Time", "America/Sao_Paulo"] },
        new() { Country = "Argentina",  City = "Buenos Aires",  Latitude = -34.60, Longitude =  -58.38, TimeZoneIdCandidates = ["Argentina Standard Time", "America/Argentina/Buenos_Aires"] },
        new() { Country = "Chile",      City = "Santiago",      Latitude = -33.45, Longitude =  -70.67, TimeZoneIdCandidates = ["Pacific SA Standard Time", "America/Santiago"] },
        new() { Country = "Colombia",   City = "Bogotá",        Latitude =   4.71, Longitude =  -74.07, TimeZoneIdCandidates = ["SA Pacific Standard Time", "America/Bogota"] },
        new() { Country = "Peru",       City = "Lima",          Latitude = -12.05, Longitude =  -77.04, TimeZoneIdCandidates = ["SA Pacific Standard Time", "America/Lima"] },

        // Europe
        new() { Country = "UK",          City = "London",      Latitude = 51.51, Longitude =  -0.13, TimeZoneIdCandidates = ["GMT Standard Time", "Europe/London"] },
        new() { Country = "Ireland",     City = "Dublin",      Latitude = 53.35, Longitude =  -6.26, TimeZoneIdCandidates = ["GMT Standard Time", "Europe/Dublin"] },
        new() { Country = "Iceland",     City = "Reykjavík",   Latitude = 64.15, Longitude = -21.94, TimeZoneIdCandidates = ["Greenwich Standard Time", "Atlantic/Reykjavik"] },
        new() { Country = "Portugal",    City = "Lisbon",      Latitude = 38.72, Longitude =  -9.14, TimeZoneIdCandidates = ["GMT Standard Time", "Europe/Lisbon"] },
        new() { Country = "Spain",       City = "Madrid",      Latitude = 40.42, Longitude =  -3.70, TimeZoneIdCandidates = ["Romance Standard Time", "Europe/Madrid"] },
        new() { Country = "France",      City = "Paris",       Latitude = 48.86, Longitude =   2.35, TimeZoneIdCandidates = ["Romance Standard Time", "Europe/Paris"] },
        new() { Country = "Netherlands", City = "Amsterdam",   Latitude = 52.37, Longitude =   4.90, TimeZoneIdCandidates = ["W. Europe Standard Time", "Europe/Amsterdam"] },
        new() { Country = "Germany",     City = "Berlin",      Latitude = 52.52, Longitude =  13.40, TimeZoneIdCandidates = ["W. Europe Standard Time", "Europe/Berlin"] },
        new() { Country = "Italy",       City = "Rome",        Latitude = 41.90, Longitude =  12.50, TimeZoneIdCandidates = ["W. Europe Standard Time", "Europe/Rome"] },
        new() { Country = "Sweden",      City = "Stockholm",   Latitude = 59.33, Longitude =  18.07, TimeZoneIdCandidates = ["W. Europe Standard Time", "Europe/Stockholm"] },
        new() { Country = "Norway",      City = "Oslo",        Latitude = 59.91, Longitude =  10.75, TimeZoneIdCandidates = ["W. Europe Standard Time", "Europe/Oslo"] },
        new() { Country = "Denmark",     City = "Copenhagen",  Latitude = 55.68, Longitude =  12.57, TimeZoneIdCandidates = ["Romance Standard Time", "Europe/Copenhagen"] },
        new() { Country = "Finland",     City = "Helsinki",    Latitude = 60.17, Longitude =  24.94, TimeZoneIdCandidates = ["FLE Standard Time", "Europe/Helsinki"] },
        new() { Country = "Poland",      City = "Warsaw",      Latitude = 52.23, Longitude =  21.01, TimeZoneIdCandidates = ["Central European Standard Time", "Europe/Warsaw"] },
        new() { Country = "Greece",      City = "Athens",      Latitude = 37.98, Longitude =  23.73, TimeZoneIdCandidates = ["GTB Standard Time", "Europe/Athens"] },
        new() { Country = "Turkey",      City = "Istanbul",    Latitude = 41.01, Longitude =  28.98, TimeZoneIdCandidates = ["Turkey Standard Time", "Europe/Istanbul"] },
        new() { Country = "Ukraine",     City = "Kyiv",        Latitude = 50.45, Longitude =  30.52, TimeZoneIdCandidates = ["FLE Standard Time", "Europe/Kyiv"] },
        new() { Country = "Russia",      City = "Moscow",      Latitude = 55.75, Longitude =  37.62, TimeZoneIdCandidates = ["Russian Standard Time", "Europe/Moscow"] },

        // Africa & Middle East
        new() { Country = "Morocco",      City = "Casablanca",    Latitude = 33.57, Longitude =  -7.59, TimeZoneIdCandidates = ["Morocco Standard Time", "Africa/Casablanca"] },
        new() { Country = "Nigeria",      City = "Lagos",         Latitude =  6.52, Longitude =   3.38, TimeZoneIdCandidates = ["W. Central Africa Standard Time", "Africa/Lagos"] },
        new() { Country = "Egypt",        City = "Cairo",         Latitude = 30.05, Longitude =  31.23, TimeZoneIdCandidates = ["Egypt Standard Time", "Africa/Cairo"] },
        new() { Country = "Kenya",        City = "Nairobi",       Latitude = -1.29, Longitude =  36.82, TimeZoneIdCandidates = ["E. Africa Standard Time", "Africa/Nairobi"] },
        new() { Country = "South Africa", City = "Johannesburg",  Latitude = -26.20,Longitude =  28.05, TimeZoneIdCandidates = ["South Africa Standard Time", "Africa/Johannesburg"] },
        new() { Country = "Israel",       City = "Jerusalem",     Latitude = 31.78, Longitude =  35.22, TimeZoneIdCandidates = ["Israel Standard Time", "Asia/Jerusalem"] },
        new() { Country = "Saudi Arabia", City = "Riyadh",        Latitude = 24.71, Longitude =  46.67, TimeZoneIdCandidates = ["Arab Standard Time", "Asia/Riyadh"] },
        new() { Country = "UAE",          City = "Dubai",         Latitude = 25.27, Longitude =  55.30, TimeZoneIdCandidates = ["Arabian Standard Time", "Asia/Dubai"] },
        new() { Country = "Iran",         City = "Tehran",        Latitude = 35.69, Longitude =  51.39, TimeZoneIdCandidates = ["Iran Standard Time", "Asia/Tehran"] },

        // Asia
        new() { Country = "India",       City = "Mumbai",     Latitude = 19.08, Longitude =  72.88, TimeZoneIdCandidates = ["India Standard Time", "Asia/Kolkata"] },
        new() { Country = "India",       City = "New Delhi",  Latitude = 28.61, Longitude =  77.21, TimeZoneIdCandidates = ["India Standard Time", "Asia/Kolkata"] },
        new() { Country = "Pakistan",    City = "Karachi",    Latitude = 24.86, Longitude =  67.00, TimeZoneIdCandidates = ["Pakistan Standard Time", "Asia/Karachi"] },
        new() { Country = "Bangladesh",  City = "Dhaka",      Latitude = 23.81, Longitude =  90.41, TimeZoneIdCandidates = ["Bangladesh Standard Time", "Asia/Dhaka"] },
        new() { Country = "Thailand",    City = "Bangkok",    Latitude = 13.76, Longitude = 100.50, TimeZoneIdCandidates = ["SE Asia Standard Time", "Asia/Bangkok"] },
        new() { Country = "Vietnam",     City = "Hanoi",      Latitude = 21.03, Longitude = 105.85, TimeZoneIdCandidates = ["SE Asia Standard Time", "Asia/Ho_Chi_Minh"] },
        new() { Country = "Singapore",   City = "Singapore",  Latitude =  1.35, Longitude = 103.82, TimeZoneIdCandidates = ["Singapore Standard Time", "Asia/Singapore"] },
        new() { Country = "Indonesia",   City = "Jakarta",    Latitude = -6.21, Longitude = 106.85, TimeZoneIdCandidates = ["SE Asia Standard Time", "Asia/Jakarta"] },
        new() { Country = "China",       City = "Beijing",    Latitude = 39.90, Longitude = 116.40, TimeZoneIdCandidates = ["China Standard Time", "Asia/Shanghai"] },
        new() { Country = "China",       City = "Shanghai",   Latitude = 31.23, Longitude = 121.47, TimeZoneIdCandidates = ["China Standard Time", "Asia/Shanghai"] },
        new() { Country = "Hong Kong",   City = "Hong Kong",  Latitude = 22.32, Longitude = 114.17, TimeZoneIdCandidates = ["China Standard Time", "Asia/Hong_Kong"] },
        new() { Country = "Taiwan",      City = "Taipei",     Latitude = 25.03, Longitude = 121.57, TimeZoneIdCandidates = ["Taipei Standard Time", "Asia/Taipei"] },
        new() { Country = "South Korea", City = "Seoul",      Latitude = 37.57, Longitude = 126.98, TimeZoneIdCandidates = ["Korea Standard Time", "Asia/Seoul"] },
        new() { Country = "Japan",       City = "Tokyo",      Latitude = 35.68, Longitude = 139.65, TimeZoneIdCandidates = ["Tokyo Standard Time", "Asia/Tokyo"] },

        // Oceania
        new() { Country = "Australia",   City = "Perth",      Latitude = -31.95, Longitude = 115.86, TimeZoneIdCandidates = ["W. Australia Standard Time", "Australia/Perth"] },
        new() { Country = "Australia",   City = "Sydney",     Latitude = -33.87, Longitude = 151.21, TimeZoneIdCandidates = ["AUS Eastern Standard Time", "Australia/Sydney"] },
        new() { Country = "New Zealand", City = "Auckland",   Latitude = -36.85, Longitude = 174.76, TimeZoneIdCandidates = ["New Zealand Standard Time", "Pacific/Auckland"] },
        new() { Country = "Fiji",        City = "Suva",       Latitude = -18.14, Longitude = 178.44, TimeZoneIdCandidates = ["Fiji Standard Time", "Pacific/Fiji"] },
    ];

    private static readonly Uri TimeZoneMapImageUri =
        new("pack://application:,,,/logo/world-map-equirectangular.png", UriKind.Absolute);

    private static BitmapImage? _timeZoneMapImageCache;

    private static BitmapImage GetTimeZoneMapImage()
    {
        if (_timeZoneMapImageCache is not null)
            return _timeZoneMapImageCache;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = TimeZoneMapImageUri;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        _timeZoneMapImageCache = bmp;
        return bmp;
    }

    private static bool TryResolveTimeZoneForCity(TimeZoneMapCity city, out TimeZoneInfo timeZone)
    {
        foreach (var id in city.TimeZoneIdCandidates)
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch
            {
                // Try next candidate id.
            }
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static string FormatGmtOffsetShort(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        var hours = (int)absolute.TotalHours;
        if (absolute.Minutes == 0)
            return string.Create(CultureInfo.InvariantCulture, $"GMT{sign}{hours}");
        return string.Create(CultureInfo.InvariantCulture, $"GMT{sign}{hours}:{absolute.Minutes:00}");
    }

    // Aspect ratio of the embedded world map image (1166 x 597 after cropping to ±180 lon, ±90 lat).
    // Not exactly 2:1 because the source SVG has slightly non-uniform scaling; using the real aspect
    // keeps lat/lon projection accurate to within ~0.5° for every city on the map.
    private const double TimeZoneMapImagePixelWidth = 1166.0;
    private const double TimeZoneMapImagePixelHeight = 597.0;

    private void ShowTimeZoneMapDialog(DateTimeOffset referenceUtc)
    {
        const double mapWidth = 1080;
        const double mapHeight = mapWidth * TimeZoneMapImagePixelHeight / TimeZoneMapImagePixelWidth;

        var dlg = new Window
        {
            Title = "World Time Map",
            Width = 1460,
            Height = 900,
            MinWidth = 1200,
            MinHeight = 720,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "World Time Map",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Reference UTC: {referenceUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 2, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Vertical bands show nominal UTC offset (±N hours). Hover a dot for exact local time; click a dot or a list entry to highlight it.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var btnClose = new Button { Content = "Close", Width = 90, IsDefault = true, IsCancel = true };
        btnClose.Click += (_, _) => dlg.Close();
        closeRow.Children.Add(btnClose);
        DockPanel.SetDock(closeRow, Dock.Bottom);
        root.Children.Add(closeRow);

        // Main split: map on the left, list of countries on the right.
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        var mapScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 10, 0)
        };

        var mapCanvas = new Canvas
        {
            Width = mapWidth,
            Height = mapHeight,
            Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xE9, 0xF4)) // ocean
        };

        // Base map image (public domain equirectangular world map, CIA World Factbook).
        try
        {
            var mapImage = new Image
            {
                Source = GetTimeZoneMapImage(),
                Width = mapWidth,
                Height = mapHeight,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(mapImage, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(mapImage, 0);
            Canvas.SetTop(mapImage, 0);
            mapCanvas.Children.Add(mapImage);
        }
        catch
        {
            // Image resource not available - continue without base map.
        }

        // Alternating UTC offset bands (24 bands of 15° each, centered on multiples of 15°),
        // drawn on top of the map with low opacity so the map still shows through.
        var bandShade = new SolidColorBrush(Color.FromArgb(0x26, 0x1F, 0x3A, 0x68));
        for (var offsetHours = -12; offsetHours <= 12; offsetHours++)
        {
            var leftLon = offsetHours * 15 - 7.5;
            var rightLon = offsetHours * 15 + 7.5;
            var x1 = ProjectLongitude(leftLon, mapWidth);
            var x2 = ProjectLongitude(rightLon, mapWidth);
            var bandRect = new Rectangle
            {
                Width = Math.Max(0, x2 - x1),
                Height = mapHeight,
                Fill = offsetHours % 2 == 0 ? Brushes.Transparent : bandShade,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bandRect, x1);
            Canvas.SetTop(bandRect, 0);
            mapCanvas.Children.Add(bandRect);
        }

        // Draw vertical band separators + top labels.
        var gridStroke = new SolidColorBrush(Color.FromArgb(0x55, 0x5A, 0x6F, 0x8E));
        for (var offsetHours = -12; offsetHours <= 12; offsetHours++)
        {
            var boundaryLon = offsetHours * 15 - 7.5;
            var x = ProjectLongitude(boundaryLon, mapWidth);
            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = mapHeight,
                Stroke = gridStroke,
                StrokeThickness = 0.5,
                IsHitTestVisible = false
            };
            mapCanvas.Children.Add(line);

            var labelLon = offsetHours * 15;
            var labelX = ProjectLongitude(labelLon, mapWidth);
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                BorderBrush = gridStroke,
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = offsetHours >= 0
                        ? string.Create(CultureInfo.InvariantCulture, $"+{offsetHours}")
                        : string.Create(CultureInfo.InvariantCulture, $"{offsetHours}"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x3D, 0x5A))
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, labelX - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, 2);
            mapCanvas.Children.Add(label);

            var labelBottom = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                BorderBrush = gridStroke,
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0, 3, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = offsetHours == 0 ? "GMT" : string.Create(CultureInfo.InvariantCulture, $"GMT{(offsetHours > 0 ? "+" : "")}{offsetHours}"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x3D, 0x5A))
                }
            };
            labelBottom.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(labelBottom, labelX - labelBottom.DesiredSize.Width / 2);
            Canvas.SetTop(labelBottom, mapHeight - labelBottom.DesiredSize.Height - 2);
            mapCanvas.Children.Add(labelBottom);
        }

        // Equator and prime meridian.
        mapCanvas.Children.Add(new Line
        {
            X1 = 0,
            X2 = mapWidth,
            Y1 = mapHeight / 2,
            Y2 = mapHeight / 2,
            Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x5A, 0x6F, 0x8E)),
            StrokeThickness = 0.5,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            IsHitTestVisible = false
        });

        // Build list grouped by offset.
        var listStack = new StackPanel();

        var resolved = new List<(TimeZoneMapCity City, TimeZoneInfo TimeZone, TimeSpan Offset, DateTime Local)>();
        foreach (var city in TimeZoneMapCities)
        {
            if (!TryResolveTimeZoneForCity(city, out var tz))
                continue;
            var offset = tz.GetUtcOffset(referenceUtc.UtcDateTime);
            var local = TimeZoneInfo.ConvertTime(referenceUtc, tz).DateTime;
            resolved.Add((city, tz, offset, local));
        }

        var dotsByCity = new Dictionary<TimeZoneMapCity, (Ellipse Dot, Ellipse Ring)>();
        var listItemsByCity = new Dictionary<TimeZoneMapCity, Border>();
        TimeZoneMapCity? selectedCity = null;
        var selectionBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xEE, 0xB8));
        var selectionRingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x00));

        void SelectCity(TimeZoneMapCity city)
        {
            if (selectedCity is not null)
            {
                if (dotsByCity.TryGetValue(selectedCity, out var prev))
                    prev.Ring.Visibility = Visibility.Collapsed;
                if (listItemsByCity.TryGetValue(selectedCity, out var prevItem))
                    prevItem.Background = Brushes.Transparent;
            }

            selectedCity = city;

            if (dotsByCity.TryGetValue(city, out var current))
            {
                current.Ring.Visibility = Visibility.Visible;
                // Bring the selected dot and its ring to the top of the z-order.
                mapCanvas.Children.Remove(current.Ring);
                mapCanvas.Children.Remove(current.Dot);
                mapCanvas.Children.Add(current.Ring);
                mapCanvas.Children.Add(current.Dot);
            }

            if (listItemsByCity.TryGetValue(city, out var item))
            {
                item.Background = selectionBrush;
                item.BringIntoView();
            }
        }

        foreach (var entry in resolved)
        {
            var x = ProjectLongitude(entry.City.Longitude, mapWidth);
            var y = ProjectLatitude(entry.City.Latitude, mapHeight);

            var ring = new Ellipse
            {
                Width = 20,
                Height = 20,
                Stroke = selectionRingBrush,
                StrokeThickness = 2.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Canvas.SetLeft(ring, x - 10);
            Canvas.SetTop(ring, y - 10);
            mapCanvas.Children.Add(ring);

            var tooltipText = string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.City.City}, {entry.City.Country}\n{entry.Local:yyyy-MM-dd HH:mm} ({FormatGmtOffsetShort(entry.Offset)})\n{BuildTimeZoneAbbreviation(entry.TimeZone, entry.Local)}  {entry.TimeZone.Id}");

            var dot = new Ellipse
            {
                Width = 11,
                Height = 11,
                Fill = BrushForOffset(entry.Offset),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                ToolTip = tooltipText,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ToolTipService.SetInitialShowDelay(dot, 0);
            ToolTipService.SetBetweenShowDelay(dot, 0);
            ToolTipService.SetShowDuration(dot, 30000);
            var entryCaptured = entry;
            dot.MouseLeftButtonDown += (_, e) =>
            {
                SelectCity(entryCaptured.City);
                e.Handled = true;
            };
            Canvas.SetLeft(dot, x - 5.5);
            Canvas.SetTop(dot, y - 5.5);
            mapCanvas.Children.Add(dot);

            dotsByCity[entry.City] = (dot, ring);
        }

        mapScroll.Content = mapCanvas;
        Grid.SetColumn(mapScroll, 0);
        split.Children.Add(mapScroll);

        // Right column - countries grouped by offset.
        var listScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var groups = resolved
            .GroupBy(e => e.Offset)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var group in groups)
        {
            var offset = group.Key;
            var gmtLabel = FormatGmtOffsetShort(offset);
            var utcLabel = FormatUtcOffset(offset);

            var headerPanel = new Border
            {
                Background = BrushForOffset(offset),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 8, 0, 4),
                Child = new TextBlock
                {
                    Text = $"{gmtLabel}  ({utcLabel})",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                }
            };
            listStack.Children.Add(headerPanel);

            foreach (var entry in group.OrderBy(e => e.City.Country).ThenBy(e => e.City.City))
            {
                var line = new TextBlock
                {
                    Margin = new Thickness(0, 0, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                line.Inlines.Add(new Run
                {
                    Text = $"{entry.City.City}",
                    FontWeight = FontWeights.SemiBold
                });
                line.Inlines.Add(new Run { Text = $" ({entry.City.Country})  " });
                line.Inlines.Add(new Run
                {
                    Text = entry.Local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    FontFamily = new FontFamily("Consolas, Courier New")
                });
                var dst = entry.TimeZone.IsDaylightSavingTime(entry.Local) ? "  DST" : string.Empty;
                if (!string.IsNullOrEmpty(dst))
                {
                    line.Inlines.Add(new Run
                    {
                        Text = dst,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB2, 0x6B, 0x00)),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                var itemBorder = new Border
                {
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 3, 6, 3),
                    Margin = new Thickness(4, 1, 4, 1),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = line
                };
                var entryCaptured = entry;
                itemBorder.MouseEnter += (_, _) =>
                {
                    if (!ReferenceEquals(selectedCity, entryCaptured.City))
                        itemBorder.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xEE, 0xB8));
                };
                itemBorder.MouseLeave += (_, _) =>
                {
                    if (!ReferenceEquals(selectedCity, entryCaptured.City))
                        itemBorder.Background = Brushes.Transparent;
                };
                itemBorder.MouseLeftButtonDown += (_, e) =>
                {
                    SelectCity(entryCaptured.City);
                    e.Handled = true;
                };
                listItemsByCity[entry.City] = itemBorder;
                listStack.Children.Add(itemBorder);
            }
        }

        listScroll.Content = listStack;
        Grid.SetColumn(listScroll, 1);
        split.Children.Add(listScroll);

        root.Children.Add(split);

        dlg.Content = root;
        dlg.ShowDialog();
    }

    private static double ProjectLongitude(double longitude, double mapWidth)
        => (longitude + 180.0) / 360.0 * mapWidth;

    private static double ProjectLatitude(double latitude, double mapHeight)
        => (90.0 - latitude) / 180.0 * mapHeight;

    private static Brush BrushForOffset(TimeSpan offset)
    {
        // Hue cycles through a gentle rainbow across the 24-hour span.
        var hours = offset.TotalHours;
        var normalized = (hours + 12.0) / 24.0;
        normalized = Math.Max(0.0, Math.Min(1.0, normalized));
        var hue = normalized * 300.0; // 0..300 -> red..magenta, avoid wrap back to red
        return new SolidColorBrush(HslToRgb(hue, 0.55, 0.45));
    }

    private static Color HslToRgb(double hue, double saturation, double lightness)
    {
        double r, g, b;
        if (saturation <= 0.0)
        {
            r = g = b = lightness;
        }
        else
        {
            var q = lightness < 0.5
                ? lightness * (1 + saturation)
                : lightness + saturation - lightness * saturation;
            var p = 2 * lightness - q;
            var hk = hue / 360.0;
            r = HueToRgb(p, q, hk + 1.0 / 3.0);
            g = HueToRgb(p, q, hk);
            b = HueToRgb(p, q, hk - 1.0 / 3.0);
        }
        return Color.FromRgb(
            (byte)Math.Round(Math.Max(0, Math.Min(1, r)) * 255),
            (byte)Math.Round(Math.Max(0, Math.Min(1, g)) * 255),
            (byte)Math.Round(Math.Max(0, Math.Min(1, b)) * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
        return p;
    }
}
