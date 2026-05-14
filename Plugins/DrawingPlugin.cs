using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Noted;

public partial class MainWindow
{
    private void ShowDrawingDialog()
    {
        var dlg = new DrawingWindow { Owner = this };
        dlg.Show();
    }
}

internal sealed class ThemeToolStyle
{
    public string Fill { get; set; } = "Transparent";
    public string Stroke { get; set; } = "#000000";
    public string TextColor { get; set; } = "#000000";
    public string FontFamilyName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 22;
    public double Thickness { get; set; } = 2;
    public double CornerRadius { get; set; } = 14;
    public string ArrowHead { get; set; } = "Simple";
    public double FreeThickness { get; set; } = 6;

    public ThemeToolStyle Clone() => new()
    {
        Fill = Fill,
        Stroke = Stroke,
        TextColor = TextColor,
        FontFamilyName = FontFamilyName,
        FontSize = FontSize,
        Thickness = Thickness,
        CornerRadius = CornerRadius,
        ArrowHead = ArrowHead,
        FreeThickness = FreeThickness,
    };

    public static ThemeToolStyle FromLegacyRoot(DrawingTheme t) => new()
    {
        Fill = t.Fill,
        Stroke = t.Stroke,
        TextColor = t.TextColor,
        FontFamilyName = t.FontFamilyName,
        FontSize = t.FontSize,
        Thickness = t.Thickness,
        CornerRadius = t.CornerRadius,
        ArrowHead = t.ArrowHead,
        FreeThickness = t.FreeThickness,
    };
}

internal sealed class DrawingTheme
{
    public string Name { get; set; } = "Default";

    /// <summary>Per-tool defaults. Null in JSON means "use legacy root fields" until <see cref="EnsureToolStyles"/> runs.</summary>
    public ThemeToolStyle? Rectangle { get; set; }
    public ThemeToolStyle? Ellipse { get; set; }
    public ThemeToolStyle? Arrow { get; set; }
    public ThemeToolStyle? Text { get; set; }
    public ThemeToolStyle? Freehand { get; set; }

    // Legacy root fields (still read from older theme files; mirrored on save for compatibility).
    public string Fill { get; set; } = "Transparent";
    public string Stroke { get; set; } = "#000000";
    public string TextColor { get; set; } = "#000000";
    public string FontFamilyName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 22;
    public double Thickness { get; set; } = 2;
    public double CornerRadius { get; set; } = 14;
    public string ArrowHead { get; set; } = "Simple";
    public double FreeThickness { get; set; } = 6;

    public void EnsureToolStyles()
    {
        if (Rectangle != null && Ellipse != null && Arrow != null && Text != null && Freehand != null)
            return;
        var seed = ThemeToolStyle.FromLegacyRoot(this);
        Rectangle ??= seed.Clone();
        Ellipse ??= seed.Clone();
        Arrow ??= seed.Clone();
        Text ??= seed.Clone();
        Freehand ??= seed.Clone();
    }

    /// <summary>Copies per-tool values back to legacy root properties for JSON consumers that only read flat fields.</summary>
    public void MirrorLegacyFromProfiles()
    {
        EnsureToolStyles();
        var r = Rectangle!;
        Fill = r.Fill;
        Stroke = r.Stroke;
        TextColor = r.TextColor;
        FontFamilyName = r.FontFamilyName;
        FontSize = r.FontSize;
        Thickness = r.Thickness;
        CornerRadius = r.CornerRadius;
        ArrowHead = Arrow!.ArrowHead;
        FreeThickness = Freehand!.FreeThickness;
    }

    public DrawingTheme Clone() => new()
    {
        Name = Name,
        Fill = Fill,
        Stroke = Stroke,
        TextColor = TextColor,
        FontFamilyName = FontFamilyName,
        FontSize = FontSize,
        Thickness = Thickness,
        CornerRadius = CornerRadius,
        ArrowHead = ArrowHead,
        FreeThickness = FreeThickness,
        Rectangle = Rectangle?.Clone(),
        Ellipse = Ellipse?.Clone(),
        Arrow = Arrow?.Clone(),
        Text = Text?.Clone(),
        Freehand = Freehand?.Clone(),
    };
}

internal static class DrawingThemeStore
{
    private const string FolderName = @"c:\tools\backup\noted";
    private const string FileName = "drawing-themes.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => System.IO.Path.Combine(FolderName, FileName);

    public static List<DrawingTheme> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return CreateDefaults();
            var loaded = JsonSerializer.Deserialize<List<DrawingTheme>>(File.ReadAllText(FilePath));
            if (loaded == null || loaded.Count == 0)
                return CreateDefaults();
            foreach (var t in loaded)
                t.EnsureToolStyles();
            return loaded;
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public static void Save(List<DrawingTheme> themes)
    {
        try
        {
            foreach (var t in themes)
            {
                t.EnsureToolStyles();
                t.MirrorLegacyFromProfiles();
            }
            Directory.CreateDirectory(FolderName);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(themes, Options));
        }
        catch
        {
            // ignore
        }
    }

    public static List<DrawingTheme> CreateDefaults() => new()
    {
        new DrawingTheme
        {
            Name = "Default", Fill = "Transparent", Stroke = "#222222", TextColor = "#222222",
            FontFamilyName = "Segoe UI", FontSize = 22, Thickness = 2, CornerRadius = 14,
            ArrowHead = "Simple", FreeThickness = 6,
        },
        new DrawingTheme
        {
            Name = "Marker", Fill = "#FFF6A9", Stroke = "#1F1F1F", TextColor = "#1F1F1F",
            FontFamilyName = "Segoe UI", FontSize = 24, Thickness = 3, CornerRadius = 18,
            ArrowHead = "Simple", FreeThickness = 9,
        },
        new DrawingTheme
        {
            Name = "Notebook", Fill = "#F5F5DC", Stroke = "#3F51B5", TextColor = "#3F51B5",
            FontFamilyName = "Comic Sans MS", FontSize = 22, Thickness = 2, CornerRadius = 10,
            ArrowHead = "Simple", FreeThickness = 5,
        },
        new DrawingTheme
        {
            Name = "Mono", Fill = "Transparent", Stroke = "#000000", TextColor = "#000000",
            FontFamilyName = "Consolas", FontSize = 18, Thickness = 1.5, CornerRadius = 0,
            ArrowHead = "Simple", FreeThickness = 3,
        },
    };
}

internal sealed class DrawingWindow : Window
{
    private enum Tool { Select, Rectangle, Ellipse, Arrow, Text, Freehand }

    private enum ArrowHeadStyle { None, Simple, Double }

    private sealed class DrawItem
    {
        public string Kind = "rect"; // rect, ellipse, arrow, text, freehand
        public Point P1;
        public Point P2;
        public double CornerRadius = 14;
        public Brush Fill = Brushes.Transparent;
        public Brush Stroke = Brushes.Black;
        public double StrokeThickness = 2;
        public string Text = "";
        public double FontSize = 22;
        public FontFamily FontFamily = new("Segoe UI");
        public Brush TextColor = Brushes.Black;
        public ArrowHeadStyle ArrowHead = ArrowHeadStyle.Simple;
        public List<Point> Points = new();

        public DrawItem Clone() => new()
        {
            Kind = Kind,
            P1 = P1,
            P2 = P2,
            CornerRadius = CornerRadius,
            Fill = Fill,
            Stroke = Stroke,
            StrokeThickness = StrokeThickness,
            Text = Text,
            FontSize = FontSize,
            FontFamily = FontFamily,
            TextColor = TextColor,
            ArrowHead = ArrowHead,
            Points = new List<Point>(Points),
        };
    }

    private static readonly Color[] PaletteColors =
    {
        Colors.Transparent,
        Colors.Black,
        Colors.White,
        Color.FromRgb(0x55, 0x55, 0x55),
        Color.FromRgb(0xBB, 0xBB, 0xBB),
        Color.FromRgb(0xE5, 0x39, 0x35),
        Color.FromRgb(0xF6, 0x8A, 0x1E),
        Color.FromRgb(0xFF, 0xD7, 0x00),
        Color.FromRgb(0xFF, 0xF6, 0xA9),
        Color.FromRgb(0x4C, 0xAF, 0x50),
        Color.FromRgb(0xA8, 0xE6, 0xA1),
        Color.FromRgb(0x21, 0x96, 0xF3),
        Color.FromRgb(0xB3, 0xE5, 0xFC),
        Color.FromRgb(0x67, 0x3A, 0xB7),
        Color.FromRgb(0xD1, 0xC4, 0xE9),
        Color.FromRgb(0x79, 0x55, 0x48),
    };

    private static readonly string[] FontChoices =
    {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Consolas", "Courier New",
        "Georgia", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
        "Comic Sans MS", "Impact",
    };

    private readonly List<DrawItem> _items = new();
    private readonly Stack<List<DrawItem>> _undoStack = new();
    private readonly Stack<List<DrawItem>> _redoStack = new();
    private const int MaxUndoDepth = 100;

    private List<DrawingTheme> _themes;
    private DrawingTheme _activeTheme;
    private bool _propertiesVisible = true;
    private ScrollViewer? _leftScroll;

    private readonly Canvas _canvas;
    private readonly Canvas _overlay;
    private readonly TextBlock _status;

    private readonly ComboBox _themeCombo;
    private readonly StackPanel _propertyPanel;

    private SwatchPalette? _fillPalette;
    private SwatchPalette? _strokePalette;
    private SwatchPalette? _textColorPalette;
    private Slider? _thicknessSlider;
    private Slider? _cornerSlider;
    private Slider? _fontSizeSlider;
    private ComboBox? _fontFamilyCombo;
    private ComboBox? _arrowHeadCombo;

    private readonly Button _btnSelect, _btnRect, _btnEllipse, _btnArrow, _btnText, _btnFreehand;

    private DrawItem? _selected;
    private Tool _tool = Tool.Select;

    private Brush _fill = Brushes.Transparent;
    private Brush _stroke = Brushes.Black;
    private Brush _textColor = Brushes.Black;
    private double _thickness = 2;
    private double _cornerRadius = 14;
    private double _fontSize = 22;
    private FontFamily _fontFamily = new("Segoe UI");
    private ArrowHeadStyle _arrowHead = ArrowHeadStyle.Simple;
    private double _freeThickness = 6;

    private bool _isDrawing;
    private DrawItem? _drawingItem;

    private bool _isMoving;
    private Point _moveLast;
    private int _activeHandle = -1;
    private bool _pendingUndoForGesture;

    private TextBox? _activeEditor;
    private DrawItem? _editingItem;
    private bool _editChangedAnything;

    private bool _suppressPropertyChanges;

    private const double HandleSize = 8;
    private const double HitPadding = 6;

    public DrawingWindow()
    {
        Title = "Drawing";
        Width = 1280;
        Height = 860;
        MinWidth = 780;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _themes = DrawingThemeStore.Load();
        _activeTheme = _themes.FirstOrDefault() ?? new DrawingTheme();
        ApplyTheme(_activeTheme, syncUiOnly: false);

        var root = new DockPanel();

        // ---- Top toolbar: tool buttons + theme picker + actions ----
        var topBar = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xEF)),
            LastChildFill = false,
        };
        DockPanel.SetDock(topBar, Dock.Top);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        _btnSelect = MakeToolButton("Select", "Select (S)", Tool.Select);
        _btnRect = MakeToolButton("Rectangle", "Rectangle (R)", Tool.Rectangle);
        _btnEllipse = MakeToolButton("Circle", "Circle (C)", Tool.Ellipse);
        _btnArrow = MakeToolButton("Arrow", "Arrow (A)", Tool.Arrow);
        _btnText = MakeToolButton("Text", "Text (T)", Tool.Text);
        _btnFreehand = MakeToolButton("Freeform", "Freeform (F)", Tool.Freehand);
        tools.Children.Add(_btnSelect);
        tools.Children.Add(_btnRect);
        tools.Children.Add(_btnEllipse);
        tools.Children.Add(_btnArrow);
        tools.Children.Add(_btnText);
        tools.Children.Add(_btnFreehand);
        topBar.Children.Add(tools);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        DockPanel.SetDock(actions, Dock.Right);

        actions.Children.Add(new TextBlock
        {
            Text = "Theme",
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
        });

        _themeCombo = new ComboBox
        {
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        RefreshThemeCombo();
        _themeCombo.SelectionChanged += (_, _) =>
        {
            if (_themeCombo.SelectedItem is DrawingTheme t)
            {
                _activeTheme = t;
                ApplyTheme(t, syncUiOnly: false);
            }
        };
        actions.Children.Add(_themeCombo);

        var btnSettings = new Button
        {
            Content = "⚙",
            FontSize = 16,
            ToolTip = "Theme settings",
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 12, 0),
        };
        btnSettings.Click += (_, _) => ShowThemeSettings();
        actions.Children.Add(btnSettings);

        var btnClear = MakeButton("Clear");
        btnClear.Click += (_, _) =>
        {
            if (_items.Count == 0) return;
            if (MessageBox.Show(this, "Clear the whole canvas?", "Drawing",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            {
                SnapshotForUndo();
                _items.Clear();
                _selected = null;
                Redraw();
            }
        };
        actions.Children.Add(btnClear);

        var btnSave = MakeButton("Save as PNG");
        btnSave.Click += (_, _) => SavePng();
        actions.Children.Add(btnSave);

        topBar.Children.Add(actions);
        root.Children.Add(topBar);

        // ---- Status bar ----
        _status = new TextBlock
        {
            Margin = new Thickness(8, 6, 8, 6),
            Foreground = Brushes.DimGray,
        };
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        // ---- Left properties panel ----
        _leftScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 230,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
        };
        DockPanel.SetDock(_leftScroll, Dock.Left);
        _propertyPanel = new StackPanel { Margin = new Thickness(10) };
        _leftScroll.Content = _propertyPanel;
        BuildPropertyPanel();
        root.Children.Add(_leftScroll);

        // ---- Center canvas ----
        var center = new Grid { Background = Brushes.White };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
        };
        var canvasHost = new Grid();
        _canvas = new Canvas
        {
            Background = Brushes.White,
            Width = 2400,
            Height = 1600,
            ClipToBounds = true,
        };
        _overlay = new Canvas
        {
            Background = null,
            Width = 2400,
            Height = 1600,
            IsHitTestVisible = false,
        };
        canvasHost.Children.Add(_canvas);
        canvasHost.Children.Add(_overlay);
        scroll.Content = canvasHost;
        center.Children.Add(scroll);
        root.Children.Add(center);

        Content = root;

        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseUp;
        _canvas.MouseRightButtonDown += (_, _) => SelectTool(Tool.Select);

        PreviewKeyDown += DrawingWindow_PreviewKeyDown;

        SelectTool(Tool.Select);
        UpdateStatus();
    }

    // ---------------- Theme ----------------

    private void RefreshThemeCombo()
    {
        var prev = _activeTheme?.Name;
        _themeCombo.ItemsSource = null;
        _themeCombo.DisplayMemberPath = "Name";
        _themeCombo.ItemsSource = _themes;
        var match = _themes.FirstOrDefault(t => t.Name == prev) ?? _themes.FirstOrDefault();
        if (match != null)
        {
            _activeTheme = match;
            _themeCombo.SelectedItem = match;
        }
    }

    private void ApplyTheme(DrawingTheme theme, bool syncUiOnly)
    {
        ApplyToolProfileFromTheme(theme, ThemeToolSource());
        if (_propertyPanel != null)
            SyncPropertyControlsFromCurrent();
    }

    private Tool MapDrawKindToTool(string kind) => kind switch
    {
        "rect" => Tool.Rectangle,
        "ellipse" => Tool.Ellipse,
        "arrow" => Tool.Arrow,
        "text" => Tool.Text,
        "freehand" => Tool.Freehand,
        _ => Tool.Rectangle,
    };

    private Tool ThemeToolSource()
    {
        if (_tool == Tool.Select && _selected != null)
            return MapDrawKindToTool(_selected.Kind);
        if (_tool == Tool.Select)
            return Tool.Rectangle;
        return _tool;
    }

    private void ApplyToolProfileFromTheme(DrawingTheme theme, Tool profileTool)
    {
        theme.EnsureToolStyles();
        var st = profileTool switch
        {
            Tool.Rectangle => theme.Rectangle!,
            Tool.Ellipse => theme.Ellipse!,
            Tool.Arrow => theme.Arrow!,
            Tool.Text => theme.Text!,
            Tool.Freehand => theme.Freehand!,
            _ => theme.Rectangle!,
        };
        _fill = ParseBrush(st.Fill);
        _stroke = ParseBrush(st.Stroke);
        _textColor = ParseBrush(st.TextColor);
        _fontFamily = new FontFamily(st.FontFamilyName);
        _fontSize = st.FontSize;
        _thickness = st.Thickness;
        _cornerRadius = st.CornerRadius;
        _freeThickness = st.FreeThickness;
        _arrowHead = Enum.TryParse<ArrowHeadStyle>(st.ArrowHead, true, out var ah) ? ah : ArrowHeadStyle.Simple;
    }

    private void ShowThemeSettings()
    {
        var dlg = new ThemeSettingsWindow(_themes)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            _themes = dlg.Themes;
            DrawingThemeStore.Save(_themes);
            RefreshThemeCombo();
        }
    }

    // ---------------- Property panel ----------------

    private void BuildPropertyPanel()
    {
        _propertyPanel.Children.Clear();

        _propertyPanel.Children.Add(SectionHeader("Fill"));
        _fillPalette = new SwatchPalette(PaletteColors, _fill, b =>
        {
            _fill = b;
            ApplyColorToSelected();
        });
        _propertyPanel.Children.Add(_fillPalette);

        _propertyPanel.Children.Add(SectionHeader("Stroke / Frame"));
        _strokePalette = new SwatchPalette(PaletteColors, _stroke, b =>
        {
            _stroke = b;
            ApplyColorToSelected();
        });
        _propertyPanel.Children.Add(_strokePalette);

        _propertyPanel.Children.Add(SectionHeader("Text color"));
        _textColorPalette = new SwatchPalette(PaletteColors, _textColor, b =>
        {
            _textColor = b;
            ApplyColorToSelected();
        });
        _propertyPanel.Children.Add(_textColorPalette);

        _propertyPanel.Children.Add(SectionHeader("Thickness"));
        _thicknessSlider = MakeSlider(1, 24, _thickness);
        _thicknessSlider.ValueChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            _thickness = _thicknessSlider!.Value;
            if (_selected != null && _selected.Kind != "freehand")
            {
                _selected.StrokeThickness = _thickness;
                Redraw();
            }
        };
        _propertyPanel.Children.Add(_thicknessSlider);

        _propertyPanel.Children.Add(SectionHeader("Corner radius (rect)"));
        _cornerSlider = MakeSlider(0, 80, _cornerRadius);
        _cornerSlider.ValueChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            _cornerRadius = _cornerSlider!.Value;
            if (_selected != null && _selected.Kind == "rect")
            {
                _selected.CornerRadius = _cornerRadius;
                Redraw();
            }
        };
        _propertyPanel.Children.Add(_cornerSlider);

        _propertyPanel.Children.Add(SectionHeader("Font"));
        _fontFamilyCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var n in FontChoices) _fontFamilyCombo.Items.Add(n);
        _fontFamilyCombo.SelectedItem = _fontFamily.Source;
        if (_fontFamilyCombo.SelectedIndex < 0) _fontFamilyCombo.SelectedIndex = 0;
        _fontFamilyCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            if (_fontFamilyCombo.SelectedItem is string s)
            {
                _fontFamily = new FontFamily(s);
                if (_selected != null && (_selected.Kind == "text" || _selected.Kind == "rect"))
                {
                    _selected.FontFamily = _fontFamily;
                    Redraw();
                }
            }
        };
        _propertyPanel.Children.Add(_fontFamilyCombo);

        _propertyPanel.Children.Add(SectionHeader("Font size"));
        _fontSizeSlider = MakeSlider(8, 96, _fontSize);
        _fontSizeSlider.ValueChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            _fontSize = _fontSizeSlider!.Value;
            if (_selected != null && (_selected.Kind == "text" || _selected.Kind == "rect"))
            {
                _selected.FontSize = _fontSize;
                Redraw();
            }
        };
        _propertyPanel.Children.Add(_fontSizeSlider);

        _propertyPanel.Children.Add(SectionHeader("Arrow style"));
        _arrowHeadCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var v in Enum.GetValues<ArrowHeadStyle>())
            _arrowHeadCombo.Items.Add(v.ToString());
        _arrowHeadCombo.SelectedIndex = (int)_arrowHead;
        _arrowHeadCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            _arrowHead = (ArrowHeadStyle)_arrowHeadCombo.SelectedIndex;
            if (_selected != null && _selected.Kind == "arrow")
            {
                _selected.ArrowHead = _arrowHead;
                Redraw();
            }
        };
        _propertyPanel.Children.Add(_arrowHeadCombo);
    }

    private void SyncPropertyControlsFromCurrent()
    {
        _suppressPropertyChanges = true;
        try
        {
            _fillPalette?.SetSelected(_fill);
            _strokePalette?.SetSelected(_stroke);
            _textColorPalette?.SetSelected(_textColor);
            if (_thicknessSlider != null) _thicknessSlider.Value = Math.Clamp(_thickness, _thicknessSlider.Minimum, _thicknessSlider.Maximum);
            if (_cornerSlider != null) _cornerSlider.Value = Math.Clamp(_cornerRadius, _cornerSlider.Minimum, _cornerSlider.Maximum);
            if (_fontSizeSlider != null) _fontSizeSlider.Value = Math.Clamp(_fontSize, _fontSizeSlider.Minimum, _fontSizeSlider.Maximum);
            if (_fontFamilyCombo != null)
            {
                var fname = _fontFamily.Source;
                if (!string.IsNullOrEmpty(fname) && _fontFamilyCombo.Items.Contains(fname))
                    _fontFamilyCombo.SelectedItem = fname;
            }
            if (_arrowHeadCombo != null)
                _arrowHeadCombo.SelectedIndex = (int)_arrowHead;
        }
        finally
        {
            _suppressPropertyChanges = false;
        }
    }

    private void SyncFromSelected()
    {
        if (_selected == null) return;
        _fill = _selected.Fill;
        _stroke = _selected.Stroke;
        _textColor = _selected.TextColor;
        _thickness = _selected.StrokeThickness;
        _cornerRadius = _selected.CornerRadius;
        _fontSize = _selected.FontSize;
        _fontFamily = _selected.FontFamily;
        if (_selected.Kind == "arrow") _arrowHead = _selected.ArrowHead;
        SyncPropertyControlsFromCurrent();
    }

    private void ApplyColorToSelected()
    {
        if (_selected == null) return;
        SnapshotForUndo();
        if (_selected.Kind == "rect" || _selected.Kind == "ellipse")
        {
            _selected.Fill = _fill;
            _selected.Stroke = _stroke;
            _selected.TextColor = _textColor;
        }
        else if (_selected.Kind == "arrow" || _selected.Kind == "freehand")
        {
            _selected.Stroke = _stroke;
        }
        else if (_selected.Kind == "text")
        {
            _selected.TextColor = _textColor;
            _selected.Stroke = _textColor;
        }
        Redraw();
    }

    // ---------------- UI helpers ----------------

    private Button MakeToolButton(string text, string tooltip, Tool tool)
    {
        var b = MakeButton(text);
        b.ToolTip = tooltip;
        b.Click += (_, _) => SelectTool(tool);
        return b;
    }

    private static Button MakeButton(string text)
        => new()
        {
            Content = text,
            Margin = new Thickness(3),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 60,
        };

    private static TextBlock SectionHeader(string text)
        => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 10, 0, 4),
        };

    private static Slider MakeSlider(double min, double max, double value)
        => new()
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 0, 0, 4),
        };

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        var brushOn = new SolidColorBrush(Color.FromRgb(0xCC, 0xE0, 0xFF));
        Brush brushOff = SystemColors.ControlBrush;
        _btnSelect.Background = tool == Tool.Select ? brushOn : brushOff;
        _btnRect.Background = tool == Tool.Rectangle ? brushOn : brushOff;
        _btnEllipse.Background = tool == Tool.Ellipse ? brushOn : brushOff;
        _btnArrow.Background = tool == Tool.Arrow ? brushOn : brushOff;
        _btnText.Background = tool == Tool.Text ? brushOn : brushOff;
        _btnFreehand.Background = tool == Tool.Freehand ? brushOn : brushOff;
        Cursor = tool == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        if (_tool == Tool.Select && _selected != null)
            SyncFromSelected();
        else
            ApplyToolProfileFromTheme(_activeTheme, _tool == Tool.Select ? Tool.Rectangle : _tool);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var hint = _tool switch
        {
            Tool.Select => "Click to select. Drag to move. Drag handles to resize. Double-click a rectangle to edit text. D to remove.",
            Tool.Rectangle => "Drag to draw a rounded rectangle.",
            Tool.Ellipse => "Drag to draw a circle/ellipse.",
            Tool.Arrow => "Drag to draw an arrow.",
            Tool.Text => "Click to place text, then type.",
            Tool.Freehand => "Drag to paint freely.",
            _ => "",
        };
        _status.Text = $"[{_tool}]  {hint}    Shortcuts: S R C A T F  D=Delete  V=Toggle panel  Ctrl+Z=Undo  Esc=cancel";
    }

    private void DrawingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.Z)
        {
            if (shift) Redo(); else Undo();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
            return;
        }

        if (_activeEditor != null) return;
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.S: SelectTool(Tool.Select); e.Handled = true; break;
            case Key.R: SelectTool(Tool.Rectangle); e.Handled = true; break;
            case Key.C: SelectTool(Tool.Ellipse); e.Handled = true; break;
            case Key.A: SelectTool(Tool.Arrow); e.Handled = true; break;
            case Key.T: SelectTool(Tool.Text); e.Handled = true; break;
            case Key.F: SelectTool(Tool.Freehand); e.Handled = true; break;
            case Key.V:
                TogglePropertiesPanel();
                e.Handled = true;
                break;
            case Key.D:
                if (_selected != null)
                {
                    DeleteSelected();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                CommitTextEdit();
                _selected = null;
                Redraw();
                e.Handled = true;
                break;
        }
    }

    private void TogglePropertiesPanel()
    {
        if (_leftScroll == null) return;
        _propertiesVisible = !_propertiesVisible;
        _leftScroll.Visibility = _propertiesVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;
        SnapshotForUndo();
        _items.Remove(_selected);
        _selected = null;
        Redraw();
    }

    // ---------------- Undo / Redo ----------------

    private List<DrawItem> CloneItems()
        => _items.Select(it => it.Clone()).ToList();

    private void SnapshotForUndo()
    {
        _redoStack.Clear();
        _undoStack.Push(CloneItems());
        while (_undoStack.Count > MaxUndoDepth)
        {
            // Stack doesn't expose remove-from-bottom; rebuild without the oldest entry.
            var arr = _undoStack.ToArray();
            _undoStack.Clear();
            for (var i = arr.Length - 2; i >= 0; i--)
                _undoStack.Push(arr[i]);
        }
    }

    private void Undo()
    {
        CommitTextEdit();
        if (_undoStack.Count == 0) return;
        _redoStack.Push(CloneItems());
        var snapshot = _undoStack.Pop();
        _items.Clear();
        _items.AddRange(snapshot);
        if (_selected != null && !_items.Contains(_selected))
            _selected = null;
        Redraw();
    }

    private void Redo()
    {
        CommitTextEdit();
        if (_redoStack.Count == 0) return;
        _undoStack.Push(CloneItems());
        var snapshot = _redoStack.Pop();
        _items.Clear();
        _items.AddRange(snapshot);
        if (_selected != null && !_items.Contains(_selected))
            _selected = null;
        Redraw();
    }

    // ---------------- Canvas interaction ----------------

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        CommitTextEdit();

        if (e.ClickCount == 2)
        {
            var hitDbl = HitTestTop(p);
            if (hitDbl != null && (hitDbl.Kind == "rect" || hitDbl.Kind == "text"))
            {
                _selected = hitDbl;
                SyncFromSelected();
                Redraw();
                StartTextEdit(hitDbl);
                e.Handled = true;
                return;
            }
        }

        _canvas.Focus();

        if (_tool == Tool.Select)
        {
            if (_selected != null)
            {
                var handle = GetHandleAt(_selected, p);
                if (handle >= 0)
                {
                    _activeHandle = handle;
                    _isMoving = false;
                    _moveLast = p;
                    _pendingUndoForGesture = true;
                    _canvas.CaptureMouse();
                    return;
                }
            }

            var hit = HitTestTop(p);
            _selected = hit;
            if (_selected != null)
            {
                SyncFromSelected();
                _activeHandle = -1;
                _isMoving = true;
                _moveLast = p;
                _pendingUndoForGesture = true;
                _canvas.CaptureMouse();
            }
            Redraw();
            return;
        }

        if (_tool == Tool.Text)
        {
            SnapshotForUndo();
            var item = new DrawItem
            {
                Kind = "text",
                P1 = p,
                P2 = new Point(p.X + 120, p.Y + (_fontSize + 8)),
                Text = "",
                FontSize = _fontSize,
                FontFamily = _fontFamily,
                TextColor = _textColor,
                Stroke = _textColor,
                StrokeThickness = _thickness,
                Fill = Brushes.Transparent,
            };
            _items.Add(item);
            _selected = item;
            Redraw();
            StartTextEdit(item, takeSnapshot: false);
            return;
        }

        SnapshotForUndo();
        _isDrawing = true;
        _drawingItem = _tool switch
        {
            Tool.Rectangle => new DrawItem
            {
                Kind = "rect",
                P1 = p, P2 = p,
                Fill = _fill, Stroke = _stroke,
                StrokeThickness = _thickness,
                CornerRadius = _cornerRadius,
                FontSize = _fontSize, FontFamily = _fontFamily, TextColor = _textColor,
            },
            Tool.Ellipse => new DrawItem
            {
                Kind = "ellipse",
                P1 = p, P2 = p,
                Fill = _fill, Stroke = _stroke,
                StrokeThickness = _thickness,
            },
            Tool.Arrow => new DrawItem
            {
                Kind = "arrow",
                P1 = p, P2 = p,
                Stroke = _stroke,
                StrokeThickness = Math.Max(1.5, _thickness),
                ArrowHead = _arrowHead,
            },
            Tool.Freehand => new DrawItem
            {
                Kind = "freehand",
                P1 = p, P2 = p,
                Stroke = _stroke,
                StrokeThickness = Math.Max(3, _freeThickness),
                Points = new List<Point> { p },
            },
            _ => null,
        };

        if (_drawingItem != null)
        {
            _items.Add(_drawingItem);
            _selected = _drawingItem;
            _canvas.CaptureMouse();
            Redraw();
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);

        if (_isDrawing && _drawingItem != null)
        {
            if (_drawingItem.Kind == "freehand")
            {
                if (_drawingItem.Points.Count == 0 || (_drawingItem.Points[^1] - p).Length > 1.0)
                    _drawingItem.Points.Add(p);
            }
            else
            {
                _drawingItem.P2 = p;
            }
            Redraw();
            return;
        }

        if (_tool == Tool.Select && _selected != null && _activeHandle >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (_pendingUndoForGesture)
            {
                SnapshotForUndo();
                _pendingUndoForGesture = false;
            }
            ResizeSelectedByHandle(_selected, _activeHandle, p);
            Redraw();
            return;
        }

        if (_tool == Tool.Select && _isMoving && _selected != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = p.X - _moveLast.X;
            var dy = p.Y - _moveLast.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 0.001)
            {
                if (_pendingUndoForGesture)
                {
                    SnapshotForUndo();
                    _pendingUndoForGesture = false;
                }
                TranslateItem(_selected, dx, dy);
                _moveLast = p;
                Redraw();
            }
        }

        if (_tool == Tool.Select && _selected != null)
        {
            var h = GetHandleAt(_selected, p);
            Cursor = h >= 0 ? CursorForHandle(_selected, h) : (HitTestTop(p) != null ? Cursors.SizeAll : Cursors.Arrow);
        }
        else if (_tool != Tool.Select)
        {
            Cursor = Cursors.Cross;
        }
        else
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();
        if (_isDrawing && _drawingItem != null)
        {
            var b = GetBounds(_drawingItem);
            if (_drawingItem.Kind != "freehand" && _drawingItem.Kind != "arrow" && (b.Width < 3 || b.Height < 3))
            {
                _items.Remove(_drawingItem);
                _selected = null;
            }
            else if (_drawingItem.Kind == "arrow")
            {
                var dx = _drawingItem.P2.X - _drawingItem.P1.X;
                var dy = _drawingItem.P2.Y - _drawingItem.P1.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 4)
                {
                    _items.Remove(_drawingItem);
                    _selected = null;
                }
            }
        }
        _isDrawing = false;
        _drawingItem = null;
        _isMoving = false;
        _activeHandle = -1;
        _pendingUndoForGesture = false;
        Redraw();
    }

    // ---------------- Bounds / hit-test / handles ----------------

    private static Rect GetBounds(DrawItem it)
    {
        switch (it.Kind)
        {
            case "rect":
            case "ellipse":
            case "arrow":
            case "text":
                return new Rect(
                    Math.Min(it.P1.X, it.P2.X),
                    Math.Min(it.P1.Y, it.P2.Y),
                    Math.Abs(it.P2.X - it.P1.X),
                    Math.Abs(it.P2.Y - it.P1.Y));
            case "freehand":
                if (it.Points.Count == 0)
                    return new Rect(it.P1, it.P1);
                var minX = it.Points.Min(p => p.X);
                var minY = it.Points.Min(p => p.Y);
                var maxX = it.Points.Max(p => p.X);
                var maxY = it.Points.Max(p => p.Y);
                return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
        return Rect.Empty;
    }

    private static void TranslateItem(DrawItem it, double dx, double dy)
    {
        it.P1 = new Point(it.P1.X + dx, it.P1.Y + dy);
        it.P2 = new Point(it.P2.X + dx, it.P2.Y + dy);
        if (it.Kind == "freehand")
        {
            for (var i = 0; i < it.Points.Count; i++)
                it.Points[i] = new Point(it.Points[i].X + dx, it.Points[i].Y + dy);
        }
    }

    private DrawItem? HitTestTop(Point p)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (HitTest(_items[i], p))
                return _items[i];
        return null;
    }

    private static bool HitTest(DrawItem it, Point p)
    {
        switch (it.Kind)
        {
            case "rect":
            case "ellipse":
            case "text":
            {
                var b = GetBounds(it);
                b.Inflate(HitPadding, HitPadding);
                return b.Contains(p);
            }
            case "arrow":
                return PointNearSegment(p, it.P1, it.P2, Math.Max(6, it.StrokeThickness + 4));
            case "freehand":
                for (var i = 1; i < it.Points.Count; i++)
                    if (PointNearSegment(p, it.Points[i - 1], it.Points[i], Math.Max(6, it.StrokeThickness + 2)))
                        return true;
                return false;
        }
        return false;
    }

    private static bool PointNearSegment(Point p, Point a, Point b, double tolerance)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 0.0001)
            return (p - a).Length <= tolerance;
        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length <= tolerance;
    }

    private static Point[] GetResizeHandles(DrawItem it)
    {
        if (it.Kind == "arrow")
            return new[] { it.P1, it.P2 };
        if (it.Kind == "text" || it.Kind == "freehand")
            return Array.Empty<Point>();
        var b = GetBounds(it);
        return new[]
        {
            new Point(b.Left, b.Top),
            new Point(b.Left + b.Width / 2, b.Top),
            new Point(b.Right, b.Top),
            new Point(b.Right, b.Top + b.Height / 2),
            new Point(b.Right, b.Bottom),
            new Point(b.Left + b.Width / 2, b.Bottom),
            new Point(b.Left, b.Bottom),
            new Point(b.Left, b.Top + b.Height / 2),
        };
    }

    private static int GetHandleAt(DrawItem it, Point p)
    {
        var handles = GetResizeHandles(it);
        for (var i = 0; i < handles.Length; i++)
        {
            var r = new Rect(handles[i].X - HandleSize, handles[i].Y - HandleSize, HandleSize * 2, HandleSize * 2);
            if (r.Contains(p))
                return i;
        }
        return -1;
    }

    private static Cursor CursorForHandle(DrawItem it, int idx)
    {
        if (it.Kind == "arrow") return Cursors.Hand;
        return idx switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            3 or 7 => Cursors.SizeWE,
            _ => Cursors.Arrow,
        };
    }

    private static void ResizeSelectedByHandle(DrawItem it, int handle, Point p)
    {
        if (it.Kind == "arrow")
        {
            if (handle == 0) it.P1 = p;
            else if (handle == 1) it.P2 = p;
            return;
        }

        var b = GetBounds(it);
        double left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
        switch (handle)
        {
            case 0: left = p.X; top = p.Y; break;
            case 1: top = p.Y; break;
            case 2: right = p.X; top = p.Y; break;
            case 3: right = p.X; break;
            case 4: right = p.X; bottom = p.Y; break;
            case 5: bottom = p.Y; break;
            case 6: left = p.X; bottom = p.Y; break;
            case 7: left = p.X; break;
        }

        if (right - left < 2) right = left + 2;
        if (bottom - top < 2) bottom = top + 2;

        it.P1 = new Point(left, top);
        it.P2 = new Point(right, bottom);
    }

    // ---------------- Rendering ----------------

    private void Redraw()
    {
        var preservedEditor = _activeEditor;
        _canvas.Children.Clear();
        _overlay.Children.Clear();
        foreach (var it in _items)
            RenderItem(it, _canvas);

        if (preservedEditor != null && _editingItem != null)
        {
            var b = GetBounds(_editingItem);
            if (_editingItem.Kind == "rect")
            {
                Canvas.SetLeft(preservedEditor, b.Left + 4);
                Canvas.SetTop(preservedEditor, b.Top + 4);
                preservedEditor.Width = Math.Max(40, b.Width - 8);
                preservedEditor.Height = Math.Max(20, b.Height - 8);
            }
            _canvas.Children.Add(preservedEditor);
        }

        if (_selected != null && _items.Contains(_selected))
            RenderSelectionAdorner(_selected);
    }

    private void RenderItem(DrawItem it, Canvas surface)
    {
        switch (it.Kind)
        {
            case "rect":
            {
                var b = GetBounds(it);
                var rect = new Rectangle
                {
                    Width = b.Width,
                    Height = b.Height,
                    Fill = it.Fill,
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    RadiusX = it.CornerRadius,
                    RadiusY = it.CornerRadius,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(rect, b.Left);
                Canvas.SetTop(rect, b.Top);
                surface.Children.Add(rect);

                if (!string.IsNullOrEmpty(it.Text))
                {
                    var tb = new TextBlock
                    {
                        Text = it.Text,
                        FontSize = it.FontSize,
                        FontFamily = it.FontFamily,
                        Foreground = it.TextColor,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Width = Math.Max(0, b.Width - 8),
                        IsHitTestVisible = false,
                    };
                    tb.Measure(new Size(b.Width, b.Height));
                    var th = tb.DesiredSize.Height;
                    Canvas.SetLeft(tb, b.Left + 4);
                    Canvas.SetTop(tb, b.Top + Math.Max(0, (b.Height - th) / 2));
                    surface.Children.Add(tb);
                }
                break;
            }
            case "ellipse":
            {
                var b = GetBounds(it);
                var el = new Ellipse
                {
                    Width = b.Width,
                    Height = b.Height,
                    Fill = it.Fill,
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(el, b.Left);
                Canvas.SetTop(el, b.Top);
                surface.Children.Add(el);
                break;
            }
            case "arrow":
            {
                var line = new Line
                {
                    X1 = it.P1.X, Y1 = it.P1.Y, X2 = it.P2.X, Y2 = it.P2.Y,
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                surface.Children.Add(line);
                AddArrowHeads(surface, it);
                break;
            }
            case "text":
            {
                if (string.IsNullOrEmpty(it.Text)) break;
                var tb = new TextBlock
                {
                    Text = it.Text,
                    FontSize = it.FontSize,
                    FontFamily = it.FontFamily,
                    Foreground = it.TextColor,
                    IsHitTestVisible = false,
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var w = tb.DesiredSize.Width;
                var h = tb.DesiredSize.Height;
                it.P2 = new Point(it.P1.X + w, it.P1.Y + h);
                Canvas.SetLeft(tb, it.P1.X);
                Canvas.SetTop(tb, it.P1.Y);
                surface.Children.Add(tb);
                break;
            }
            case "freehand":
            {
                if (it.Points.Count < 2) return;
                var poly = new Polyline
                {
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeDashCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                foreach (var pt in it.Points)
                    poly.Points.Add(pt);
                surface.Children.Add(poly);
                break;
            }
        }
    }

    private static void AddArrowHeads(Canvas surface, DrawItem it)
    {
        if (it.ArrowHead == ArrowHeadStyle.None) return;
        var headSize = Math.Max(10, it.StrokeThickness * 4);
        if (it.ArrowHead == ArrowHeadStyle.Double)
        {
            DrawHead(surface, it.P2, it.P1, it.Stroke, it.StrokeThickness, headSize);
            DrawHead(surface, it.P1, it.P2, it.Stroke, it.StrokeThickness, headSize);
            return;
        }
        DrawHead(surface, it.P1, it.P2, it.Stroke, it.StrokeThickness, headSize);
    }

    private static void DrawHead(Canvas surface, Point from, Point to, Brush stroke, double thickness, double size)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0.001) return;
        var ux = dx / len;
        var uy = dy / len;
        var angle = Math.PI / 7;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var leftX = to.X - size * (ux * cos + uy * sin);
        var leftY = to.Y - size * (uy * cos - ux * sin);
        var rightX = to.X - size * (ux * cos - uy * sin);
        var rightY = to.Y - size * (uy * cos + ux * sin);

        var l1 = new Line
        {
            X1 = to.X, Y1 = to.Y, X2 = leftX, Y2 = leftY,
            Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        var l2 = new Line
        {
            X1 = to.X, Y1 = to.Y, X2 = rightX, Y2 = rightY,
            Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        surface.Children.Add(l1);
        surface.Children.Add(l2);
    }

    private void RenderSelectionAdorner(DrawItem it)
    {
        var b = GetBounds(it);
        var accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));

        if (it.Kind == "text" || it.Kind == "freehand")
        {
            var underline = new Line
            {
                X1 = b.Left,
                Y1 = b.Bottom + 1,
                X2 = b.Right,
                Y2 = b.Bottom + 1,
                Stroke = accent,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false,
            };
            _overlay.Children.Add(underline);
            return;
        }

        if (it.Kind != "arrow")
        {
            var box = new Rectangle
            {
                Width = Math.Max(0, b.Width + 4),
                Height = Math.Max(0, b.Height + 4),
                Stroke = accent,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(box, b.Left - 2);
            Canvas.SetTop(box, b.Top - 2);
            _overlay.Children.Add(box);
        }

        var handles = GetResizeHandles(it);
        foreach (var h in handles)
        {
            var hr = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = accent,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(hr, h.X - HandleSize / 2);
            Canvas.SetTop(hr, h.Y - HandleSize / 2);
            _overlay.Children.Add(hr);
        }
    }

    // ---------------- Text editor ----------------

    private void StartTextEdit(DrawItem it, bool takeSnapshot = true)
    {
        CommitTextEdit();
        if (takeSnapshot)
            SnapshotForUndo();
        _editChangedAnything = false;
        _editingItem = it;
        var b = GetBounds(it);
        var isFreshTextItem = it.Kind == "text" && string.IsNullOrEmpty(it.Text);

        Brush boxBackground;
        Brush boxBorder;
        Thickness boxBorderThickness;
        if (it.Kind == "rect")
        {
            boxBackground = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF));
            boxBorder = Brushes.Transparent;
            boxBorderThickness = new Thickness(0);
        }
        else if (isFreshTextItem)
        {
            boxBackground = Brushes.Transparent;
            boxBorder = Brushes.Transparent;
            boxBorderThickness = new Thickness(0);
        }
        else
        {
            boxBackground = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
            boxBorder = new SolidColorBrush(Color.FromArgb(0xAA, 0x21, 0x96, 0xF3));
            boxBorderThickness = new Thickness(1);
        }

        var box = new TextBox
        {
            Text = it.Text ?? "",
            FontSize = it.FontSize,
            FontFamily = it.FontFamily,
            Foreground = it.TextColor,
            Background = boxBackground,
            BorderBrush = boxBorder,
            BorderThickness = boxBorderThickness,
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = it.Kind == "rect" ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalContentAlignment = it.Kind == "rect" ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalContentAlignment = it.Kind == "rect" ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
        double left, top, w, h;
        if (it.Kind == "rect")
        {
            left = b.Left + 4;
            top = b.Top + 4;
            w = Math.Max(40, b.Width - 8);
            h = Math.Max(20, b.Height - 8);
        }
        else if (isFreshTextItem)
        {
            left = it.P1.X;
            top = it.P1.Y;
            w = 480;
            h = Math.Max(it.FontSize * 1.6, 40);
        }
        else
        {
            left = it.P1.X - 2;
            top = it.P1.Y - 2;
            w = Math.Max(160, b.Width + 24);
            h = Math.Max(it.FontSize + 12, b.Height + 8);
        }
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        box.Width = w;
        box.Height = h;

        var originalText = it.Text ?? "";

        void ResizeForContent()
        {
            var text = box.Text ?? "";
            var typeface = new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch);
            var dpi = VisualTreeHelper.GetDpi(box).PixelsPerDip;

            if (it.Kind == "rect")
            {
                var availWidth = Math.Max(40, GetBounds(it).Width - 8);
                var ft = new FormattedText(
                    string.IsNullOrEmpty(text) ? " " : text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, box.FontSize, Brushes.Black, dpi)
                {
                    MaxTextWidth = availWidth,
                };
                var neededRectHeight = ft.Height + 16;
                var currentRectHeight = GetBounds(it).Height;
                if (neededRectHeight > currentRectHeight)
                {
                    it.P2 = new Point(it.P2.X, it.P1.Y + neededRectHeight);
                    _editChangedAnything = true;
                    Redraw();
                }
            }
            else if (it.Kind == "text")
            {
                var lines = text.Length == 0 ? new[] { "" } : text.Split('\n');
                double maxLineWidth = 0;
                foreach (var line in lines)
                {
                    var ft = new FormattedText(
                        string.IsNullOrEmpty(line) ? " " : line,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface, box.FontSize, Brushes.Black, dpi);
                    if (ft.Width > maxLineWidth) maxLineWidth = ft.Width;
                }
                var minWidth = isFreshTextItem ? 480 : 200;
                var newW = Math.Max(minWidth, maxLineWidth + 24);
                var newH = Math.Max(box.FontSize * 1.6, lines.Length * box.FontSize * 1.4 + 12);
                box.Width = newW;
                box.Height = newH;
            }
        }

        box.TextChanged += (_, _) =>
        {
            if (!string.Equals(box.Text, originalText, StringComparison.Ordinal))
                _editChangedAnything = true;
            ResizeForContent();
        };

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CommitTextEdit(); e.Handled = true; }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            { CommitTextEdit(); e.Handled = true; }
        };
        var sawFocus = false;
        box.GotKeyboardFocus += (_, _) => sawFocus = true;
        box.LostKeyboardFocus += (_, _) =>
        {
            if (sawFocus) CommitTextEdit();
        };

        _canvas.Children.Add(box);
        _activeEditor = box;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_activeEditor, box)) return;
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex = box.Text?.Length ?? 0;
            if (!string.IsNullOrEmpty(box.Text)) box.SelectAll();
            ResizeForContent();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitTextEdit()
    {
        if (_activeEditor == null || _editingItem == null) return;
        var newText = _activeEditor.Text ?? "";
        if (!string.Equals(_editingItem.Text, newText, StringComparison.Ordinal))
            _editChangedAnything = true;
        _editingItem.Text = newText;
        var editor = _activeEditor;
        var item = _editingItem;
        _activeEditor = null;
        _editingItem = null;
        _canvas.Children.Remove(editor);

        if (item.Kind == "text" && string.IsNullOrWhiteSpace(item.Text))
        {
            _items.Remove(item);
            if (ReferenceEquals(_selected, item)) _selected = null;
        }

        if (!_editChangedAnything && _undoStack.Count > 0)
            _undoStack.Pop();
        _editChangedAnything = false;

        Redraw();
    }

    // ---------------- Save / utils ----------------

    public static Brush ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            return Brushes.Transparent;
        try
        {
            var obj = ColorConverter.ConvertFromString(value);
            if (obj is Color c) return new SolidColorBrush(c);
        }
        catch
        {
            // fall through
        }
        return Brushes.Black;
    }

    public static string BrushToHex(Brush brush)
    {
        if (brush is SolidColorBrush scb)
        {
            if (scb.Color.A == 0) return "Transparent";
            return scb.Color.A == 255
                ? $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}"
                : $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
        }
        return "#000000";
    }

    private void SavePng()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            FileName = "drawing.png",
        };
        if (dlg.ShowDialog(this) != true) return;

        var prevSelected = _selected;
        _selected = null;
        Redraw();
        try
        {
            var size = new Size(_canvas.Width, _canvas.Height);
            _canvas.Measure(size);
            _canvas.Arrange(new Rect(size));
            var rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.OpenWrite(dlg.FileName);
            encoder.Save(fs);
            _status.Text = $"Saved: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _selected = prevSelected;
            Redraw();
        }
    }
}

// ---------------- Color swatch palette ----------------

internal sealed class SwatchPalette : Border
{
    private readonly UniformGrid _grid;
    private readonly Action<Brush> _onChanged;
    private readonly Color[] _colors;
    private Border? _selected;

    public SwatchPalette(Color[] colors, Brush initial, Action<Brush> onChanged)
    {
        _colors = colors;
        _onChanged = onChanged;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Margin = new Thickness(0, 0, 0, 4);
        _grid = new UniformGrid { Columns = 4, Margin = new Thickness(0) };
        foreach (var c in colors)
        {
            var swatch = MakeSwatch(c);
            _grid.Children.Add(swatch);
        }
        Child = _grid;
        SetSelected(initial);
    }

    private Border MakeSwatch(Color color)
    {
        Brush background = color == Colors.Transparent ? CreateCheckeredBrush() : new SolidColorBrush(color);
        if (background is Freezable f) f.Freeze();
        var actualBrush = color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

        var b = new Border
        {
            Width = 32,
            Height = 26,
            Margin = new Thickness(2),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Background = background,
            Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(3),
            Tag = actualBrush,
        };
        b.MouseLeftButtonDown += (_, _) =>
        {
            SetSelectedSwatch(b);
            _onChanged(actualBrush);
        };
        return b;
    }

    private void SetSelectedSwatch(Border b)
    {
        if (_selected != null)
        {
            _selected.BorderBrush = Brushes.DimGray;
            _selected.BorderThickness = new Thickness(1);
        }
        _selected = b;
        _selected.BorderBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        _selected.BorderThickness = new Thickness(2);
    }

    public void SetSelected(Brush brush)
    {
        var hex = DrawingWindow.BrushToHex(brush);
        foreach (var child in _grid.Children)
        {
            if (child is Border b && b.Tag is Brush tagBrush)
            {
                if (string.Equals(DrawingWindow.BrushToHex(tagBrush), hex, StringComparison.OrdinalIgnoreCase))
                {
                    SetSelectedSwatch(b);
                    return;
                }
            }
        }
    }

    private static DrawingBrush CreateCheckeredBrush()
    {
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 8, 8));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, 4, 4));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(4, 4, 4, 4));
        }
        return new DrawingBrush
        {
            Drawing = dg,
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 8, 8),
        };
    }
}

// ---------------- Theme settings dialog ----------------

internal sealed class ThemeSettingsWindow : Window
{
    public List<DrawingTheme> Themes { get; private set; }

    private readonly ListBox _list;
    private readonly StackPanel _editor;
    private DrawingTheme? _editing;
    private bool _suppressUpdate;

    private TextBox? _nameBox;

    private ComboBox? _rFill, _rStroke, _rTextColor, _rFont;
    private TextBox? _rFontSize, _rThickness, _rCorner;

    private ComboBox? _eFill, _eStroke;
    private TextBox? _eThickness;

    private ComboBox? _aStroke, _aArrow;
    private TextBox? _aThickness;

    private ComboBox? _tTextColor, _tFont;
    private TextBox? _tFontSize, _tThickness;

    private ComboBox? _fStroke;
    private TextBox? _fFreeThickness;

    private static readonly string[] ColorChoices =
    {
        "Transparent",
        "#000000", "#FFFFFF", "#555555", "#BBBBBB",
        "#E53935", "#F68A1E", "#FFD700", "#FFF6A9",
        "#4CAF50", "#A8E6A1", "#2196F3", "#B3E5FC",
        "#673AB7", "#D1C4E9", "#795548", "#3F51B5",
    };

    private static readonly string[] FontChoices =
    {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Consolas", "Courier New",
        "Georgia", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
        "Comic Sans MS", "Impact",
    };

    public ThemeSettingsWindow(List<DrawingTheme> existing)
    {
        Themes = existing.Select(t => t.Clone()).ToList();
        Title = "Drawing Themes";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var btnOk = new Button { Content = "Save", Width = 90, IsDefault = true, Margin = new Thickness(4, 0, 4, 0) };
        var btnCancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        btnOk.Click += (_, _) =>
        {
            CommitEditor();
            DialogResult = true;
            Close();
        };
        bottom.Children.Add(btnOk);
        bottom.Children.Add(btnCancel);
        root.Children.Add(bottom);

        // Left: list + add/remove
        var leftDock = new DockPanel { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(leftDock, Dock.Left);

        var listButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(listButtons, Dock.Bottom);
        var btnAdd = new Button { Content = "Add", Margin = new Thickness(0, 6, 4, 0), Padding = new Thickness(6, 2, 6, 2) };
        var btnRemove = new Button { Content = "Remove", Margin = new Thickness(0, 6, 4, 0), Padding = new Thickness(6, 2, 6, 2) };
        var btnDuplicate = new Button { Content = "Duplicate", Margin = new Thickness(0, 6, 4, 0), Padding = new Thickness(6, 2, 6, 2) };
        listButtons.Children.Add(btnAdd);
        listButtons.Children.Add(btnDuplicate);
        listButtons.Children.Add(btnRemove);
        leftDock.Children.Add(listButtons);

        _list = new ListBox { DisplayMemberPath = "Name" };
        _list.ItemsSource = Themes;
        _list.SelectionChanged += (_, _) =>
        {
            CommitEditor();
            _editing = _list.SelectedItem as DrawingTheme;
            LoadEditor();
        };
        leftDock.Children.Add(_list);
        root.Children.Add(leftDock);

        btnAdd.Click += (_, _) =>
        {
            CommitEditor();
            var theme = new DrawingTheme { Name = "New theme" };
            theme.EnsureToolStyles();
            Themes.Add(theme);
            _list.ItemsSource = null;
            _list.ItemsSource = Themes;
            _list.SelectedItem = theme;
        };
        btnDuplicate.Click += (_, _) =>
        {
            CommitEditor();
            if (_list.SelectedItem is DrawingTheme t)
            {
                var copy = t.Clone();
                copy.Name = t.Name + " copy";
                Themes.Add(copy);
                _list.ItemsSource = null;
                _list.ItemsSource = Themes;
                _list.SelectedItem = copy;
            }
        };
        btnRemove.Click += (_, _) =>
        {
            if (_list.SelectedItem is DrawingTheme t && Themes.Count > 1)
            {
                Themes.Remove(t);
                _list.ItemsSource = null;
                _list.ItemsSource = Themes;
                _list.SelectedIndex = 0;
            }
        };

        // Right: editor
        var editorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _editor = new StackPanel();
        editorScroll.Content = _editor;
        root.Children.Add(editorScroll);

        BuildEditor();
        if (Themes.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        Content = root;
    }

    private void BuildEditor()
    {
        _editor.Children.Add(EditorLabel("Name"));
        _nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.Name = _nameBox.Text;
            _list.Items.Refresh();
        };
        _editor.Children.Add(_nameBox);

        var tabs = new TabControl { MinHeight = 380, Margin = new Thickness(0, 4, 0, 0) };

        var spR = new StackPanel { Margin = new Thickness(8) };
        spR.Children.Add(EditorLabel("Fill"));
        _rFill = MakeColorCombo();
        spR.Children.Add(_rFill);
        spR.Children.Add(EditorLabel("Stroke / frame"));
        _rStroke = MakeColorCombo();
        spR.Children.Add(_rStroke);
        spR.Children.Add(EditorLabel("Text color (rectangle label)"));
        _rTextColor = MakeColorCombo();
        spR.Children.Add(_rTextColor);
        spR.Children.Add(EditorLabel("Font family"));
        _rFont = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var n in FontChoices) _rFont.Items.Add(n);
        spR.Children.Add(_rFont);
        spR.Children.Add(EditorLabel("Font size"));
        _rFontSize = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spR.Children.Add(_rFontSize);
        spR.Children.Add(EditorLabel("Stroke thickness"));
        _rThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spR.Children.Add(_rThickness);
        spR.Children.Add(EditorLabel("Corner radius"));
        _rCorner = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spR.Children.Add(_rCorner);
        tabs.Items.Add(new TabItem { Header = "Rectangle", Content = spR });

        var spE = new StackPanel { Margin = new Thickness(8) };
        spE.Children.Add(EditorLabel("Fill"));
        _eFill = MakeColorCombo();
        spE.Children.Add(_eFill);
        spE.Children.Add(EditorLabel("Stroke / frame"));
        _eStroke = MakeColorCombo();
        spE.Children.Add(_eStroke);
        spE.Children.Add(EditorLabel("Stroke thickness"));
        _eThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spE.Children.Add(_eThickness);
        tabs.Items.Add(new TabItem { Header = "Circle", Content = spE });

        var spA = new StackPanel { Margin = new Thickness(8) };
        spA.Children.Add(EditorLabel("Stroke"));
        _aStroke = MakeColorCombo();
        spA.Children.Add(_aStroke);
        spA.Children.Add(EditorLabel("Thickness"));
        _aThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spA.Children.Add(_aThickness);
        spA.Children.Add(EditorLabel("Arrow head"));
        _aArrow = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _aArrow.Items.Add("None");
        _aArrow.Items.Add("Simple");
        _aArrow.Items.Add("Double");
        spA.Children.Add(_aArrow);
        tabs.Items.Add(new TabItem { Header = "Arrow", Content = spA });

        var spT = new StackPanel { Margin = new Thickness(8) };
        spT.Children.Add(EditorLabel("Text color"));
        _tTextColor = MakeColorCombo();
        spT.Children.Add(_tTextColor);
        spT.Children.Add(EditorLabel("Font family"));
        _tFont = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var n in FontChoices) _tFont.Items.Add(n);
        spT.Children.Add(_tFont);
        spT.Children.Add(EditorLabel("Font size"));
        _tFontSize = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spT.Children.Add(_tFontSize);
        spT.Children.Add(EditorLabel("Thickness (outline)"));
        _tThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spT.Children.Add(_tThickness);
        tabs.Items.Add(new TabItem { Header = "Text", Content = spT });

        var spF = new StackPanel { Margin = new Thickness(8) };
        spF.Children.Add(EditorLabel("Stroke"));
        _fStroke = MakeColorCombo();
        spF.Children.Add(_fStroke);
        spF.Children.Add(EditorLabel("Free draw thickness"));
        _fFreeThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        spF.Children.Add(_fFreeThickness);
        tabs.Items.Add(new TabItem { Header = "Freeform", Content = spF });

        _editor.Children.Add(tabs);

        void R(Action<ThemeToolStyle> set)
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.EnsureToolStyles();
            set(_editing.Rectangle!);
        }

        void E(Action<ThemeToolStyle> set)
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.EnsureToolStyles();
            set(_editing.Ellipse!);
        }

        void A(Action<ThemeToolStyle> set)
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.EnsureToolStyles();
            set(_editing.Arrow!);
        }

        void Tx(Action<ThemeToolStyle> set)
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.EnsureToolStyles();
            set(_editing.Text!);
        }

        void F(Action<ThemeToolStyle> set)
        {
            if (_suppressUpdate || _editing == null) return;
            _editing.EnsureToolStyles();
            set(_editing.Freehand!);
        }

        _rFill!.SelectionChanged += (_, _) => R(s => s.Fill = ColorString(_rFill));
        _rStroke!.SelectionChanged += (_, _) => R(s => s.Stroke = ColorString(_rStroke));
        _rTextColor!.SelectionChanged += (_, _) => R(s => s.TextColor = ColorString(_rTextColor));
        _rFont!.SelectionChanged += (_, _) => R(s => s.FontFamilyName = _rFont.SelectedItem as string ?? s.FontFamilyName);
        _rFontSize!.TextChanged += (_, _) => R(s => { if (double.TryParse(_rFontSize.Text, out var v)) s.FontSize = Math.Max(4, v); });
        _rThickness!.TextChanged += (_, _) => R(s => { if (double.TryParse(_rThickness.Text, out var v)) s.Thickness = Math.Max(0.5, v); });
        _rCorner!.TextChanged += (_, _) => R(s => { if (double.TryParse(_rCorner.Text, out var v)) s.CornerRadius = Math.Max(0, v); });

        _eFill!.SelectionChanged += (_, _) => E(s => s.Fill = ColorString(_eFill));
        _eStroke!.SelectionChanged += (_, _) => E(s => s.Stroke = ColorString(_eStroke));
        _eThickness!.TextChanged += (_, _) => E(s => { if (double.TryParse(_eThickness.Text, out var v)) s.Thickness = Math.Max(0.5, v); });

        _aStroke!.SelectionChanged += (_, _) => A(s => s.Stroke = ColorString(_aStroke));
        _aThickness!.TextChanged += (_, _) => A(s => { if (double.TryParse(_aThickness.Text, out var v)) s.Thickness = Math.Max(0.5, v); });
        _aArrow!.SelectionChanged += (_, _) => A(s => s.ArrowHead = _aArrow.SelectedItem as string ?? "Simple");

        _tTextColor!.SelectionChanged += (_, _) => Tx(s => s.TextColor = ColorString(_tTextColor));
        _tFont!.SelectionChanged += (_, _) => Tx(s => s.FontFamilyName = _tFont.SelectedItem as string ?? s.FontFamilyName);
        _tFontSize!.TextChanged += (_, _) => Tx(s => { if (double.TryParse(_tFontSize.Text, out var v)) s.FontSize = Math.Max(4, v); });
        _tThickness!.TextChanged += (_, _) => Tx(s => { if (double.TryParse(_tThickness.Text, out var v)) s.Thickness = Math.Max(0.5, v); });

        _fStroke!.SelectionChanged += (_, _) => F(s => s.Stroke = ColorString(_fStroke));
        _fFreeThickness!.TextChanged += (_, _) => F(s => { if (double.TryParse(_fFreeThickness.Text, out var v)) s.FreeThickness = Math.Max(1, v); });
    }

    private static string ColorString(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem item && item.Tag is string s) return s;
        if (cb.SelectedItem is string s2) return s2;
        return "#000000";
    }

    private ComboBox MakeColorCombo()
    {
        var cb = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var hex in ColorChoices)
        {
            var item = new ComboBoxItem { Tag = hex };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var sw = new Border
            {
                Width = 20,
                Height = 14,
                Margin = new Thickness(0, 0, 6, 0),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Background = DrawingWindow.ParseBrush(hex),
            };
            sp.Children.Add(sw);
            sp.Children.Add(new TextBlock { Text = hex, VerticalAlignment = VerticalAlignment.Center });
            item.Content = sp;
            cb.Items.Add(item);
        }
        return cb;
    }

    private void LoadEditor()
    {
        _suppressUpdate = true;
        try
        {
            if (_editing == null)
            {
                _nameBox!.Text = "";
                return;
            }
            _nameBox!.Text = _editing.Name;
            _editing.EnsureToolStyles();
            var r = _editing.Rectangle!;
            var e = _editing.Ellipse!;
            var a = _editing.Arrow!;
            var tx = _editing.Text!;
            var fh = _editing.Freehand!;
            SelectColor(_rFill!, r.Fill);
            SelectColor(_rStroke!, r.Stroke);
            SelectColor(_rTextColor!, r.TextColor);
            _rFont!.SelectedItem = r.FontFamilyName;
            if (_rFont.SelectedItem == null) _rFont.SelectedIndex = 0;
            _rFontSize!.Text = r.FontSize.ToString();
            _rThickness!.Text = r.Thickness.ToString();
            _rCorner!.Text = r.CornerRadius.ToString();

            SelectColor(_eFill!, e.Fill);
            SelectColor(_eStroke!, e.Stroke);
            _eThickness!.Text = e.Thickness.ToString();

            SelectColor(_aStroke!, a.Stroke);
            _aThickness!.Text = a.Thickness.ToString();
            _aArrow!.SelectedItem = a.ArrowHead;
            if (_aArrow.SelectedItem == null) _aArrow.SelectedIndex = 1;

            SelectColor(_tTextColor!, tx.TextColor);
            _tFont!.SelectedItem = tx.FontFamilyName;
            if (_tFont.SelectedItem == null) _tFont.SelectedIndex = 0;
            _tFontSize!.Text = tx.FontSize.ToString();
            _tThickness!.Text = tx.Thickness.ToString();

            SelectColor(_fStroke!, fh.Stroke);
            _fFreeThickness!.Text = fh.FreeThickness.ToString();
        }
        finally
        {
            _suppressUpdate = false;
        }
    }

    private void SelectColor(ComboBox cb, string value)
    {
        foreach (var obj in cb.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string s
                && string.Equals(s, value, StringComparison.OrdinalIgnoreCase))
            {
                cb.SelectedItem = item;
                return;
            }
        }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private void CommitEditor()
    {
        // Edits are applied live to the bound _editing instance; nothing extra needed.
    }

    private static TextBlock EditorLabel(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(0, 2, 0, 2),
    };
}
