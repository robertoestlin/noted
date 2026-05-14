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

internal sealed class DrawingTheme
{
    public string Name { get; set; } = "Default";
    public string Fill { get; set; } = "Transparent";
    public string Stroke { get; set; } = "#000000";
    public string TextColor { get; set; } = "#000000";
    public string FontFamilyName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 22;
    public double Thickness { get; set; } = 2;
    public double CornerRadius { get; set; } = 14;
    public string ArrowHead { get; set; } = "Simple";
    public double FreeThickness { get; set; } = 6;

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
    private List<DrawingTheme> _themes;
    private DrawingTheme _activeTheme;

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

    private TextBox? _activeEditor;
    private DrawItem? _editingItem;

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
        _btnSelect = MakeToolButton("Select (S)", Tool.Select);
        _btnRect = MakeToolButton("Rect (R)", Tool.Rectangle);
        _btnEllipse = MakeToolButton("Circle (C)", Tool.Ellipse);
        _btnArrow = MakeToolButton("Arrow (A)", Tool.Arrow);
        _btnText = MakeToolButton("Text (T)", Tool.Text);
        _btnFreehand = MakeToolButton("Free (F)", Tool.Freehand);
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

        var btnDelete = MakeButton("Delete (D)");
        btnDelete.Click += (_, _) => DeleteSelected();
        actions.Children.Add(btnDelete);

        var btnClear = MakeButton("Clear");
        btnClear.Click += (_, _) =>
        {
            if (_items.Count == 0) return;
            if (MessageBox.Show(this, "Clear the whole canvas?", "Drawing",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            {
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
        var leftScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 230,
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
        };
        DockPanel.SetDock(leftScroll, Dock.Left);
        _propertyPanel = new StackPanel { Margin = new Thickness(10) };
        leftScroll.Content = _propertyPanel;
        BuildPropertyPanel();
        root.Children.Add(leftScroll);

        // ---- Center canvas ----
        var center = new Grid { Background = Brushes.LightGray };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.LightGray,
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
        _fill = ParseBrush(theme.Fill);
        _stroke = ParseBrush(theme.Stroke);
        _textColor = ParseBrush(theme.TextColor);
        _fontFamily = new FontFamily(theme.FontFamilyName);
        _fontSize = theme.FontSize;
        _thickness = theme.Thickness;
        _cornerRadius = theme.CornerRadius;
        _freeThickness = theme.FreeThickness;
        _arrowHead = Enum.TryParse<ArrowHeadStyle>(theme.ArrowHead, true, out var ah) ? ah : ArrowHeadStyle.Simple;

        if (_propertyPanel != null)
            SyncPropertyControlsFromCurrent();
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

    private Button MakeToolButton(string text, Tool tool)
    {
        var b = MakeButton(text);
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
        _status.Text = $"[{_tool}]  {hint}    Shortcuts: S=Select  R=Rect  C=Circle  A=Arrow  T=Text  F=Free  D=Delete  Esc=cancel/deselect";
    }

    private void DrawingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    private void DeleteSelected()
    {
        if (_selected == null) return;
        _items.Remove(_selected);
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
                _canvas.CaptureMouse();
            }
            Redraw();
            return;
        }

        if (_tool == Tool.Text)
        {
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
            StartTextEdit(item);
            return;
        }

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
            ResizeSelectedByHandle(_selected, _activeHandle, p);
            Redraw();
            return;
        }

        if (_tool == Tool.Select && _isMoving && _selected != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = p.X - _moveLast.X;
            var dy = p.Y - _moveLast.Y;
            TranslateItem(_selected, dx, dy);
            _moveLast = p;
            Redraw();
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

    private void StartTextEdit(DrawItem it)
    {
        CommitTextEdit();
        _editingItem = it;
        var b = GetBounds(it);

        var box = new TextBox
        {
            Text = it.Text ?? "",
            FontSize = it.FontSize,
            FontFamily = it.FontFamily,
            Foreground = it.TextColor,
            Background = it.Kind == "rect" ? new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent,
            BorderBrush = it.Kind == "rect" ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x55, 0x21, 0x96, 0xF3)),
            BorderThickness = it.Kind == "rect" ? new Thickness(0) : new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
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
        else
        {
            left = it.P1.X;
            top = it.P1.Y;
            w = Math.Max(120, b.Width);
            h = Math.Max(it.FontSize + 8, b.Height);
        }
        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        box.Width = w;
        box.Height = h;

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
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CommitTextEdit()
    {
        if (_activeEditor == null || _editingItem == null) return;
        _editingItem.Text = _activeEditor.Text ?? "";
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
    private ComboBox? _fillCombo;
    private ComboBox? _strokeCombo;
    private ComboBox? _textColorCombo;
    private ComboBox? _fontCombo;
    private TextBox? _fontSizeBox;
    private TextBox? _thicknessBox;
    private TextBox? _cornerBox;
    private ComboBox? _arrowCombo;
    private TextBox? _freeBox;

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
            var sel = _list.SelectedIndex;
            _list.ItemsSource = null;
            _list.ItemsSource = Themes;
            _list.SelectedIndex = sel;
        };
        _editor.Children.Add(_nameBox);

        _editor.Children.Add(EditorLabel("Fill color"));
        _fillCombo = MakeColorCombo(); _editor.Children.Add(_fillCombo);
        _editor.Children.Add(EditorLabel("Stroke / frame color"));
        _strokeCombo = MakeColorCombo(); _editor.Children.Add(_strokeCombo);
        _editor.Children.Add(EditorLabel("Text color"));
        _textColorCombo = MakeColorCombo(); _editor.Children.Add(_textColorCombo);

        _editor.Children.Add(EditorLabel("Font family"));
        _fontCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var n in FontChoices) _fontCombo.Items.Add(n);
        _editor.Children.Add(_fontCombo);

        _editor.Children.Add(EditorLabel("Font size"));
        _fontSizeBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _editor.Children.Add(_fontSizeBox);

        _editor.Children.Add(EditorLabel("Stroke thickness"));
        _thicknessBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _editor.Children.Add(_thicknessBox);

        _editor.Children.Add(EditorLabel("Free draw thickness"));
        _freeBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _editor.Children.Add(_freeBox);

        _editor.Children.Add(EditorLabel("Rectangle corner radius"));
        _cornerBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _editor.Children.Add(_cornerBox);

        _editor.Children.Add(EditorLabel("Arrow head"));
        _arrowCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _arrowCombo.Items.Add("None");
        _arrowCombo.Items.Add("Simple");
        _arrowCombo.Items.Add("Double");
        _editor.Children.Add(_arrowCombo);

        _fillCombo.SelectionChanged += (_, _) => Sync(c => c.Fill = ColorString(_fillCombo!));
        _strokeCombo.SelectionChanged += (_, _) => Sync(c => c.Stroke = ColorString(_strokeCombo!));
        _textColorCombo.SelectionChanged += (_, _) => Sync(c => c.TextColor = ColorString(_textColorCombo!));
        _fontCombo.SelectionChanged += (_, _) => Sync(c => c.FontFamilyName = _fontCombo!.SelectedItem as string ?? c.FontFamilyName);
        _fontSizeBox.TextChanged += (_, _) => Sync(c => { if (double.TryParse(_fontSizeBox!.Text, out var v)) c.FontSize = Math.Max(4, v); });
        _thicknessBox.TextChanged += (_, _) => Sync(c => { if (double.TryParse(_thicknessBox!.Text, out var v)) c.Thickness = Math.Max(0.5, v); });
        _freeBox.TextChanged += (_, _) => Sync(c => { if (double.TryParse(_freeBox!.Text, out var v)) c.FreeThickness = Math.Max(1, v); });
        _cornerBox.TextChanged += (_, _) => Sync(c => { if (double.TryParse(_cornerBox!.Text, out var v)) c.CornerRadius = Math.Max(0, v); });
        _arrowCombo.SelectionChanged += (_, _) => Sync(c => c.ArrowHead = _arrowCombo!.SelectedItem as string ?? "Simple");
    }

    private void Sync(Action<DrawingTheme> apply)
    {
        if (_suppressUpdate || _editing == null) return;
        apply(_editing);
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
            SelectColor(_fillCombo!, _editing.Fill);
            SelectColor(_strokeCombo!, _editing.Stroke);
            SelectColor(_textColorCombo!, _editing.TextColor);
            _fontCombo!.SelectedItem = _editing.FontFamilyName;
            if (_fontCombo.SelectedItem == null) _fontCombo.SelectedIndex = 0;
            _fontSizeBox!.Text = _editing.FontSize.ToString();
            _thicknessBox!.Text = _editing.Thickness.ToString();
            _freeBox!.Text = _editing.FreeThickness.ToString();
            _cornerBox!.Text = _editing.CornerRadius.ToString();
            _arrowCombo!.SelectedItem = _editing.ArrowHead;
            if (_arrowCombo.SelectedItem == null) _arrowCombo.SelectedIndex = 1;
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
