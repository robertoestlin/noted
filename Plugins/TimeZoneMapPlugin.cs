using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

    // Rough equirectangular continent outlines (longitude, latitude).
    private static readonly (double Lon, double Lat)[][] TimeZoneMapContinents =
    [
        // North America
        [
            (-168, 66), (-141, 70), (-125, 72), (-100, 74), (-80, 73), (-70, 65),
            (-60, 55), (-54, 47), (-65, 45), (-70, 43), (-76, 35), (-80, 30),
            (-82, 25), (-85, 25), (-95, 29), (-100, 18), (-108, 16), (-115, 32),
            (-125, 40), (-130, 55), (-150, 60), (-168, 66)
        ],
        // South America
        [
            (-80, 10), (-75, 12), (-62, 11), (-50, 5), (-40, 0), (-35, -8),
            (-40, -22), (-50, -30), (-58, -40), (-68, -53), (-73, -50), (-74, -40),
            (-76, -30), (-80, -20), (-80, -5), (-80, 10)
        ],
        // Europe (combined with Asia via land bridge, but drawn separate for visual)
        [
            (-10, 36), (-5, 43), (10, 43), (15, 38), (25, 36), (30, 40), (40, 42),
            (45, 50), (60, 60), (65, 70), (30, 72), (20, 70), (10, 63), (0, 58),
            (-5, 58), (-10, 50), (-10, 36)
        ],
        // Africa
        [
            (-17, 15), (-10, 27), (-5, 32), (10, 35), (25, 32), (34, 31), (43, 12),
            (51, 12), (45, 4), (42, -5), (40, -15), (35, -20), (25, -34), (18, -35),
            (12, -20), (8, -5), (-2, 5), (-8, 7), (-15, 11), (-17, 15)
        ],
        // Asia
        [
            (45, 50), (60, 60), (80, 75), (140, 75), (170, 70), (180, 65), (180, 60),
            (160, 58), (140, 52), (135, 42), (125, 40), (120, 30), (115, 22), (108, 15),
            (100, 5), (95, 5), (85, 20), (75, 22), (68, 22), (60, 25), (55, 28),
            (50, 30), (42, 40), (42, 42), (45, 50)
        ],
        // Australia
        [
            (115, -12), (130, -12), (140, -10), (145, -14), (153, -25), (150, -38),
            (140, -38), (128, -32), (115, -34), (113, -22), (115, -12)
        ],
        // Greenland
        [
            (-55, 60), (-40, 59), (-20, 62), (-15, 75), (-30, 83), (-55, 80), (-55, 60)
        ],
        // UK
        [
            (-6, 50), (-2, 50), (1, 52), (-2, 55), (-4, 59), (-6, 56), (-6, 50)
        ],
        // Japan
        [
            (130, 33), (135, 34), (140, 36), (142, 44), (140, 45), (136, 37), (133, 35), (130, 33)
        ],
        // Madagascar
        [
            (43, -14), (50, -15), (49, -25), (44, -25), (43, -14)
        ],
        // New Zealand (north)
        [
            (172, -34), (178, -37), (178, -42), (174, -42), (172, -38), (172, -34)
        ],
        // New Zealand (south)
        [
            (166, -46), (174, -46), (174, -41), (170, -41), (166, -46)
        ],
        // Indonesia (rough arc)
        [
            (95, 5), (105, 5), (116, -2), (125, -6), (140, -4), (140, -8), (120, -10),
            (108, -8), (95, 0), (95, 5)
        ],
        // Antarctica strip
        [
            (-180, -65), (180, -65), (180, -85), (-180, -85), (-180, -65)
        ],
    ];

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

    private void ShowTimeZoneMapDialog(DateTimeOffset referenceUtc)
    {
        const double mapWidth = 1080;
        const double mapHeight = 540;

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
            Text = "Vertical bands show nominal UTC offset (±N hours). Dots mark cities; hover for exact local time.",
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

        // Draw alternating UTC offset bands (24 bands of 15° each, centered on multiples of 15°).
        var bandShade = new SolidColorBrush(Color.FromArgb(0x22, 0x1F, 0x3A, 0x68));
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

        // Draw continent polygons.
        var landFill = new SolidColorBrush(Color.FromRgb(0xC7, 0xE3, 0xBE));
        var landStroke = new SolidColorBrush(Color.FromRgb(0x7D, 0xAC, 0x75));
        foreach (var continent in TimeZoneMapContinents)
        {
            var poly = new Polygon
            {
                Fill = landFill,
                Stroke = landStroke,
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            var points = new PointCollection(continent.Length);
            foreach (var (lon, lat) in continent)
                points.Add(new Point(ProjectLongitude(lon, mapWidth), ProjectLatitude(lat, mapHeight)));
            poly.Points = points;
            mapCanvas.Children.Add(poly);
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

        // Draw city dots with tooltips.
        foreach (var entry in resolved)
        {
            var x = ProjectLongitude(entry.City.Longitude, mapWidth);
            var y = ProjectLatitude(entry.City.Latitude, mapHeight);
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = BrushForOffset(entry.Offset),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                ToolTip = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.City.City}, {entry.City.Country}\n{entry.Local:yyyy-MM-dd HH:mm} ({FormatGmtOffsetShort(entry.Offset)})\n{BuildTimeZoneAbbreviation(entry.TimeZone, entry.Local)}  {entry.TimeZone.Id}"),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            Canvas.SetLeft(dot, x - 4.5);
            Canvas.SetTop(dot, y - 4.5);
            mapCanvas.Children.Add(dot);
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
                    Margin = new Thickness(10, 2, 4, 2),
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
                listStack.Children.Add(line);
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
