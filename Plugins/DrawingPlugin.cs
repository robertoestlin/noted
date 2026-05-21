using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Noted.Models;
using Noted.Services;
using Path = System.IO.Path;

namespace Noted;

public partial class MainWindow
{
    private const string DrawingsSubfolderName = "drawings";
    private const string DrawingFileNamePrefix = "drawing-";

    private string? _lastDrawingThemeName;

    internal const int DefaultDrawingRectangleWidth = 150;
    internal const int DefaultDrawingRectangleHeight = 75;
    internal const int DefaultDrawingEllipseWidth = 58;
    internal const int DefaultDrawingEllipseHeight = 58;
    internal const int DefaultDrawingShapeSizeSnapThresholdPx = 12;
    internal const int MinDrawingShapeSizePx = 8;
    internal const int MaxDrawingShapeSizePx = 4096;

    private int _drawingDefaultRectangleWidth = DefaultDrawingRectangleWidth;
    private int _drawingDefaultRectangleHeight = DefaultDrawingRectangleHeight;
    private int _drawingDefaultEllipseWidth = DefaultDrawingEllipseWidth;
    private int _drawingDefaultEllipseHeight = DefaultDrawingEllipseHeight;
    private int _drawingShapeSizeSnapThresholdPx = DefaultDrawingShapeSizeSnapThresholdPx;

    internal void GetDrawingShapeDefaults(string kind, out double standardWidth, out double standardHeight)
    {
        if (kind == "rect")
        {
            standardWidth = _drawingDefaultRectangleWidth;
            standardHeight = _drawingDefaultRectangleHeight;
            return;
        }
        standardWidth = _drawingDefaultEllipseWidth;
        standardHeight = _drawingDefaultEllipseHeight;
    }

    internal int DrawingShapeSizeSnapThresholdPx => _drawingShapeSizeSnapThresholdPx;

    private void ShowDrawingDialog()
    {
        var dlg = new DrawingWindow(this, _lastDrawingThemeName) { Owner = this };
        dlg.Show();
    }

    private void ShowDrawingEditDialog(DrawingEditContext context, DrawingWorkspaceDto workspace)
    {
        var dlg = new DrawingWindow(this, _lastDrawingThemeName, workspace, context) { Owner = this };
        dlg.Show();
    }

    /// <summary>Returns the active <see cref="TabDocument"/> for the current app mode (short-term tab, long-term
    /// page, or documentation page). Used by the drawing dialog to decide where to insert.</summary>
    private TabDocument? GetActiveEditorTabDocument()
    {
        return _appMode switch
        {
            AppMode.Documentation =>
                _docCurrentNode != null && _docNodeDocs.TryGetValue(_docCurrentNode.Id, out var d) ? d : null,
            AppMode.LongTerm => GetActiveLongTermTabDocument(),
            _ => CurrentDoc(),
        };
    }

    private string GetDrawingsFolderPath()
        => Path.Combine(GetBackupImagesFolderPath(), DrawingsSubfolderName);

    /// <summary>Called by <see cref="DrawingWindow"/> when the user clicks "Insert into editor" or "Save new version".
    /// Writes the PNG (to the images folder or doc package zip) and the workspace JSON (to a sibling drawings folder
    /// or doc package zip), then inserts/updates the image marker in the active editor.</summary>
    internal bool InsertDrawingIntoActiveEditor(byte[] pngBytes, string workspaceJson,
        DrawingEditContext? editContext)
    {
        var doc = editContext?.Doc ?? GetActiveEditorTabDocument();
        if (doc == null)
        {
            MessageBox.Show(this,
                "Open a tab or page before inserting a drawing.",
                "Insert drawing", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string baseName;
        if (editContext != null)
        {
            baseName = editContext.BaseName;
        }
        else
        {
            baseName = DrawingFileNamePrefix + DateTime.Now.ToString("yyyy-MM-dd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        bool isDocPackage = editContext != null
            ? !string.IsNullOrEmpty(editContext.PackageId)
            : TryGetDocPackageIdForEditor(doc.Editor, out _);

        try
        {
            string fileName;
            if (isDocPackage)
            {
                if (!TryWriteDrawingIntoPackage(doc, editContext, baseName, pngBytes, workspaceJson, out fileName))
                    return false;
            }
            else
            {
                if (!TryWriteDrawingIntoImagesFolder(editContext, baseName, pngBytes, workspaceJson, out fileName))
                    return false;
            }

            int scalePercent = editContext?.ScalePercent ?? 100;
            if (editContext != null)
                UpdateExistingMarkerLine(doc.Editor, editContext, fileName, scalePercent);
            else
                InsertImageMarkerLine(doc.Editor, BuildInlineImageMarkerText(fileName, scalePercent));

            doc.Editor.TextArea.TextView.Redraw();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not insert drawing:\n" + ex.Message,
                "Insert drawing", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool TryWriteDrawingIntoImagesFolder(DrawingEditContext? editContext, string baseName,
        byte[] pngBytes, string workspaceJson, out string fileName)
    {
        var imagesFolder = GetBackupImagesFolderPath();
        Directory.CreateDirectory(imagesFolder);
        var drawingsFolder = GetDrawingsFolderPath();
        Directory.CreateDirectory(drawingsFolder);

        fileName = PickNextDrawingFileName(baseName,
            candidate =>
                File.Exists(Path.Combine(imagesFolder, candidate))
                || File.Exists(Path.Combine(drawingsFolder, Path.ChangeExtension(candidate, ".json"))));

        var imagePath = Path.Combine(imagesFolder, fileName);
        File.WriteAllBytes(imagePath, pngBytes);

        var workspacePath = Path.Combine(drawingsFolder, Path.ChangeExtension(fileName, ".json"));
        File.WriteAllText(workspacePath, workspaceJson);

        _inlineImageCache.Remove(fileName);
        return true;
    }

    private bool TryWriteDrawingIntoPackage(TabDocument doc, DrawingEditContext? editContext, string baseName,
        byte[] pngBytes, string workspaceJson, out string fileName)
    {
        fileName = string.Empty;
        var packageId = editContext?.PackageId;
        if (string.IsNullOrEmpty(packageId)
            && !TryGetDocPackageIdForEditor(doc.Editor, out packageId))
        {
            return false;
        }

        var ownerPackage = _docPackages.FirstOrDefault(p => p.Id == packageId);
        if (ownerPackage != null && _documentationService.FindPackagePath(_backupFolder, packageId!) == null)
            _documentationService.SavePackage(_backupFolder, ownerPackage);

        fileName = PickNextDrawingFileName(baseName,
            candidate =>
                _documentationService.ImageExists(_backupFolder, packageId!, candidate)
                || _documentationService.DrawingExists(_backupFolder, packageId!,
                    Path.ChangeExtension(candidate, ".json")));

        _documentationService.WriteImage(_backupFolder, packageId!, fileName, pngBytes);
        _documentationService.WriteDrawing(_backupFolder, packageId!,
            Path.ChangeExtension(fileName, ".json"), workspaceJson);

        _inlineImageCache.Remove(BuildDocPackageImageCacheKey(packageId!, fileName));
        return true;
    }

    /// <summary>Picks <c>{base}-1.png</c>, <c>{base}-2.png</c>, … (e.g. <c>drawing-2026-05-16-232532-1.png</c>).</summary>
    private static string PickNextDrawingFileName(string baseName, Func<string, bool> existsPredicate)
    {
        for (int suffix = 1; suffix < 100000; suffix++)
        {
            var candidate = $"{baseName}-{suffix}.png";
            if (!existsPredicate(candidate))
                return candidate;
        }

        return $"{baseName}-{Guid.NewGuid():N}.png";
    }

    private static void UpdateExistingMarkerLine(TextEditor editor, DrawingEditContext context,
        string newFileName, int scalePercent)
    {
        if (editor.Document == null)
            return;

        var replacement = BuildInlineImageMarkerText(newFileName, scalePercent);
        var lineNumber = context.LineNumber;

        if (lineNumber >= 1 && lineNumber <= editor.Document.LineCount)
        {
            var line = editor.Document.GetLineByNumber(lineNumber);
            var lineText = editor.Document.GetText(line.Offset, line.Length);
            if (TryGetInlineImageMarker(lineText, out var marker)
                && string.Equals(marker.FileName, context.OriginalFileName, StringComparison.OrdinalIgnoreCase))
            {
                editor.Document.Replace(line.Offset, line.Length, replacement);
                return;
            }
        }

        for (int i = 1; i <= editor.Document.LineCount; i++)
        {
            var line = editor.Document.GetLineByNumber(i);
            var lineText = editor.Document.GetText(line.Offset, line.Length);
            if (TryGetInlineImageMarker(lineText, out var marker)
                && string.Equals(marker.FileName, context.OriginalFileName, StringComparison.OrdinalIgnoreCase))
            {
                editor.Document.Replace(line.Offset, line.Length, replacement);
                return;
            }
        }
    }

    /// <summary>Loads a drawing workspace JSON from disk (or doc package zip) given the marker filename it accompanies.
    /// Returns null when no workspace was saved alongside this image.</summary>
    internal DrawingWorkspaceDto? TryLoadDrawingWorkspace(TextEditor editor, string imageFileName)
    {
        var workspaceFileName = Path.ChangeExtension(imageFileName, ".json");
        if (string.IsNullOrEmpty(workspaceFileName))
            return null;

        string? json;
        if (TryGetDocPackageIdForEditor(editor, out var packageId))
        {
            json = _documentationService.TryReadDrawing(_backupFolder, packageId, workspaceFileName);
        }
        else
        {
            var workspacePath = Path.Combine(GetDrawingsFolderPath(), workspaceFileName);
            json = File.Exists(workspacePath) ? File.ReadAllText(workspacePath) : null;
        }

        return DrawingWindow.TryDeserialize(json);
    }

    /// <summary>Extracts the version-less base from a drawing filename: <c>drawing-2026-05-16-232532-2.png</c> →
    /// <c>drawing-2026-05-16-232532</c>. Files that don't match the <c>drawing-</c> convention return their stem
    /// unchanged.</summary>
    internal static string ExtractDrawingBaseName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem))
            return stem;

        // "drawing-" + "yyyy-MM-dd-HHmmss"
        var timeBaseLength = DrawingFileNamePrefix.Length + 17;
        if (stem.Length <= timeBaseLength)
            return stem;

        var dashIndex = stem.LastIndexOf('-');
        if (dashIndex <= 0 || dashIndex >= stem.Length - 1)
            return stem;

        var tail = stem.AsSpan(dashIndex + 1);
        foreach (var ch in tail)
        {
            if (!char.IsDigit(ch))
                return stem;
        }

        var withoutSuffix = stem.Substring(0, dashIndex);
        return withoutSuffix.Length == timeBaseLength ? withoutSuffix : stem;
    }
}

internal sealed class ThemeToolStyle
{
    public bool Enabled { get; set; }
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
        Enabled = Enabled,
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
    /// <summary>Number of "toolbar slot" variants per tool kind. The first N variants in each
    /// list are toolbar candidates (rendered as top-bar buttons when enabled). Additional
    /// variants beyond this count are valid but only appear in the property panel.</summary>
    public const int ToolbarSlotCount = 3;
    public const double DefaultArrowLabelFontSize = 18;

    public string Name { get; set; } = "Default";

    // Per-kind variant lists. Always contains at least ToolbarSlotCount entries; can have more.
    // Each ThemeToolStyle carries its own Enabled flag.
    public List<ThemeToolStyle> Rectangles { get; set; } = new();
    public List<ThemeToolStyle> Ellipses { get; set; } = new();
    public List<ThemeToolStyle> Arrows { get; set; } = new();
    public List<ThemeToolStyle> Texts { get; set; } = new();
    public List<ThemeToolStyle> Freehands { get; set; } = new();

    public int ActiveRectangleIndex { get; set; }
    public int ActiveEllipseIndex { get; set; }
    public int ActiveArrowIndex { get; set; }
    public int ActiveTextIndex { get; set; }
    public int ActiveFreehandIndex { get; set; }

    // Legacy singletons retained for read-only back-compat from older theme files.
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
        // Step 1: ensure legacy singletons are populated from legacy root for very old theme files.
        var seed = ThemeToolStyle.FromLegacyRoot(this);
        Rectangle ??= seed.Clone();
        Ellipse ??= seed.Clone();
        if (Arrow == null)
        {
            Arrow = seed.Clone();
            Arrow.FontSize = DefaultArrowLabelFontSize;
        }
        Text ??= seed.Clone();
        Freehand ??= seed.Clone();

        // Step 2: ensure each kind has at least ToolbarSlotCount entries. If empty (older file
        // format), seed from the legacy singleton with variant 0 enabled, others disabled. Any
        // count above ToolbarSlotCount is preserved as-is (extra variants appear in the property
        // panel only).
        EnsureVariantList(Rectangles, Rectangle!);
        EnsureVariantList(Ellipses, Ellipse!);
        EnsureVariantList(Arrows, Arrow!);
        EnsureVariantList(Texts, Text!);
        EnsureVariantList(Freehands, Freehand!);

        ActiveRectangleIndex = ClampActiveIndex(Rectangles, ActiveRectangleIndex);
        ActiveEllipseIndex = ClampActiveIndex(Ellipses, ActiveEllipseIndex);
        ActiveArrowIndex = ClampActiveIndex(Arrows, ActiveArrowIndex);
        ActiveTextIndex = ClampActiveIndex(Texts, ActiveTextIndex);
        ActiveFreehandIndex = ClampActiveIndex(Freehands, ActiveFreehandIndex);
    }

    private static void EnsureVariantList(List<ThemeToolStyle> list, ThemeToolStyle primary)
    {
        if (list.Count == 0)
        {
            var first = primary.Clone();
            first.Enabled = true;
            list.Add(first);
        }
        while (list.Count < ToolbarSlotCount)
        {
            var clone = list[0].Clone();
            clone.Enabled = false;
            list.Add(clone);
        }
    }

    private static int ClampActiveIndex(List<ThemeToolStyle> list, int idx)
    {
        if (list.Count == 0) return 0;
        if (idx < 0 || idx >= list.Count) idx = 0;
        if (list[idx].Enabled) return idx;
        for (var i = 0; i < list.Count; i++)
            if (list[i].Enabled) return i;
        return 0;
    }

    public List<ThemeToolStyle> GetVariants(string kind) => kind switch
    {
        "rect" => Rectangles,
        "ellipse" => Ellipses,
        "arrow" => Arrows,
        "text" => Texts,
        "freehand" => Freehands,
        _ => Rectangles,
    };

    public int GetActiveIndex(string kind) => kind switch
    {
        "rect" => ActiveRectangleIndex,
        "ellipse" => ActiveEllipseIndex,
        "arrow" => ActiveArrowIndex,
        "text" => ActiveTextIndex,
        "freehand" => ActiveFreehandIndex,
        _ => 0,
    };

    public void SetActiveIndex(string kind, int idx)
    {
        switch (kind)
        {
            case "rect": ActiveRectangleIndex = idx; break;
            case "ellipse": ActiveEllipseIndex = idx; break;
            case "arrow": ActiveArrowIndex = idx; break;
            case "text": ActiveTextIndex = idx; break;
            case "freehand": ActiveFreehandIndex = idx; break;
        }
    }

    public ThemeToolStyle GetActiveVariant(string kind)
    {
        EnsureToolStyles();
        var variants = GetVariants(kind);
        var idx = ClampActiveIndex(variants, GetActiveIndex(kind));
        SetActiveIndex(kind, idx);
        return variants[idx];
    }

    /// <summary>Copies the active variant of each kind back to the legacy singletons + flat root fields,
    /// so older JSON consumers (and any code still reading <see cref="Rectangle"/>) keep working.</summary>
    public void MirrorLegacyFromProfiles()
    {
        EnsureToolStyles();
        var r = GetActiveVariant("rect");
        var a = GetActiveVariant("arrow");
        var f = GetActiveVariant("freehand");
        Rectangle = r.Clone();
        Ellipse = GetActiveVariant("ellipse").Clone();
        Arrow = a.Clone();
        Text = GetActiveVariant("text").Clone();
        Freehand = f.Clone();
        Fill = r.Fill;
        Stroke = r.Stroke;
        TextColor = r.TextColor;
        FontFamilyName = r.FontFamilyName;
        FontSize = r.FontSize;
        Thickness = r.Thickness;
        CornerRadius = r.CornerRadius;
        ArrowHead = a.ArrowHead;
        FreeThickness = f.FreeThickness;
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
        Rectangles = Rectangles.Select(s => s.Clone()).ToList(),
        Ellipses = Ellipses.Select(s => s.Clone()).ToList(),
        Arrows = Arrows.Select(s => s.Clone()).ToList(),
        Texts = Texts.Select(s => s.Clone()).ToList(),
        Freehands = Freehands.Select(s => s.Clone()).ToList(),
        ActiveRectangleIndex = ActiveRectangleIndex,
        ActiveEllipseIndex = ActiveEllipseIndex,
        ActiveArrowIndex = ActiveArrowIndex,
        ActiveTextIndex = ActiveTextIndex,
        ActiveFreehandIndex = ActiveFreehandIndex,
    };
}

internal sealed class ThemeDisplayNameConverter : IValueConverter
{
    private const string BuiltInSuffix = " (built-in)";

    public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        return s.EndsWith(BuiltInSuffix, System.StringComparison.Ordinal)
            ? s[..^BuiltInSuffix.Length]
            : s;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotSupportedException();
}

internal static class DrawingThemeStore
{
    private const string FolderName = @"c:\tools\backup\noted";
    private const string FileName = "drawing-themes.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => System.IO.Path.Combine(FolderName, FileName);

    /// <summary>Folder for drawing-themes.json and drawing-named-colors.json.</summary>
    public static string NotedDataDirectory => FolderName;

    /// <summary>Built-in themes are identified by the trailing "(built-in)" name suffix. Built-ins
    /// are always loaded from code via <see cref="CreateDefaults"/> and are never persisted to the
    /// user's drawing-themes.json — only user-created themes go there.</summary>
    public const string BuiltInSuffix = " (built-in)";

    public static bool IsBuiltInName(string? name)
        => name != null && name.EndsWith(BuiltInSuffix, System.StringComparison.Ordinal);

    public static List<DrawingTheme> Load()
    {
        var result = CreateDefaults();
        try
        {
            if (!File.Exists(FilePath))
                return Finalize(result);
            var loaded = JsonSerializer.Deserialize<List<DrawingTheme>>(File.ReadAllText(FilePath));
            if (loaded == null || loaded.Count == 0)
                return Finalize(result);
            MigrateLegacyNames(loaded);
            foreach (var t in loaded)
            {
                if (IsBuiltInName(t.Name)) continue; // canonical version comes from code
                result.Add(t);
            }
            return Finalize(result);
        }
        catch
        {
            return Finalize(CreateDefaults());
        }
    }

    private static List<DrawingTheme> Finalize(List<DrawingTheme> themes)
    {
        foreach (var t in themes) t.EnsureToolStyles();
        return themes;
    }

    public static void Save(List<DrawingTheme> themes)
    {
        try
        {
            // Built-ins are loaded from code on every launch, so we never persist them — only
            // user-created themes go to disk.
            var userThemes = themes.Where(t => !IsBuiltInName(t.Name)).ToList();
            foreach (var t in userThemes)
            {
                t.EnsureToolStyles();
                t.MirrorLegacyFromProfiles();
            }
            Directory.CreateDirectory(FolderName);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(userThemes, Options));
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Folder shipped with the app that holds the canonical built-in theme definitions
    /// as <c>*.json</c> files. Each file deserializes into a <see cref="DrawingTheme"/>; per-kind
    /// variants are then populated by <see cref="DrawingTheme.EnsureToolStyles"/> from the legacy
    /// root fields (Fill/Stroke/TextColor/Font/...). Files are loaded in filename order, so a
    /// numeric prefix controls the display order.</summary>
    public static string BuiltInThemesDirectory =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins", "resources", "drawing", "themes");

    public static List<DrawingTheme> CreateDefaults()
    {
        var result = LoadBuiltInsFromDisk();
        if (result.Count > 0) return result;

        // Safety net so the rest of the app always has at least one default. This branch is
        // only reached when the shipped JSON resources are missing (broken deployment / tests).
        return new List<DrawingTheme>
        {
            new DrawingTheme
            {
                Name = "Mono (built-in)", Fill = "Transparent", Stroke = "#222222", TextColor = "#222222",
                FontFamilyName = "Segoe UI", FontSize = 22, Thickness = 2, CornerRadius = 14,
                ArrowHead = "Simple", FreeThickness = 6,
            },
        };
    }

    private static List<DrawingTheme> LoadBuiltInsFromDisk()
    {
        var result = new List<DrawingTheme>();
        try
        {
            var dir = BuiltInThemesDirectory;
            if (!Directory.Exists(dir)) return result;
            var files = Directory
                .EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                try
                {
                    var theme = JsonSerializer.Deserialize<DrawingTheme>(File.ReadAllText(path));
                    if (theme == null || string.IsNullOrWhiteSpace(theme.Name)) continue;
                    if (!IsBuiltInName(theme.Name)) theme.Name += BuiltInSuffix;
                    result.Add(theme);
                }
                catch
                {
                    // skip malformed file but keep loading the rest
                }
            }
        }
        catch
        {
            // ignore
        }
        return result;
    }

    /// <summary>Rename legacy theme names to the new "(built-in)" naming and drop the deprecated
    /// "Default" theme. Idempotent — re-running has no effect since the source names are gone
    /// after the first pass.</summary>
    private static void MigrateLegacyNames(List<DrawingTheme> themes)
    {
        themes.RemoveAll(t => string.Equals(t.Name, "Default", StringComparison.Ordinal));
        foreach (var t in themes)
        {
            t.Name = t.Name switch
            {
                "Mr V" => "Mono (built-in)",
                "Mono" => "Mono Square (built-in)",
                "Notebook" => "Notebook (built-in)",
                _ => t.Name,
            };
        }
    }
}

internal sealed class DrawingNamedColor
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#000000";

    public override string ToString() => $"{Name}    {Hex}";
}

internal static class DrawingColorUtilities
{
    public static bool TryParseColorString(string? s, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (string.Equals(s, "Transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Colors.Transparent;
            return true;
        }
        if (TryConvertColor(s, out color)) return true;
        if (LooksLikeBareHex(s) && TryConvertColor("#" + s, out color)) return true;
        return false;
    }

    private static bool TryConvertColor(string s, out Color color)
    {
        color = Colors.Black;
        try
        {
            var o = ColorConverter.ConvertFromString(s);
            if (o is Color c)
            {
                color = Color.FromArgb(c.A, c.R, c.G, c.B);
                return true;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private static bool LooksLikeBareHex(string s)
    {
        if (s.Length != 3 && s.Length != 4 && s.Length != 6 && s.Length != 8) return false;
        foreach (var ch in s)
        {
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                return false;
        }
        return true;
    }

    /// <summary>Returns the trimmed clipboard text normalized to a hex color string
    /// (prefixing <c>#</c> when the clipboard contains a bare hex value such as
    /// <c>DAE8FC</c>). Returns <c>null</c> if the clipboard does not contain a parseable color.</summary>
    public static string? TryReadColorFromClipboard()
    {
        string raw;
        try
        {
            if (!Clipboard.ContainsText()) return null;
            raw = Clipboard.GetText() ?? "";
        }
        catch
        {
            return null;
        }
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (string.Equals(trimmed, "Transparent", StringComparison.OrdinalIgnoreCase))
            return "Transparent";
        var normalized = trimmed.StartsWith('#') || !LooksLikeBareHex(trimmed)
            ? trimmed
            : "#" + trimmed;
        return TryParseColorString(normalized, out _) ? normalized : null;
    }

    public static string FormatHexForTheme(Color c)
    {
        if (c.A == 0) return "Transparent";
        if (c.A == 255) return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static Color ColorFromHsv(double hueDegrees, double sat, double val)
    {
        sat = Math.Clamp(sat, 0, 1);
        val = Math.Clamp(val, 0, 1);
        while (hueDegrees >= 360) hueDegrees -= 360;
        while (hueDegrees < 0) hueDegrees += 360;

        var c = val * sat;
        var x = c * (1 - Math.Abs(hueDegrees / 60 % 2 - 1));
        var m = val - c;

        double rp = 0, gp = 0, bp = 0;
        if (hueDegrees < 60) { rp = c; gp = x; bp = 0; }
        else if (hueDegrees < 120) { rp = x; gp = c; bp = 0; }
        else if (hueDegrees < 180) { rp = 0; gp = c; bp = x; }
        else if (hueDegrees < 240) { rp = 0; gp = x; bp = c; }
        else if (hueDegrees < 300) { rp = x; gp = 0; bp = c; }
        else { rp = c; gp = 0; bp = x; }

        return Color.FromRgb(
            (byte)Math.Round((rp + m) * 255),
            (byte)Math.Round((gp + m) * 255),
            (byte)Math.Round((bp + m) * 255));
    }

    public static void RgbToHsv(Color color, out double hueDegrees, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        v = max;
        s = max < 1e-10 ? 0 : delta / max;

        if (delta < 1e-10)
        {
            hueDegrees = 0;
            return;
        }

        double hh;
        if (Math.Abs(max - r) < 1e-10)
            hh = (g - b) / delta + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-10)
            hh = (b - r) / delta + 2;
        else
            hh = (r - g) / delta + 4;

        hueDegrees = hh * 60;
    }

    public static Color ParseOrDefault(string? hex, Color fallback)
        => TryParseColorString(hex, out var c) ? c : fallback;
}

/// <summary>
/// Backwards-compatible facade over <see cref="ColorPaletteService"/>. The drawing plugin's
/// own "Custom colors" dialog operates on the default ("Main") palette; pickers across the
/// app see every named color from every palette via <see cref="MergeWithBasePalette"/>.
/// </summary>
internal static class DrawingNamedColorStore
{
    private static readonly ColorPaletteService Service = new();

    public static List<DrawingNamedColor> Load()
    {
        var main = Service.GetOrCreateDefault();
        return main.Colors
            .Select(c => new DrawingNamedColor { Name = c.Name, Hex = c.Hex })
            .ToList();
    }

    public static void Save(List<DrawingNamedColor> colors)
    {
        var effective = colors
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Hex))
            .Select(c => new NamedColor { Name = c.Name, Hex = c.Hex })
            .ToList();
        Service.SaveBuiltInPaletteColors(ColorPaletteService.DefaultPaletteName, effective);
    }

    public static Color[] MergeWithBasePalette(Color[] baseColors)
    {
        var list = new List<Color>(baseColors);
        foreach (var n in Service.GetAllNamedColors())
        {
            if (!DrawingColorUtilities.TryParseColorString(n.Hex, out var c) || c.A == 0)
                continue;
            var exists = false;
            foreach (var x in list)
            {
                if (x == c) { exists = true; break; }
            }
            if (!exists) list.Add(c);
        }
        return list.ToArray();
    }
}

internal sealed class DrawingColorPickerWindow : Window
{
    public string? ResultHex { get; private set; }

    /// <summary>Fires for every textual change to the picker's selected color while the dialog is open.</summary>
    public event Action<Color>? ColorChanged;

    private const int SpecW = 240;
    private const int SpecH = 160;
    private const int StripW = 22;

    private readonly WriteableBitmap _spectrumBmp;
    private readonly WriteableBitmap _stripBmp;
    private readonly Image _spectrumImg;
    private readonly Image _stripImg;
    private readonly Grid _spectrumGrid;
    private readonly Grid _stripGrid;
    private readonly Canvas _markerCanvas;
    private readonly Ellipse _markerRing;
    private readonly Polygon _stripThumb;
    private readonly TextBox _hex;
    private bool _suppress;

    private double _selH;
    private double _selS;
    private double _selV;

    public DrawingColorPickerWindow(string? initialHex)
    {
        Title = "Color picker";
        Width = 340;
        Height = 380;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var start = DrawingColorUtilities.ParseOrDefault(initialHex, Color.FromRgb(0x21, 0x96, 0xF3));
        if (start.A == 0) start = Colors.White;
        DrawingColorUtilities.RgbToHsv(start, out _selH, out _selS, out _selV);

        var root = new DockPanel { Margin = new Thickness(14) };

        var form = new StackPanel();

        form.Children.Add(new TextBlock
        {
            Text = "Click the field to choose hue and saturation. Use the strip for brightness.",
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        _spectrumBmp = new WriteableBitmap(SpecW, SpecH, 96, 96, PixelFormats.Pbgra32, null);
        _spectrumImg = new Image { Width = SpecW, Height = SpecH, Stretch = Stretch.Fill };
        _spectrumImg.Source = _spectrumBmp;
        _markerRing = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        _markerCanvas = new Canvas { Width = SpecW, Height = SpecH, Background = Brushes.Transparent };
        _markerCanvas.Children.Add(_markerRing);

        _spectrumGrid = new Grid { Width = SpecW, Height = SpecH, ClipToBounds = true };
        _spectrumGrid.Children.Add(_spectrumImg);
        _spectrumGrid.Children.Add(_markerCanvas);
        _spectrumGrid.MouseLeftButtonDown += (_, e) =>
        {
            _spectrumGrid.CaptureMouse();
            ApplySpectrumPoint(e.GetPosition(_spectrumGrid));
        };
        _spectrumGrid.MouseLeftButtonUp += (_, _) => _spectrumGrid.ReleaseMouseCapture();
        _spectrumGrid.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _spectrumGrid.IsMouseCaptured)
                ApplySpectrumPoint(e.GetPosition(_spectrumGrid));
        };

        pickerRow.Children.Add(_spectrumGrid);

        _stripBmp = new WriteableBitmap(StripW, SpecH, 96, 96, PixelFormats.Pbgra32, null);
        _stripImg = new Image { Width = StripW, Height = SpecH, Stretch = Stretch.Fill };
        _stripImg.Source = _stripBmp;
        _stripThumb = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Points = new PointCollection { new(0, 0), new(7, 5), new(0, 10) },
            IsHitTestVisible = false,
        };
        _stripGrid = new Grid { Width = StripW, Height = SpecH, Margin = new Thickness(10, 0, 0, 0), ClipToBounds = true };
        _stripGrid.Children.Add(_stripImg);
        var stripOverlay = new Canvas { Width = StripW, Height = SpecH, Background = Brushes.Transparent };
        stripOverlay.Children.Add(_stripThumb);
        _stripGrid.Children.Add(stripOverlay);
        _stripGrid.MouseLeftButtonDown += (_, e) =>
        {
            _stripGrid.CaptureMouse();
            ApplyStripPoint(e.GetPosition(_stripGrid));
        };
        _stripGrid.MouseLeftButtonUp += (_, _) => _stripGrid.ReleaseMouseCapture();
        _stripGrid.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _stripGrid.IsMouseCaptured)
                ApplyStripPoint(e.GetPosition(_stripGrid));
        };

        pickerRow.Children.Add(_stripGrid);
        form.Children.Add(pickerRow);

        form.Children.Add(new TextBlock { Text = "Hex", FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 4) });
        _hex = new TextBox { Margin = new Thickness(0, 0, 0, 0) };
        form.Children.Add(_hex);

        _hex.TextChanged += (_, _) =>
        {
            if (!_suppress)
            {
                if (DrawingColorUtilities.TryParseColorString(_hex.Text, out var typed) && typed.A != 0)
                {
                    DrawingColorUtilities.RgbToHsv(typed, out _selH, out _selS, out _selV);
                    RegenerateSpectrum();
                    RegenerateStrip();
                    UpdateMarkerAndThumb();
                }
                else
                {
                    return;
                }
            }
            if (DrawingColorUtilities.TryParseColorString(_hex.Text, out var current) && current.A != 0)
                ColorChanged?.Invoke(Color.FromRgb(current.R, current.G, current.B));
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var btnSelect = new Button { Content = "Select", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var btnCancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        btnSelect.Click += (_, _) =>
        {
            if (!DrawingColorUtilities.TryParseColorString(_hex.Text, out var c))
            {
                MessageBox.Show(this, "Could not parse the hex color.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ResultHex = DrawingColorUtilities.FormatHexForTheme(c);
            DialogResult = true;
            Close();
        };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(btnSelect);
        buttons.Children.Add(btnCancel);
        root.Children.Add(buttons);

        root.Children.Add(form);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            var pasted = DrawingColorUtilities.TryReadColorFromClipboard();
            if (pasted is null) return;
            _hex.Text = pasted;
            _hex.CaretIndex = _hex.Text.Length;
            btnSelect.Focus();
            e.Handled = true;
        };

        RegenerateSpectrum();
        RegenerateStrip();
        _suppress = true;
        _hex.Text = DrawingColorUtilities.FormatHexForTheme(start);
        _suppress = false;
        UpdateMarkerAndThumb();
    }

    private void ApplySpectrumPoint(Point p)
    {
        var x = Math.Clamp(p.X, 0, SpecW - 1);
        var y = Math.Clamp(p.Y, 0, SpecH - 1);
        _selH = x / Math.Max(SpecW - 1, 1) * 360.0;
        _selS = 1.0 - y / Math.Max(SpecH - 1, 1);
        RegenerateStrip();
        PushHexFromHsv();
        UpdateMarkerAndThumb();
    }

    private void ApplyStripPoint(Point p)
    {
        var y = Math.Clamp(p.Y, 0, SpecH - 1);
        _selV = 1.0 - y / Math.Max(SpecH - 1, 1);
        RegenerateSpectrum();
        PushHexFromHsv();
        UpdateMarkerAndThumb();
    }

    private void PushHexFromHsv()
    {
        var c = DrawingColorUtilities.ColorFromHsv(_selH, _selS, _selV);
        _suppress = true;
        _hex.Text = DrawingColorUtilities.FormatHexForTheme(c);
        _suppress = false;
    }

    private void RegenerateSpectrum()
    {
        var stride = SpecW * 4;
        var pixels = new byte[stride * SpecH];
        for (var y = 0; y < SpecH; y++)
        {
            var sat = 1.0 - y / (double)Math.Max(SpecH - 1, 1);
            for (var x = 0; x < SpecW; x++)
            {
                var hue = x / (double)Math.Max(SpecW - 1, 1) * 360.0;
                var col = DrawingColorUtilities.ColorFromHsv(hue, sat, _selV);
                var i = y * stride + x * 4;
                pixels[i] = col.B;
                pixels[i + 1] = col.G;
                pixels[i + 2] = col.R;
                pixels[i + 3] = 255;
            }
        }
        _spectrumBmp.WritePixels(new Int32Rect(0, 0, SpecW, SpecH), pixels, stride, 0);
    }

    private void RegenerateStrip()
    {
        var stride = StripW * 4;
        var pixels = new byte[stride * SpecH];
        for (var y = 0; y < SpecH; y++)
        {
            var v = 1.0 - y / (double)Math.Max(SpecH - 1, 1);
            var col = DrawingColorUtilities.ColorFromHsv(_selH, _selS, v);
            for (var x = 0; x < StripW; x++)
            {
                var i = y * stride + x * 4;
                pixels[i] = col.B;
                pixels[i + 1] = col.G;
                pixels[i + 2] = col.R;
                pixels[i + 3] = 255;
            }
        }
        _stripBmp.WritePixels(new Int32Rect(0, 0, StripW, SpecH), pixels, stride, 0);
    }

    private void UpdateMarkerAndThumb()
    {
        var mx = _selH / 360.0 * (SpecW - 1);
        var my = (1.0 - _selS) * (SpecH - 1);
        Canvas.SetLeft(_markerRing, mx - _markerRing.Width / 2);
        Canvas.SetTop(_markerRing, my - _markerRing.Height / 2);

        var ty = (1.0 - _selV) * (SpecH - 1) - 5;
        Canvas.SetLeft(_stripThumb, StripW - 9);
        Canvas.SetTop(_stripThumb, Math.Clamp(ty, 0, SpecH - 11));
    }
}

internal sealed class DrawingNamedColorsWindow : Window
{
    private readonly ListBox _list;
    private List<DrawingNamedColor> _working = new();
    private bool _dirty;

    public DrawingNamedColorsWindow()
    {
        Title = "Custom colors";
        Width = 420;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _working = DrawingNamedColorStore.Load();

        var root = new DockPanel { Margin = new Thickness(12) };

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var btnClose = new Button { Content = "Close", Width = 90, IsCancel = true };
        btnClose.Click += (_, _) =>
        {
            DialogResult = _dirty;
            Close();
        };
        bottom.Children.Add(btnClose);
        root.Children.Add(bottom);

        var center = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(actions, Dock.Top);
        var btnAdd = new Button { Content = "Pick & add…", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
        var btnEdit = new Button { Content = "Edit…", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
        var btnRemove = new Button { Content = "Remove", Padding = new Thickness(10, 4, 10, 4) };
        actions.Children.Add(btnAdd);
        actions.Children.Add(btnEdit);
        actions.Children.Add(btnRemove);
        center.Children.Add(actions);

        _list = new ListBox { MinHeight = 240 };
        _list.ItemsSource = _working;
        center.Children.Add(_list);
        root.Children.Add(center);

        Content = root;

        btnAdd.Click += (_, _) => AddOrEdit(isEdit: false);
        btnEdit.Click += (_, _) => AddOrEdit(isEdit: true);
        btnRemove.Click += (_, _) =>
        {
            if (_list.SelectedItem is not DrawingNamedColor sel) return;
            _working.Remove(sel);
            DrawingNamedColorStore.Save(_working);
            _list.ItemsSource = null;
            _list.ItemsSource = _working;
            _dirty = true;
        };
    }

    private void AddOrEdit(bool isEdit)
    {
        DrawingNamedColor? existing = isEdit ? _list.SelectedItem as DrawingNamedColor : null;
        if (isEdit && existing == null) return;

        var pick = new DrawingColorPickerWindow(isEdit ? existing!.Hex : null) { Owner = this };
        if (pick.ShowDialog() != true || string.IsNullOrWhiteSpace(pick.ResultHex)) return;

        var nameDlg = new Window
        {
            Title = isEdit ? "Rename color" : "Name this color",
            Width = 380,
            Height = 150,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(14) };
        sp.Children.Add(new TextBlock { Text = "Display name", Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = existing?.Name ?? "", Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(nameBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        sp.Children.Add(row);
        nameDlg.Content = sp;
        ok.Click += (_, _) => { nameDlg.DialogResult = true; nameDlg.Close(); };
        cancel.Click += (_, _) => { nameDlg.DialogResult = false; nameDlg.Close(); };
        if (nameDlg.ShowDialog() != true) return;

        var name = nameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "Please enter a name.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (isEdit && existing != null)
        {
            existing.Name = name;
            existing.Hex = pick.ResultHex!;
        }
        else
        {
            _working.Add(new DrawingNamedColor { Name = name, Hex = pick.ResultHex! });
        }

        DrawingNamedColorStore.Save(_working);
        _list.ItemsSource = null;
        _list.ItemsSource = _working;
        _dirty = true;
    }
}

internal static class DrawingFreehandGeometry
{
    public static Geometry CreateSmoothGeometry(IReadOnlyList<Point> points) => CreateSmoothFreehandGeometry(points);

    public static List<Point> Flatten(IReadOnlyList<Point> points) => FlattenFreehandGeometry(points);

    public static double PathLength(IReadOnlyList<Point> points) => FreehandPathLength(points);

    private static double FreehandPathLength(IReadOnlyList<Point> points)
    {
        var len = 0.0;
        for (var i = 1; i < points.Count; i++)
            len += (points[i] - points[i - 1]).Length;
        return len;
    }

    private static Geometry CreateSmoothFreehandGeometry(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            return Geometry.Empty;
        if (points.Count == 1)
            return new EllipseGeometry(points[0], 1, 1);

        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false,
        };
        var segments = new PathSegmentCollection();

        if (points.Count == 2)
        {
            segments.Add(new LineSegment(points[1], true));
            figure.Segments = segments;
            return new PathGeometry(new[] { figure });
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            var mid = new Point((current.X + next.X) * 0.5, (current.Y + next.Y) * 0.5);
            if (i == 0)
                segments.Add(new LineSegment(mid, true));
            else
                segments.Add(new QuadraticBezierSegment(current, mid, true));
        }

        segments.Add(new LineSegment(points[^1], true));
        figure.Segments = segments;
        return new PathGeometry(new[] { figure });
    }

    private static List<Point> FlattenFreehandGeometry(IReadOnlyList<Point> points, int stepsPerCurve = 10)
    {
        var result = new List<Point>();
        if (points.Count == 0)
            return result;
        result.Add(points[0]);
        if (points.Count == 1)
            return result;
        if (points.Count == 2)
        {
            result.Add(points[1]);
            return result;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            var mid = new Point((current.X + next.X) * 0.5, (current.Y + next.Y) * 0.5);
            if (i == 0)
                SampleLine(result, points[0], mid, stepsPerCurve);
            else
                SampleQuadratic(result, result[^1], current, mid, stepsPerCurve);
        }

        var end = points[^1];
        if ((result[^1] - end).Length > 0.01)
            SampleLine(result, result[^1], end, stepsPerCurve);
        return result;
    }

    private static void SampleLine(List<Point> result, Point start, Point end, int steps)
    {
        for (var j = 1; j <= steps; j++)
        {
            var t = (double)j / steps;
            result.Add(new Point(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t));
        }
    }

    private static void SampleQuadratic(List<Point> result, Point start, Point control, Point end, int steps)
    {
        for (var j = 1; j <= steps; j++)
        {
            var t = (double)j / steps;
            var u = 1 - t;
            result.Add(new Point(
                u * u * start.X + 2 * u * t * control.X + t * t * end.X,
                u * u * start.Y + 2 * u * t * control.Y + t * t * end.Y));
        }
    }
}

// ---------------- Drawing workspace (serializable) ----------------

/// <summary>JSON-serializable snapshot of a <see cref="DrawingWindow"/> canvas. Stored next to the PNG so the
/// drawing can be re-opened and edited later via the "Edit drawing" context-menu action.</summary>
internal sealed class DrawingWorkspaceDto
{
    public int Version { get; set; } = 1;
    public double CanvasWidth { get; set; } = 2000;
    public double CanvasHeight { get; set; } = 1400;
    public List<DrawItemDto> Items { get; set; } = new();
}

internal sealed class DrawItemDto
{
    public string Kind { get; set; } = "rect";
    public double P1X { get; set; }
    public double P1Y { get; set; }
    public double P2X { get; set; }
    public double P2Y { get; set; }
    public double CornerRadius { get; set; } = 14;
    public string Fill { get; set; } = "Transparent";
    public string GradientFill { get; set; } = "Transparent";
    public bool HasShadow { get; set; }
    public string Stroke { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 2;
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 22;
    public string FontFamilyName { get; set; } = "Segoe UI";
    public string TextColor { get; set; } = "#000000";
    public string ArrowHead { get; set; } = "Simple";
    public bool Direct { get; set; } = true;
    /// <summary>When true, simple arrow head is at P2 (draw end before any canonical swap).</summary>
    public bool SimpleHeadAtP2 { get; set; } = true;
    public List<double> RouteBendsX { get; set; } = new();
    public List<double> RouteBendsY { get; set; } = new();
    public List<double> PointsX { get; set; } = new();
    public List<double> PointsY { get; set; } = new();
    public int FreehandGroupId { get; set; }
    public int Id { get; set; }
    public int? AnchorStartShapeId { get; set; }
    public int? AnchorEndShapeId { get; set; }
    public int? AnchorStartIndex { get; set; }
    public int? AnchorEndIndex { get; set; }
}

/// <summary>Context passed to <see cref="DrawingWindow"/> when re-opening a previously-saved drawing via the
/// "Edit drawing" context-menu action. Saving creates a new <c>-N</c> versioned file next to the original.</summary>
internal sealed class DrawingEditContext
{
    public required TabDocument Doc { get; init; }
    public required string OriginalFileName { get; init; }
    public required string BaseName { get; init; }
    public required int LineNumber { get; init; }
    public int ScalePercent { get; init; } = 100;
    public string? PackageId { get; init; }
}

internal sealed partial class DrawingWindow : Window
{
    private enum Tool { Select, Rectangle, Ellipse, Arrow, Text, Freehand }

    private enum ArrowHeadStyle { None, Simple, Double }

    private sealed class DrawItem
    {
        public int Id;
        public string Kind = "rect"; // rect, ellipse, arrow, text, freehand
        public Point P1;
        public Point P2;
        public int? AnchorStartShapeId;
        public int? AnchorEndShapeId;
        public int? AnchorStartIndex;
        public int? AnchorEndIndex;
        public double CornerRadius = 14;
        public Brush Fill = Brushes.Transparent;
        public Brush GradientFill = Brushes.Transparent;
        public bool HasShadow;
        public Brush Stroke = Brushes.Black;
        public double StrokeThickness = 2;
        public string Text = "";
        public double FontSize = 22;
        public FontFamily FontFamily = new("Segoe UI");
        public Brush TextColor = Brushes.Black;
        public ArrowHeadStyle ArrowHead = ArrowHeadStyle.Simple;
        public bool Direct = true;
        public bool SimpleHeadAtP2 = true;
        public List<Point> RouteBends = new();
        public List<Point> Points = new();
        /// <summary>Strokes drawn in the same freehand-tool session share a group id for move/delete.</summary>
        public int FreehandGroupId;

        public DrawItem Clone() => new()
        {
            Id = Id,
            Kind = Kind,
            P1 = P1,
            P2 = P2,
            AnchorStartShapeId = AnchorStartShapeId,
            AnchorEndShapeId = AnchorEndShapeId,
            AnchorStartIndex = AnchorStartIndex,
            AnchorEndIndex = AnchorEndIndex,
            CornerRadius = CornerRadius,
            Fill = Fill,
            GradientFill = GradientFill,
            HasShadow = HasShadow,
            Stroke = Stroke,
            StrokeThickness = StrokeThickness,
            Text = Text,
            FontSize = FontSize,
            FontFamily = FontFamily,
            TextColor = TextColor,
            ArrowHead = ArrowHead,
            Direct = Direct,
            SimpleHeadAtP2 = SimpleHeadAtP2,
            RouteBends = new List<Point>(RouteBends),
            Points = new List<Point>(Points),
            FreehandGroupId = FreehandGroupId,
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
        Color.FromRgb(0x3F, 0x51, 0xB5),
        Color.FromRgb(0xF5, 0xF5, 0xDC),
    };

    private static readonly string[] FontChoices =
    {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Consolas", "Courier New",
        "Georgia", "Helvetica", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
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

    private readonly ComboBox _themeCombo;
    private readonly StackPanel _propertyPanel;

    private CompactColorPicker? _fillColorPicker;
    private CompactColorPicker? _gradientColorPicker;
    private CompactColorPicker? _strokeColorPicker;
    private CompactColorPicker? _textColorPicker;
    private CompactColorPicker? _freehandColorPicker;
    private CompactColorPicker? _arrowColorPicker;
    private Grid? _fillColorRow;
    private Grid? _gradientColorRow;
    private Grid? _strokeColorRow;
    private Grid? _textColorRow;
    private Grid? _freehandColorRow;
    private Grid? _arrowColorRow;
    private CheckBox? _shadowCheck;
    private TextBlock? _thicknessHeader;
    private TextBox? _thicknessBox;
    private TextBox? _cornerRadiusBox;
    private TextBox? _fontSizeBox;

    private const double ThicknessMin = 1;
    private const double ThicknessMax = 24;
    private const double CornerRadiusMin = 0;
    private const double CornerRadiusMax = 80;
    private const double FontSizeMin = 8;
    private const double FontSizeMax = 96;
    private ComboBox? _fontFamilyCombo;
    private ComboBox? _arrowHeadCombo;
    private CheckBox? _arrowDirectCheck;
    private TextBlock? _cornerRadiusHeader;
    private TextBlock? _fontHeader;
    private TextBlock? _fontSizeHeader;
    private TextBlock? _arrowStyleHeader;
    private TextBlock? _sizeHeader;
    private TextBlock? _sizeLine1;
    private TextBlock? _sizeLine2;
    private TextBlock? _variantsHeader;
    private WrapPanel? _variantsPanel;

    private StackPanel? _toolsPanel;

    private sealed record ToolBarTag(Tool Tool, string? Kind, int VariantIndex);

    private readonly HashSet<DrawItem> _selection = new();
    private List<DrawItem>? _clipboard;
    private Tool _tool = Tool.Select;

    private Brush _fill = Brushes.Transparent;
    private Brush _gradientFill = Brushes.Transparent;
    private bool _hasShadow;
    private Brush _stroke = Brushes.Black;
    private Brush _textColor = Brushes.Black;
    private double _thickness = 2;
    private double _cornerRadius = 14;
    private double _fontSize = 22;
    private FontFamily _fontFamily = new("Segoe UI");
    private ArrowHeadStyle _arrowHead = ArrowHeadStyle.Simple;
    private bool _arrowDirect = true;
    private double _freeThickness = 6;

    private bool _isDrawing;
    private DrawItem? _drawingItem;

    private bool _isMoving;
    private Point _moveLast;
    private double _snapHeldDx;
    private double _snapHeldDy;
    private IReadOnlyList<DrawingSnapGuides.GuideLine> _snapGuides = Array.Empty<DrawingSnapGuides.GuideLine>();
    private int _activeHandle = -1;
    private bool _pendingUndoForGesture;

    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Point _marqueeCurrent;

    private TextBox? _activeEditor;
    private DrawItem? _editingItem;
    private bool _editChangedAnything;

    private bool _suppressPropertyChanges;

    private int _nextFreehandGroupId = 1;
    private int _currentFreehandGroupId;
    private int _nextItemId = 1;
    private DrawItem? _hoveredAnchorShape;
    private DrawItem? _arrowDrawStartShape;
    private DrawItem? _arrowDrawEndShape;
    private int? _arrowDrawEndAnchorIndex;

    private const double HandleSize = 8;
    private const double HitPadding = 6;
    private const double CanvasWidth = 2000;
    private const double CanvasHeight = 1400;
    private const double InsertPaddingPx = 20;
    private const double EllipseInscribedFactor = 0.70710678118654752;

    private readonly MainWindow _host;
    private readonly DrawingEditContext? _editContext;
    private ScrollViewer? _canvasScroll;
    private bool _isPanning;
    private Point _panStartScreen;
    private double _panStartHOffset;
    private double _panStartVOffset;

    public DrawingWindow(MainWindow host, string? preferredThemeName)
        : this(host, preferredThemeName, null, null)
    {
    }

    public DrawingWindow(MainWindow host, string? preferredThemeName,
        DrawingWorkspaceDto? initialWorkspace, DrawingEditContext? editContext)
    {
        _host = host;
        _editContext = editContext;
        Title = editContext == null ? "Drawing" : $"Edit drawing — {editContext.BaseName}";
        Width = 1280;
        Height = 860;
        MinWidth = 780;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _themes = DrawingThemeStore.Load();
        _activeTheme = ResolveThemeByName(_themes, preferredThemeName)
            ?? _themes.FirstOrDefault()
            ?? new DrawingTheme();
        _activeTheme.EnsureToolStyles();
        ApplyToolProfileFromTheme(_activeTheme, _tool);

        var root = new DockPanel();

        // ---- Top toolbar: tool buttons + theme picker + actions ----
        var topBar = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xEF)),
            LastChildFill = false,
        };
        DockPanel.SetDock(topBar, Dock.Top);

        _toolsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
        topBar.Children.Add(_toolsPanel);

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
                _host.PersistLastDrawingThemeName(t.Name);
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
                ClearSelection();
                Redraw();
            }
        };
        actions.Children.Add(btnClear);

        var btnSave = MakeButton("Save as PNG");
        btnSave.Click += (_, _) => SavePng();
        actions.Children.Add(btnSave);

        var btnInsert = MakeButton(_editContext == null ? "Insert into editor" : "Save new version");
        btnInsert.Click += (_, _) => InsertIntoEditor();
        actions.Children.Add(btnInsert);

        topBar.Children.Add(actions);
        root.Children.Add(topBar);

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
        ApplyTheme(_activeTheme, syncUiOnly: false);
        root.Children.Add(_leftScroll);

        // ---- Center canvas ----
        var center = new Grid { Background = Brushes.White };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.White,
        };
        _canvasScroll = scroll;
        var canvasHost = new Grid();
        _canvas = new Canvas
        {
            Background = Brushes.White,
            Width = CanvasWidth,
            Height = CanvasHeight,
            ClipToBounds = true,
        };
        _overlay = new Canvas
        {
            Background = null,
            Width = CanvasWidth,
            Height = CanvasHeight,
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
        _canvas.MouseRightButtonDown += Canvas_MouseRightButtonDown;

        scroll.PreviewMouseDown += CanvasScroll_PreviewMouseDown;
        scroll.PreviewMouseMove += CanvasScroll_PreviewMouseMove;
        scroll.PreviewMouseUp += CanvasScroll_PreviewMouseUp;
        scroll.LostMouseCapture += (_, _) => _isPanning = false;

        PreviewKeyDown += DrawingWindow_PreviewKeyDown;

        SelectTool(Tool.Select);

        if (initialWorkspace != null)
        {
            LoadWorkspace(initialWorkspace);
            SizeWindowToFitItems();
        }

        Closed += (_, _) =>
        {
            _host.PersistLastDrawingThemeName(_activeTheme.Name);
            DrawingThemeStore.Save(_themes);
        };
    }

    private static DrawingTheme? ResolveThemeByName(List<DrawingTheme> themes, string? name)
    {
        if (themes.Count == 0 || string.IsNullOrWhiteSpace(name)) return null;
        return themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------- Theme ----------------

    private void RefreshThemeCombo()
    {
        var prev = _activeTheme?.Name;
        _themeCombo.ItemsSource = null;
        // ItemTemplate strips the "(built-in)" suffix for display; the underlying DrawingTheme.Name
        // is unchanged so the Drawing Themes settings window still shows the full name.
        _themeCombo.DisplayMemberPath = null;
        if (_themeCombo.ItemTemplate == null)
        {
            var ft = new FrameworkElementFactory(typeof(TextBlock));
            ft.SetBinding(TextBlock.TextProperty, new Binding("Name") { Converter = new ThemeDisplayNameConverter() });
            _themeCombo.ItemTemplate = new DataTemplate { VisualTree = ft };
        }
        _themeCombo.ItemsSource = _themes;
        var match = _themes.FirstOrDefault(t => string.Equals(t.Name, prev, StringComparison.OrdinalIgnoreCase))
            ?? _themes.FirstOrDefault();
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
        {
            SyncPropertyControlsFromCurrent();
            UpdatePropertyPanelVisibility();
        }
        RefreshToolBar();
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

    private static string ToolToKind(Tool tool) => tool switch
    {
        Tool.Rectangle => "rect",
        Tool.Ellipse => "ellipse",
        Tool.Arrow => "arrow",
        Tool.Text => "text",
        Tool.Freehand => "freehand",
        _ => "rect",
    };

    private Tool ThemeToolSource()
    {
        if (_tool == Tool.Select && PrimarySelection != null)
            return MapDrawKindToTool(PrimarySelection.Kind);
        if (_tool == Tool.Select)
            return Tool.Rectangle;
        return _tool;
    }

    private void ApplyToolProfileFromTheme(DrawingTheme theme, Tool profileTool)
    {
        theme.EnsureToolStyles();
        var st = theme.GetActiveVariant(ToolToKind(profileTool));
        _fill = ParseBrush(st.Fill);
        _gradientFill = Brushes.Transparent;
        _hasShadow = false;
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
        var saved = dlg.ShowDialog() == true;
        RefreshDrawingPalettes();
        if (saved)
        {
            _themes = dlg.Themes;
            DrawingThemeStore.Save(_themes);
            RefreshThemeCombo();
            ApplyTheme(_activeTheme, syncUiOnly: false);
            _host.PersistLastDrawingThemeName(_activeTheme.Name);
        }
        else
        {
            RefreshToolBar();
        }
    }

    private void RefreshDrawingPalettes()
    {
        _fillColorPicker?.Refresh();
        _fillColorPicker?.SetSelected(_fill);
        _gradientColorPicker?.Refresh();
        _gradientColorPicker?.SetSelected(_gradientFill);
        _strokeColorPicker?.Refresh();
        _strokeColorPicker?.SetSelected(_stroke);
        _textColorPicker?.Refresh();
        _textColorPicker?.SetSelected(_textColor);
        _freehandColorPicker?.Refresh();
        _freehandColorPicker?.SetSelected(_stroke);
        _arrowColorPicker?.Refresh();
        _arrowColorPicker?.SetSelected(_stroke);
    }

    // ---------------- Property panel ----------------

    private void BuildPropertyPanel()
    {
        _propertyPanel.Children.Clear();

        _sizeHeader = SectionHeader("Size");
        _propertyPanel.Children.Add(_sizeHeader);
        _sizeLine1 = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 2),
        };
        _propertyPanel.Children.Add(_sizeLine1);
        _sizeLine2 = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _propertyPanel.Children.Add(_sizeLine2);

        _variantsHeader = SectionHeader("Variants");
        _propertyPanel.Children.Add(_variantsHeader);
        _variantsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        _propertyPanel.Children.Add(_variantsPanel);

        _fillColorPicker = new CompactColorPicker(_fill,
            onChanged: b => { _fill = b; ApplyColorToSelected(); },
            onPreview: b => { _fill = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _fillColorRow = MakeCompactColorRow("Fill", _fillColorPicker);
        _propertyPanel.Children.Add(_fillColorRow);

        _gradientColorPicker = new CompactColorPicker(_gradientFill,
            onChanged: b => { _gradientFill = b; ApplyColorToSelected(); },
            onPreview: b => { _gradientFill = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _gradientColorRow = MakeCompactColorRow("Gradient", _gradientColorPicker);
        _propertyPanel.Children.Add(_gradientColorRow);

        _strokeColorPicker = new CompactColorPicker(_stroke,
            onChanged: b => { _stroke = b; ApplyColorToSelected(); },
            onPreview: b => { _stroke = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _strokeColorRow = MakeCompactColorRow("Stroke / Frame", _strokeColorPicker);
        _propertyPanel.Children.Add(_strokeColorRow);

        _textColorPicker = new CompactColorPicker(_textColor,
            onChanged: b => { _textColor = b; ApplyColorToSelected(); },
            onPreview: b => { _textColor = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _textColorRow = MakeCompactColorRow("Text color", _textColorPicker);
        _propertyPanel.Children.Add(_textColorRow);

        _freehandColorPicker = new CompactColorPicker(_stroke,
            onChanged: b => { _stroke = b; ApplyColorToSelected(); },
            onPreview: b => { _stroke = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _freehandColorRow = MakeCompactColorRow("Color", _freehandColorPicker);
        _propertyPanel.Children.Add(_freehandColorRow);

        _arrowColorPicker = new CompactColorPicker(_stroke,
            onChanged: b => { _stroke = b; _textColor = b; ApplyColorToSelected(); },
            onPreview: b => { _stroke = b; _textColor = b; ApplyColorToSelectedNoSnapshot(); },
            onBeginCustomPick: SnapshotForUndo);
        _arrowColorRow = MakeCompactColorRow("Color", _arrowColorPicker);
        _propertyPanel.Children.Add(_arrowColorRow);

        _thicknessHeader = SectionHeader("Thickness");
        _propertyPanel.Children.Add(_thicknessHeader);
        _thicknessBox = MakeNumberBox(_thickness, ThicknessMin, ThicknessMax, v =>
        {
            _thickness = v;
            if (PrimarySelection != null && PrimarySelection.Kind != "freehand")
            {
                PrimarySelection.StrokeThickness = _thickness;
                Redraw();
            }
        });
        _propertyPanel.Children.Add(_thicknessBox);

        _cornerRadiusHeader = SectionHeader("Corner radius (rect)");
        _propertyPanel.Children.Add(_cornerRadiusHeader);
        _cornerRadiusBox = MakeNumberBox(_cornerRadius, CornerRadiusMin, CornerRadiusMax, v =>
        {
            _cornerRadius = v;
            if (PrimarySelection != null && PrimarySelection.Kind == "rect")
            {
                PrimarySelection.CornerRadius = _cornerRadius;
                Redraw();
            }
        });
        _propertyPanel.Children.Add(_cornerRadiusBox);

        _fontHeader = SectionHeader("Font");
        _propertyPanel.Children.Add(_fontHeader);
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
                if (PrimarySelection != null && PrimarySelection.Kind is "text" or "rect" or "ellipse" or "arrow")
                {
                    PrimarySelection.FontFamily = _fontFamily;
                    Redraw();
                }
            }
        };
        _propertyPanel.Children.Add(_fontFamilyCombo);

        _fontSizeHeader = SectionHeader("Font size");
        _propertyPanel.Children.Add(_fontSizeHeader);
        _fontSizeBox = MakeNumberBox(_fontSize, FontSizeMin, FontSizeMax, v =>
        {
            _fontSize = v;
            if (PrimarySelection != null && PrimarySelection.Kind is "text" or "rect" or "ellipse" or "arrow")
            {
                PrimarySelection.FontSize = _fontSize;
                Redraw();
            }
        });
        _propertyPanel.Children.Add(_fontSizeBox);

        _arrowStyleHeader = SectionHeader("Arrow style");
        _propertyPanel.Children.Add(_arrowStyleHeader);
        _arrowHeadCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var v in Enum.GetValues<ArrowHeadStyle>())
            _arrowHeadCombo.Items.Add(v.ToString());
        _arrowHeadCombo.SelectedIndex = (int)_arrowHead;
        _arrowHeadCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            _arrowHead = (ArrowHeadStyle)_arrowHeadCombo.SelectedIndex;
            if (PrimarySelection != null && PrimarySelection.Kind == "arrow")
            {
                PrimarySelection.ArrowHead = _arrowHead;
                Redraw();
            }
        };
        _propertyPanel.Children.Add(_arrowHeadCombo);
        _arrowDirectCheck = new CheckBox
        {
            Content = "Direct",
            IsChecked = _arrowDirect,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _arrowDirectCheck.Checked += (_, _) => OnArrowDirectChanged(true);
        _arrowDirectCheck.Unchecked += (_, _) => OnArrowDirectChanged(false);
        _propertyPanel.Children.Add(_arrowDirectCheck);

        _shadowCheck = new CheckBox
        {
            Content = "Shadow",
            IsChecked = _hasShadow,
            Margin = new Thickness(0, 6, 0, 4),
        };
        _shadowCheck.Checked += (_, _) => OnShadowChanged(true);
        _shadowCheck.Unchecked += (_, _) => OnShadowChanged(false);
        _propertyPanel.Children.Add(_shadowCheck);

        UpdatePropertyPanelVisibility();
    }

    private static void SetPropertySectionVisibility(UIElement? element, bool visible)
    {
        if (element == null) return;
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? GetHomogeneousSelectionKind()
    {
        if (_selection.Count == 0) return null;
        var kinds = _selection.Select(s => s.Kind).Distinct().ToList();
        return kinds.Count == 1 ? kinds[0] : null;
    }

    private DrawItem? SelectionPropertySource()
    {
        var kind = GetHomogeneousSelectionKind();
        if (kind == null) return null;
        return _selection.FirstOrDefault(s => s.Kind == kind);
    }

    private string? PropertyPanelContextKind()
    {
        var selectedKind = GetHomogeneousSelectionKind();
        if (selectedKind != null)
            return selectedKind;
        if (_tool == Tool.Select)
            return null;
        return _tool switch
        {
            Tool.Rectangle => "rect",
            Tool.Ellipse => "ellipse",
            Tool.Arrow => "arrow",
            Tool.Text => "text",
            Tool.Freehand => "freehand",
            _ => "rect",
        };
    }

    private void UpdatePropertyPanelVisibility()
    {
        if (_leftScroll != null)
            _leftScroll.IsEnabled = GetHomogeneousSelectionKind() != null || _tool != Tool.Select;
        var kind = PropertyPanelContextKind();
        if (kind == null)
        {
            SetPropertySectionVisibility(_fillColorRow, false);
            SetPropertySectionVisibility(_gradientColorRow, false);
            SetPropertySectionVisibility(_strokeColorRow, false);
            SetPropertySectionVisibility(_textColorRow, false);
            SetPropertySectionVisibility(_thicknessHeader, false);
            SetPropertySectionVisibility(_thicknessBox, false);
            SetPropertySectionVisibility(_freehandColorRow, false);
            SetPropertySectionVisibility(_arrowColorRow, false);
            SetPropertySectionVisibility(_cornerRadiusHeader, false);
            SetPropertySectionVisibility(_cornerRadiusBox, false);
            SetPropertySectionVisibility(_fontHeader, false);
            SetPropertySectionVisibility(_fontFamilyCombo, false);
            SetPropertySectionVisibility(_fontSizeHeader, false);
            SetPropertySectionVisibility(_fontSizeBox, false);
            SetPropertySectionVisibility(_arrowStyleHeader, false);
            SetPropertySectionVisibility(_arrowHeadCombo, false);
            SetPropertySectionVisibility(_arrowDirectCheck, false);
            SetPropertySectionVisibility(_shadowCheck, false);
            SetPropertySectionVisibility(_sizeHeader, false);
            SetPropertySectionVisibility(_sizeLine1, false);
            SetPropertySectionVisibility(_sizeLine2, false);
            SetPropertySectionVisibility(_variantsHeader, false);
            SetPropertySectionVisibility(_variantsPanel, false);
            _variantsPanel?.Children.Clear();
            return;
        }

        var isFreehand = kind == "freehand";
        var isArrow = kind == "arrow";
        SetPropertySectionVisibility(_fillColorRow, !isFreehand && !isArrow && kind is "rect" or "ellipse");
        SetPropertySectionVisibility(_gradientColorRow, !isFreehand && !isArrow && kind == "rect");
        SetPropertySectionVisibility(_strokeColorRow, !isFreehand && !isArrow && kind is "rect" or "ellipse");
        SetPropertySectionVisibility(_textColorRow, !isFreehand && !isArrow && kind is "rect" or "ellipse" or "text");
        SetPropertySectionVisibility(_arrowColorRow, isArrow);
        SetPropertySectionVisibility(_thicknessHeader, !isFreehand);
        SetPropertySectionVisibility(_thicknessBox, !isFreehand);
        SetPropertySectionVisibility(_freehandColorRow, isFreehand);
        SetPropertySectionVisibility(_cornerRadiusHeader, !isFreehand && !isArrow && kind == "rect");
        SetPropertySectionVisibility(_cornerRadiusBox, !isFreehand && !isArrow && kind == "rect");
        SetPropertySectionVisibility(_fontHeader, !isFreehand && kind is "text" or "rect" or "ellipse" or "arrow");
        SetPropertySectionVisibility(_fontFamilyCombo, !isFreehand && kind is "text" or "rect" or "ellipse" or "arrow");
        SetPropertySectionVisibility(_fontSizeHeader, !isFreehand && kind is "text" or "rect" or "ellipse" or "arrow");
        SetPropertySectionVisibility(_fontSizeBox, !isFreehand && kind is "text" or "rect" or "ellipse" or "arrow");
        SetPropertySectionVisibility(_arrowStyleHeader, isArrow);
        SetPropertySectionVisibility(_arrowHeadCombo, isArrow);
        SetPropertySectionVisibility(_arrowDirectCheck, isArrow);
        SetPropertySectionVisibility(_shadowCheck, kind is "rect" or "ellipse" or "text");
        RefreshSizeInfo(kind);
        RefreshVariantsSection(kind);
    }

    private void RefreshVariantsSection(string? kind)
    {
        if (_variantsPanel == null || _variantsHeader == null) return;
        _variantsPanel.Children.Clear();
        if (kind is not ("rect" or "ellipse" or "arrow" or "text" or "freehand"))
        {
            SetPropertySectionVisibility(_variantsHeader, false);
            SetPropertySectionVisibility(_variantsPanel, false);
            return;
        }

        _activeTheme.EnsureToolStyles();
        var variants = _activeTheme.GetVariants(kind);
        var tool = MapDrawKindToTool(kind);
        var activeIdx = _activeTheme.GetActiveIndex(kind);
        var hasMatchingSelection = _selection.Any(s => s.Kind == kind);
        var brushOn = new SolidColorBrush(Color.FromRgb(0xCC, 0xE0, 0xFF));
        var brushOff = SystemColors.ControlBrush;

        var anyShown = false;
        for (var i = 0; i < variants.Count; i++)
        {
            if (!variants[i].Enabled) continue;
            var captured = i;
            var st = variants[i];
            var btn = new Button
            {
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 38,
                MinHeight = 32,
                ToolTip = $"Variant {i + 1}",
                Content = BuildToolIcon(tool, st),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = (!hasMatchingSelection && i == activeIdx) ? brushOn : brushOff,
            };
            btn.Click += (_, _) => OnPropertyVariantClicked(kind, captured);
            _variantsPanel.Children.Add(btn);
            anyShown = true;
        }

        SetPropertySectionVisibility(_variantsHeader, anyShown);
        SetPropertySectionVisibility(_variantsPanel, anyShown);
    }

    private void OnPropertyVariantClicked(string kind, int variantIndex)
    {
        _activeTheme.EnsureToolStyles();
        var variants = _activeTheme.GetVariants(kind);
        if (variantIndex < 0 || variantIndex >= variants.Count) return;
        var st = variants[variantIndex];
        if (!st.Enabled) return;

        if (_selection.Any(s => s.Kind == kind))
        {
            ApplyVariantToSelection(kind, st);
            // Keep the highlight reflecting the theme's active variant for new draws; just
            // refresh visuals after applying.
            UpdatePropertyPanelVisibility();
        }
        else
        {
            SelectTool(MapDrawKindToTool(kind), variantIndex);
        }
    }

    private void ApplyVariantToSelection(string kind, ThemeToolStyle st)
    {
        if (_selection.Count == 0) return;
        SnapshotForUndo();
        var ahParsed = Enum.TryParse<ArrowHeadStyle>(st.ArrowHead, true, out var ah) ? ah : ArrowHeadStyle.Simple;
        foreach (var it in _selection.Where(i => i.Kind == kind))
        {
            switch (kind)
            {
                case "rect":
                    it.Fill = ParseBrush(st.Fill);
                    it.Stroke = ParseBrush(st.Stroke);
                    it.TextColor = ParseBrush(st.TextColor);
                    it.FontFamily = new FontFamily(st.FontFamilyName);
                    it.FontSize = st.FontSize;
                    it.StrokeThickness = st.Thickness;
                    it.CornerRadius = st.CornerRadius;
                    break;
                case "ellipse":
                    it.Fill = ParseBrush(st.Fill);
                    it.Stroke = ParseBrush(st.Stroke);
                    it.TextColor = ParseBrush(st.TextColor);
                    it.FontFamily = new FontFamily(st.FontFamilyName);
                    it.FontSize = st.FontSize;
                    it.StrokeThickness = st.Thickness;
                    break;
                case "arrow":
                    it.Stroke = ParseBrush(st.Stroke);
                    it.TextColor = ParseBrush(st.TextColor);
                    it.FontFamily = new FontFamily(st.FontFamilyName);
                    it.FontSize = st.FontSize;
                    it.StrokeThickness = st.Thickness;
                    it.ArrowHead = ahParsed;
                    break;
                case "text":
                    it.TextColor = ParseBrush(st.TextColor);
                    it.Stroke = ParseBrush(st.TextColor);
                    it.FontFamily = new FontFamily(st.FontFamilyName);
                    it.FontSize = st.FontSize;
                    it.StrokeThickness = st.Thickness;
                    break;
                case "freehand":
                    it.Stroke = ParseBrush(st.Stroke);
                    it.StrokeThickness = st.FreeThickness;
                    break;
            }
        }
        SyncFromSelected();
        Redraw();
    }

    private void RefreshSizeInfo(string? kind)
    {
        var showSize = kind is "rect" or "ellipse" or "arrow";
        SetPropertySectionVisibility(_sizeHeader, showSize);
        SetPropertySectionVisibility(_sizeLine1, showSize);
        SetPropertySectionVisibility(_sizeLine2, showSize);
        if (!showSize || _sizeLine1 == null || _sizeLine2 == null)
            return;

        var item = GetSizeDisplaySource(kind);
        if (item == null)
        {
            _sizeLine1.Text = "";
            _sizeLine2.Text = "";
            SetPropertySectionVisibility(_sizeLine1, false);
            SetPropertySectionVisibility(_sizeLine2, false);
            return;
        }

        switch (item.Kind)
        {
            case "rect":
            {
                var b = GetBounds(item);
                _sizeLine1.Text = $"Width: {Math.Round(b.Width):0} px";
                _sizeLine2.Text = $"Height: {Math.Round(b.Height):0} px";
                SetPropertySectionVisibility(_sizeLine1, true);
                SetPropertySectionVisibility(_sizeLine2, true);
                break;
            }
            case "ellipse":
            {
                var b = GetBounds(item);
                var w = b.Width;
                var h = b.Height;
                if (Math.Abs(w - h) < 1.0)
                {
                    _sizeLine1.Text = $"Diameter: {Math.Round(w):0} px";
                    _sizeLine2.Text = "";
                    SetPropertySectionVisibility(_sizeLine1, true);
                    SetPropertySectionVisibility(_sizeLine2, false);
                }
                else
                {
                    _sizeLine1.Text = $"Diameter (horizontal): {Math.Round(w):0} px";
                    _sizeLine2.Text = $"Diameter (vertical): {Math.Round(h):0} px";
                    SetPropertySectionVisibility(_sizeLine1, true);
                    SetPropertySectionVisibility(_sizeLine2, true);
                }
                break;
            }
            case "arrow":
            {
                var path = GetArrowPathPoints(item);
                var length = PathPolylineLength(path);
                var angleDeg = GetArrowPathMetrics(path).AngleDeg;
                _sizeLine1.Text = $"Length: {Math.Round(length):0} px";
                _sizeLine2.Text = $"Angle: {angleDeg:0.0}°";
                SetPropertySectionVisibility(_sizeLine1, true);
                SetPropertySectionVisibility(_sizeLine2, true);
                break;
            }
        }
    }

    private DrawItem? GetSizeDisplaySource(string? kind)
    {
        if (kind is not ("rect" or "ellipse" or "arrow"))
            return null;
        if (_isDrawing && _drawingItem != null && _drawingItem.Kind == kind)
            return _drawingItem;
        if (_tool == Tool.Select && !IsMultiSelection)
        {
            var sel = SelectionPropertySource();
            if (sel != null && sel.Kind == kind)
                return sel;
        }
        return null;
    }

    private void SyncPropertyControlsFromCurrent()
    {
        _suppressPropertyChanges = true;
        try
        {
            _fillColorPicker?.SetSelected(_fill);
            _gradientColorPicker?.SetSelected(_gradientFill);
            _strokeColorPicker?.SetSelected(_stroke);
            _textColorPicker?.SetSelected(_textColor);
            _freehandColorPicker?.SetSelected(_stroke);
            _arrowColorPicker?.SetSelected(_stroke);
            if (_shadowCheck != null) _shadowCheck.IsChecked = _hasShadow;
            if (_thicknessBox != null) _thicknessBox.Text = Math.Clamp(_thickness, ThicknessMin, ThicknessMax).ToString("0.##");
            if (_cornerRadiusBox != null) _cornerRadiusBox.Text = Math.Clamp(_cornerRadius, CornerRadiusMin, CornerRadiusMax).ToString("0.##");
            if (_fontSizeBox != null) _fontSizeBox.Text = Math.Clamp(_fontSize, FontSizeMin, FontSizeMax).ToString("0.##");
            if (_fontFamilyCombo != null)
            {
                var fname = _fontFamily.Source;
                if (!string.IsNullOrEmpty(fname) && _fontFamilyCombo.Items.Contains(fname))
                    _fontFamilyCombo.SelectedItem = fname;
            }
            if (_arrowHeadCombo != null)
                _arrowHeadCombo.SelectedIndex = (int)_arrowHead;
            if (_arrowDirectCheck != null)
                _arrowDirectCheck.IsChecked = _arrowDirect;
        }
        finally
        {
            _suppressPropertyChanges = false;
        }
    }

    private void OnShadowChanged(bool hasShadow)
    {
        if (_suppressPropertyChanges) return;
        _hasShadow = hasShadow;
        if (_selection.Count == 0) return;
        SnapshotForUndo();
        foreach (var it in _selection)
            it.HasShadow = hasShadow;
        Redraw();
    }

    private void OnArrowDirectChanged(bool direct)
    {
        if (_suppressPropertyChanges) return;
        _arrowDirect = direct;
        if (PrimarySelection == null || PrimarySelection.Kind != "arrow")
            return;
        SnapshotForUndo();
        foreach (var it in _selection.Where(i => i.Kind == "arrow"))
        {
            it.Direct = direct;
            if (direct)
                it.RouteBends.Clear();
            else
            {
                CommitArrowAnchors(it);
                ApplySimpleHeadTargetFromDrawOrder(it);
                CanonicalizeArrowEndpoints(it);
                CommitArrowAnchors(it);
                InitializeArrowRouteBends(it);
            }
        }
        Redraw();
    }

    private void SyncFromSelected()
    {
        var sel = SelectionPropertySource();
        if (sel == null) return;
        _fill = sel.Fill;
        _gradientFill = sel.Kind == "rect" ? sel.GradientFill : Brushes.Transparent;
        _hasShadow = sel.HasShadow;
        _stroke = sel.Stroke;
        _textColor = sel.TextColor;
        _thickness = sel.StrokeThickness;
        _cornerRadius = sel.CornerRadius;
        _fontSize = sel.FontSize;
        _fontFamily = sel.FontFamily;
        if (sel.Kind == "arrow")
        {
            _arrowHead = sel.ArrowHead;
            _arrowDirect = sel.Direct;
        }
        SyncPropertyControlsFromCurrent();
    }

    private void ApplyColorToSelected()
    {
        var sel = SelectionPropertySource();
        if (sel == null) return;
        SnapshotForUndo();
        ApplyColorToSelectedCore(sel);
    }

    private void ApplyColorToSelectedNoSnapshot()
    {
        var sel = SelectionPropertySource();
        if (sel == null) return;
        ApplyColorToSelectedCore(sel);
    }

    private void ApplyColorToSelectedCore(DrawItem sel)
    {
        if (sel.Kind == "rect" || sel.Kind == "ellipse")
        {
            foreach (var it in _selection.Where(i => i.Kind is "rect" or "ellipse"))
            {
                it.Fill = _fill;
                it.Stroke = _stroke;
                it.TextColor = _textColor;
                if (it.Kind == "rect")
                    it.GradientFill = _gradientFill;
            }
        }
        else if (sel.Kind == "arrow")
        {
            foreach (var it in _selection.Where(i => i.Kind is "arrow"))
            {
                it.Stroke = _stroke;
                it.TextColor = _textColor;
            }
        }
        else if (sel.Kind == "freehand")
        {
            foreach (var it in _selection.Where(i => i.Kind is "freehand"))
                it.Stroke = _stroke;
        }
        else if (sel.Kind == "text")
        {
            sel.TextColor = _textColor;
            sel.Stroke = _textColor;
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

    private static Button MakeToolbarButton(string tooltip, ToolBarTag tag, FrameworkElement icon, Action onClick)
    {
        var b = new Button
        {
            Margin = new Thickness(2),
            Padding = new Thickness(6, 4, 6, 4),
            MinWidth = 38,
            MinHeight = 32,
            ToolTip = tooltip,
            Tag = tag,
            Content = icon,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void RefreshToolBar()
    {
        if (_toolsPanel == null) return;
        _toolsPanel.Children.Clear();
        _activeTheme.EnsureToolStyles();

        _toolsPanel.Children.Add(MakeToolbarButton(
            "Select (S)",
            new ToolBarTag(Tool.Select, null, 0),
            BuildPointerIcon(),
            () => SelectTool(Tool.Select)));

        AddVariantToolButtons(Tool.Rectangle, "rect", "Rectangle (R)");
        AddVariantToolButtons(Tool.Ellipse, "ellipse", "Circle (C)");
        AddVariantToolButtons(Tool.Arrow, "arrow", "Arrow (A)");
        AddVariantToolButtons(Tool.Freehand, "freehand", "Freeform (F)");
        AddVariantToolButtons(Tool.Text, "text", "Text (T)");

        UpdateToolBarHighlight();
    }

    private void AddVariantToolButtons(Tool tool, string kind, string tooltipBase)
    {
        if (_toolsPanel == null) return;
        var variants = _activeTheme.GetVariants(kind);
        // Only the first ToolbarSlotCount variants are toolbar candidates; any extras live in the
        // property panel only.
        var slotCount = Math.Min(variants.Count, DrawingTheme.ToolbarSlotCount);
        var enabledIndexes = new List<int>();
        for (var i = 0; i < slotCount; i++)
        {
            if (variants[i].Enabled)
                enabledIndexes.Add(i);
        }
        if (enabledIndexes.Count == 0)
        {
            var fallback = _activeTheme.GetActiveIndex(kind);
            if (fallback < 0 || fallback >= slotCount) fallback = 0;
            enabledIndexes.Add(fallback);
        }

        var multi = enabledIndexes.Count > 1;
        foreach (var index in enabledIndexes)
        {
            var st = variants[index];
            var tooltip = multi ? $"{tooltipBase} — variant {index + 1}" : tooltipBase;
            var captured = index;
            _toolsPanel.Children.Add(MakeToolbarButton(
                tooltip,
                new ToolBarTag(tool, kind, captured),
                BuildToolIcon(tool, st),
                () => SelectTool(tool, captured)));
        }
    }

    private static FrameworkElement BuildToolIcon(Tool tool, ThemeToolStyle st) => tool switch
    {
        Tool.Rectangle => BuildRectIcon(ParseBrush(st.Stroke), ParseBrush(st.Fill)),
        Tool.Ellipse => BuildEllipseIcon(ParseBrush(st.Stroke), ParseBrush(st.Fill)),
        Tool.Arrow => BuildArrowIcon(ParseBrush(st.Stroke)),
        Tool.Text => BuildTextIcon(ParseBrush(st.TextColor)),
        Tool.Freehand => BuildFreeformIcon(ParseBrush(st.Stroke)),
        _ => BuildPointerIcon(),
    };

    private void UpdateToolBarHighlight()
    {
        if (_toolsPanel == null) return;
        var brushOn = new SolidColorBrush(Color.FromRgb(0xCC, 0xE0, 0xFF));
        var brushOff = SystemColors.ControlBrush;
        foreach (var child in _toolsPanel.Children)
        {
            if (child is not Button btn || btn.Tag is not ToolBarTag tag) continue;
            if (tag.Tool == Tool.Select)
                btn.Background = _tool == Tool.Select ? brushOn : brushOff;
            else
            {
                var activeIdx = _activeTheme.GetActiveIndex(tag.Kind!);
                btn.Background = _tool == tag.Tool && activeIdx == tag.VariantIndex ? brushOn : brushOff;
            }
        }
    }

    private static FrameworkElement BuildPointerIcon()
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 1,15 L 5,11 L 8,17 L 10,16 L 7,10 L 12,10 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x2F)),
            Stroke = Brushes.White,
            StrokeThickness = 0.7,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 18,
            Height = 18,
            SnapsToDevicePixels = true,
        };
        return WrapIcon(path);
    }

    private static FrameworkElement BuildFreeformIcon(Brush stroke)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,12 C 3,3 7,17 10,10 S 17,3 19,11"),
            Stroke = stroke,
            StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = null,
            Width = 20,
            Height = 18,
            SnapsToDevicePixels = true,
        };
        return WrapIcon(path);
    }

    private static FrameworkElement BuildRectIcon(Brush stroke, Brush fill)
    {
        var r = new System.Windows.Shapes.Rectangle
        {
            Width = 18,
            Height = 14,
            RadiusX = 2,
            RadiusY = 2,
            Stroke = stroke,
            StrokeThickness = 2,
            Fill = HasVisibleFill(fill) ? fill : Brushes.Transparent,
            SnapsToDevicePixels = true,
        };
        return WrapIcon(r);
    }

    private static FrameworkElement BuildEllipseIcon(Brush stroke, Brush fill)
    {
        var e = new System.Windows.Shapes.Ellipse
        {
            Width = 18,
            Height = 16,
            Stroke = stroke,
            StrokeThickness = 2,
            Fill = HasVisibleFill(fill) ? fill : Brushes.Transparent,
        };
        return WrapIcon(e);
    }

    private static FrameworkElement BuildArrowIcon(Brush stroke)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,9 L 17,9 M 12,4 L 17,9 L 12,14"),
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = null,
            Width = 20,
            Height = 18,
            SnapsToDevicePixels = true,
        };
        return WrapIcon(path);
    }

    private static FrameworkElement BuildTextIcon(Brush color)
    {
        return new TextBlock
        {
            Text = "T",
            Foreground = color,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Width = 18,
            Height = 20,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static FrameworkElement WrapIcon(FrameworkElement inner)
    {
        var grid = new Grid
        {
            Width = 22,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        inner.HorizontalAlignment = HorizontalAlignment.Center;
        inner.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(inner);
        return grid;
    }

    private static bool HasVisibleFill(Brush? brush)
    {
        if (brush is SolidColorBrush sb)
            return sb.Color.A > 0;
        return brush != null;
    }

    private static System.Windows.Media.Effects.DropShadowEffect CreateShadowEffect() => new()
    {
        Color = Colors.Black,
        BlurRadius = 10,
        ShadowDepth = 4,
        Direction = 315,
        Opacity = 0.45,
    };

    private static Brush BuildRectFill(DrawItem it)
    {
        if (!HasVisibleFill(it.GradientFill))
            return it.Fill;
        var bottom = it.Fill is SolidColorBrush sbBottom ? sbBottom.Color : Colors.Transparent;
        var top = it.GradientFill is SolidColorBrush sbTop ? sbTop.Color : Colors.Transparent;
        return new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(top, 0),
                new GradientStop(bottom, 1),
            },
        };
    }

    private static Button MakeButton(string text)
        => new()
        {
            Content = text,
            Margin = new Thickness(3),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 60,
        };

    private static Grid MakeCompactColorRow(string label, CompactColorPicker picker)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hdr = SectionHeader(label);
        hdr.VerticalAlignment = VerticalAlignment.Center;
        hdr.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(hdr, 0);
        Grid.SetColumn(picker, 1);
        row.Children.Add(hdr);
        row.Children.Add(picker);
        return row;
    }

    private static TextBlock SectionHeader(string text)
        => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 10, 0, 4),
        };

    private TextBox MakeNumberBox(double initial, double min, double max, Action<double> onValueChanged)
    {
        var box = new TextBox
        {
            Text = Math.Clamp(initial, min, max).ToString("0.##"),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 80,
        };
        box.TextChanged += (_, _) =>
        {
            if (_suppressPropertyChanges) return;
            if (double.TryParse(box.Text, out var v))
                onValueChanged(Math.Clamp(v, min, max));
        };
        box.LostFocus += (_, _) =>
        {
            if (double.TryParse(box.Text, out var v))
                box.Text = Math.Clamp(v, min, max).ToString("0.##");
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            if (!double.TryParse(box.Text, out var v)) return;
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 5 : 1;
            v = Math.Clamp(v + (e.Key == Key.Up ? step : -step), min, max);
            box.Text = v.ToString("0.##");
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };
        return box;
    }

    private void SelectTool(Tool tool, int? variantIndex = null)
    {
        if (tool == Tool.Freehand && _tool != Tool.Freehand)
            _currentFreehandGroupId = _nextFreehandGroupId++;

        if (variantIndex.HasValue && tool != Tool.Select)
        {
            _activeTheme.EnsureToolStyles();
            var kind = ToolToKind(tool);
            var variants = _activeTheme.GetVariants(kind);
            if (variantIndex.Value >= 0 && variantIndex.Value < variants.Count && variants[variantIndex.Value].Enabled)
            {
                _activeTheme.SetActiveIndex(kind, variantIndex.Value);
                _host.PersistLastDrawingThemeName(_activeTheme.Name);
            }
        }

        var hoverCleared = _hoveredAnchorShape != null && tool != Tool.Arrow;
        _hoveredAnchorShape = tool == Tool.Arrow ? _hoveredAnchorShape : null;
        _tool = tool;
        UpdateToolBarHighlight();
        _canvas.Cursor = tool == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        if (_tool == Tool.Select)
        {
            if (SelectionPropertySource() != null)
                SyncFromSelected();
        }
        else
        {
            ApplyToolProfileFromTheme(_activeTheme, _tool);
            SyncPropertyControlsFromCurrent();
        }
        UpdatePropertyPanelVisibility();
        if (hoverCleared || tool == Tool.Arrow)
            Redraw();
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
        if (ctrl && e.Key == Key.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.V)
        {
            PasteSelection();
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
                if (HasSelection)
                {
                    DeleteSelected();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                CommitTextEdit();
                ClearSelection();
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
    }

    private DrawItem? PrimarySelection => _selection.Count == 1 ? _selection.FirstOrDefault() : null;

    private bool HasSelection => _selection.Count > 0;

    private bool IsMultiSelection => _selection.Count > 1;

    private void ClearSelection()
    {
        _selection.Clear();
        UpdatePropertyPanelForSelection();
    }

    private void SetSingleSelection(DrawItem? item)
    {
        _selection.Clear();
        if (item != null)
        {
            foreach (var m in ExpandItemMembers(item))
                _selection.Add(m);
            if (item.Kind == "arrow" && !item.Direct)
            {
                CommitArrowAnchors(item);
                EnsureArrowRouteBends(item);
            }
        }
        UpdatePropertyPanelForSelection();
    }

    private void ToggleSelection(DrawItem item)
    {
        var members = ExpandItemMembers(item).ToList();
        if (members.Any(m => _selection.Contains(m)))
        {
            foreach (var m in members)
                _selection.Remove(m);
        }
        else
        {
            foreach (var m in members)
                _selection.Add(m);
        }
        UpdatePropertyPanelForSelection();
    }

    private void PruneSelection()
    {
        _selection.RemoveWhere(it => !_items.Contains(it));
        UpdatePropertyPanelForSelection();
    }

    private void UpdatePropertyPanelForSelection()
    {
        if (_leftScroll != null)
            _leftScroll.IsEnabled = GetHomogeneousSelectionKind() != null || _tool != Tool.Select;
        if (SelectionPropertySource() != null)
            SyncFromSelected();
        UpdatePropertyPanelVisibility();
    }

    private IEnumerable<DrawItem> ExpandItemMembers(DrawItem item)
    {
        if (item.Kind == "freehand" && item.FreehandGroupId != 0)
        {
            foreach (var it in _items)
            {
                if (it.Kind == "freehand" && it.FreehandGroupId == item.FreehandGroupId)
                    yield return it;
            }
            yield break;
        }
        yield return item;
    }

    private void DeleteSelected()
    {
        if (!HasSelection) return;
        SnapshotForUndo();
        foreach (var it in GetSelectionMembers().ToList())
        {
            if (IsAnchorableShape(it))
                RemoveArrowsAnchoredTo(it.Id);
            _items.Remove(it);
        }
        ClearSelection();
        Redraw();
    }

    private void CopySelection()
    {
        if (!HasSelection) return;
        _clipboard = GetSelectionMembers().Select(it => it.Clone()).ToList();
    }

    private void PasteSelection()
    {
        if (_clipboard == null || _clipboard.Count == 0) return;
        SnapshotForUndo();
        const double offset = 16;
        _selection.Clear();
        var groupMap = new Dictionary<int, int>();
        var idMap = new Dictionary<int, int>();
        var pasted = new List<DrawItem>();
        foreach (var proto in _clipboard)
        {
            var copy = proto.Clone();
            var oldId = copy.Id;
            copy.Id = 0;
            AssignItemId(copy);
            if (oldId != 0)
                idMap[oldId] = copy.Id;
            TranslateItem(copy, offset, offset);
            if (copy.Kind == "freehand" && copy.FreehandGroupId != 0)
            {
                if (!groupMap.TryGetValue(copy.FreehandGroupId, out var newId))
                {
                    newId = _nextFreehandGroupId++;
                    groupMap[copy.FreehandGroupId] = newId;
                }
                copy.FreehandGroupId = newId;
            }
            pasted.Add(copy);
        }
        foreach (var copy in pasted)
        {
            if (copy.AnchorStartShapeId is int s && idMap.TryGetValue(s, out var ns))
                copy.AnchorStartShapeId = ns;
            else if (copy.AnchorStartShapeId.HasValue)
                copy.AnchorStartShapeId = null;

            if (copy.AnchorEndShapeId is int e && idMap.TryGetValue(e, out var ne))
                copy.AnchorEndShapeId = ne;
            else if (copy.AnchorEndShapeId.HasValue)
                copy.AnchorEndShapeId = null;

            _items.Add(copy);
            _selection.Add(copy);
        }
        RefreshAllAnchoredArrows();
        UpdatePropertyPanelForSelection();
        Redraw();
    }

    private IEnumerable<DrawItem> GetSelectionMembers()
    {
        var yielded = new HashSet<DrawItem>();
        foreach (var sel in _selection.ToList())
        {
            foreach (var it in ExpandItemMembers(sel))
            {
                if (yielded.Add(it))
                    yield return it;
            }
        }
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
        PruneSelection();
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
        PruneSelection();
        Redraw();
    }

    // ---------------- Canvas interaction ----------------

    private void ApplyDefaultSizeToShape(DrawItem item)
    {
        _host.GetDrawingShapeDefaults(item.Kind, out var stdW, out var stdH);
        var p1 = item.P1;
        var dx = item.P2.X - p1.X;
        var dy = item.P2.Y - p1.Y;
        var signX = Math.Abs(dx) < 0.001 ? 1 : Math.Sign(dx);
        var signY = Math.Abs(dy) < 0.001 ? 1 : Math.Sign(dy);
        item.P2 = new Point(p1.X + signX * stdW, p1.Y + signY * stdH);
    }

    private void SnapDefaultSizedEllipseCenterY(DrawItem item)
    {
        if (item.Kind != "ellipse") return;

        var bounds = GetBounds(item);
        var centerY = bounds.Top + bounds.Height / 2;
        var startY = item.P1.Y;

        double? bestDy = null;
        foreach (var other in _items)
        {
            if (ReferenceEquals(other, item)) continue;
            var ob = GetBounds(other);
            if (ob.Height <= 0) continue;
            if (startY < ob.Top || startY > ob.Bottom) continue;
            var otherCenterY = ob.Top + ob.Height / 2;
            var dy = otherCenterY - centerY;
            if (bestDy == null || Math.Abs(dy) < Math.Abs(bestDy.Value))
                bestDy = dy;
        }

        if (!bestDy.HasValue || Math.Abs(bestDy.Value) < 0.001) return;
        item.P1 = new Point(item.P1.X, item.P1.Y + bestDy.Value);
        item.P2 = new Point(item.P2.X, item.P2.Y + bestDy.Value);
    }

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawing && _drawingItem is { Kind: "rect" or "ellipse" })
        {
            CommitTextEdit();
            var item = _drawingItem;
            ApplyDefaultSizeToShape(item);
            SnapDefaultSizedEllipseCenterY(item);
            if (_canvas.IsMouseCaptured)
                _canvas.ReleaseMouseCapture();
            _isDrawing = false;
            _drawingItem = null;
            _snapGuides = Array.Empty<DrawingSnapGuides.GuideLine>();
            SetSingleSelection(item);
            Redraw();
            StartTextEdit(item, takeSnapshot: false);
            e.Handled = true;

            // The trailing right-button-up would otherwise land on the
            // newly-added TextBox and open its built-in context menu,
            // which steals keyboard focus and kills the caret before
            // the user can type. Swallow that one event.
            MouseButtonEventHandler? upHandler = null;
            upHandler = (_, ev) =>
            {
                _canvas.PreviewMouseRightButtonUp -= upHandler!;
                ev.Handled = true;
            };
            _canvas.PreviewMouseRightButtonUp += upHandler;
            return;
        }

        SelectTool(Tool.Select);
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        CommitTextEdit();

        if (e.ClickCount == 2)
        {
            var hitDbl = HitTestTop(p);
            if (hitDbl != null && (hitDbl.Kind == "rect" || hitDbl.Kind == "ellipse" || hitDbl.Kind == "text" || hitDbl.Kind == "arrow"))
            {
                SetSingleSelection(hitDbl);
                Redraw();
                StartTextEdit(hitDbl);
                e.Handled = true;
                return;
            }
        }

        _canvas.Focus();

        if (_tool == Tool.Select)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var hit = HitTestTop(p);

            if (!IsMultiSelection && PrimarySelection != null)
            {
                var handle = GetHandleAt(PrimarySelection, p);
                if (handle >= 0)
                {
                    _activeHandle = handle;
                    _activeArrowSegment = -1;
                    _isMoving = false;
                    _moveLast = p;
                    _pendingUndoForGesture = true;
                    _canvas.CaptureMouse();
                    return;
                }

                if (TryBeginArrowSegmentDrag(PrimarySelection, p))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (hit != null)
            {
                if (ctrl)
                {
                    ToggleSelection(hit);
                    Redraw();
                    return;
                }

                if (hit.Kind == "arrow" && !hit.Direct && TryBeginArrowSegmentDrag(hit, p))
                {
                    if (!_selection.Contains(hit))
                        SetSingleSelection(hit);
                    e.Handled = true;
                    return;
                }

                if (!_selection.Contains(hit))
                    SetSingleSelection(hit);

                _activeHandle = -1;
                _activeArrowSegment = -1;
                _isMoving = true;
                _moveLast = p;
                _snapHeldDx = 0;
                _snapHeldDy = 0;
                _pendingUndoForGesture = true;
                _canvas.CaptureMouse();
                Redraw();
                return;
            }

            if (!ctrl)
                ClearSelection();

            _marqueeStart = p;
            _marqueeCurrent = p;
            _isMarqueeSelecting = true;
            _canvas.CaptureMouse();
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
                HasShadow = _hasShadow,
            };
            AssignItemId(item);
            _items.Add(item);
            SetSingleSelection(item);
            Redraw();
            StartTextEdit(item, takeSnapshot: false);
            return;
        }

        SnapshotForUndo();
        _isDrawing = true;
        _arrowDrawStartShape = null;
        _arrowDrawEndShape = null;
        _arrowDrawEndAnchorIndex = null;
        Point? arrowSnapStart = null;
        if (_tool == Tool.Arrow)
        {
            if (TryGetNearestAnchorSnap(p, out var snapShape, out var snapPoint))
            {
                _arrowDrawStartShape = snapShape;
                arrowSnapStart = snapPoint;
            }
            else
                _arrowDrawStartShape = HitTestAnchorableShape(p);
        }
        _drawingItem = _tool switch
        {
            Tool.Rectangle => new DrawItem
            {
                Kind = "rect",
                P1 = p, P2 = p,
                Fill = _fill, GradientFill = _gradientFill, Stroke = _stroke,
                HasShadow = _hasShadow,
                StrokeThickness = _thickness,
                CornerRadius = _cornerRadius,
                FontSize = _fontSize, FontFamily = _fontFamily, TextColor = _textColor,
            },
            Tool.Ellipse => new DrawItem
            {
                Kind = "ellipse",
                P1 = p, P2 = p,
                Fill = _fill, Stroke = _stroke,
                HasShadow = _hasShadow,
                StrokeThickness = _thickness,
                FontSize = _fontSize, FontFamily = _fontFamily, TextColor = _textColor,
            },
            Tool.Arrow => new DrawItem
            {
                Kind = "arrow",
                P1 = p, P2 = p,
                Stroke = _stroke,
                StrokeThickness = Math.Max(1.5, _thickness),
                ArrowHead = _arrowHead,
                Direct = _arrowDirect,
                FontSize = _fontSize,
                FontFamily = _fontFamily,
                TextColor = _textColor,
            },
            Tool.Freehand => new DrawItem
            {
                Kind = "freehand",
                P1 = p, P2 = p,
                Stroke = _stroke,
                StrokeThickness = Math.Max(3, _freeThickness),
                Points = new List<Point> { p },
                FreehandGroupId = _currentFreehandGroupId,
            },
            _ => null,
        };

        if (_drawingItem != null)
        {
            AssignItemId(_drawingItem);
            if (_drawingItem.Kind == "arrow" && _arrowDrawStartShape != null)
            {
                _drawingItem.AnchorStartShapeId = _arrowDrawStartShape.Id;
                var startPt = arrowSnapStart ?? p;
                _drawingItem.AnchorStartIndex = GetNearestAnchorIndex(_arrowDrawStartShape, startPt);
                _drawingItem.P1 = GetShapeAnchorPointByIndex(_arrowDrawStartShape, _drawingItem.AnchorStartIndex.Value);
            }
            _items.Add(_drawingItem);
            SetSingleSelection(_drawingItem);
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
                if (_drawingItem.Points.Count == 0 || (_drawingItem.Points[^1] - p).Length > 0.75)
                    _drawingItem.Points.Add(p);
            }
            else if (_drawingItem.Kind is "rect" or "ellipse")
            {
                _host.GetDrawingShapeDefaults(_drawingItem.Kind, out var stdW, out var stdH);
                _drawingItem.P2 = DrawingShapeSizeSnap.SnapDragCorner(
                    _drawingItem.P1,
                    p,
                    stdW,
                    stdH,
                    _host.DrawingShapeSizeSnapThresholdPx);
            }
            else if (_drawingItem.Kind == "arrow")
            {
                _drawingItem.P2 = ResolveArrowEndpoint(
                    p, _arrowDrawEndShape, _arrowDrawEndAnchorIndex,
                    out _arrowDrawEndShape, out _arrowDrawEndAnchorIndex);
                if (_arrowDrawStartShape != null && _drawingItem.AnchorStartIndex is int startIdx)
                    _drawingItem.P1 = GetShapeAnchorPointByIndex(_arrowDrawStartShape, startIdx);
            }
            else
            {
                _drawingItem.P2 = p;
            }
            if (_drawingItem.Kind is "rect" or "ellipse" or "arrow")
                RefreshSizeInfo(_drawingItem.Kind);
            Redraw();
            return;
        }

        if (_isMarqueeSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            _marqueeCurrent = p;
            Redraw();
            return;
        }

        if (_tool == Tool.Select && PrimarySelection != null && _activeHandle >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            if (_pendingUndoForGesture)
            {
                SnapshotForUndo();
                _pendingUndoForGesture = false;
            }
            ResizeSelectedByHandle(PrimarySelection, _activeHandle, p);
            RefreshSizeInfo(PrimarySelection.Kind);
            Redraw();
            return;
        }

        if (_tool == Tool.Select && PrimarySelection != null && _activeArrowSegment >= 0
            && (e.LeftButton == MouseButtonState.Pressed || Mouse.Captured == _canvas))
        {
            if (_pendingUndoForGesture)
            {
                SnapshotForUndo();
                _pendingUndoForGesture = false;
            }
            DragArrowSegment(PrimarySelection, _activeArrowSegment, p);
            RefreshSizeInfo(PrimarySelection.Kind);
            Redraw();
            e.Handled = true;
            return;
        }

        if (_tool == Tool.Select && _isMoving && HasSelection && e.LeftButton == MouseButtonState.Pressed)
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
                var moving = GetSelectionBounds();
                var selected = GetSelectionMembers().ToHashSet();
                var others = _items.Where(i => !selected.Contains(i)).Select(GetBounds).ToList();
                var step = DrawingSnapGuides.StickyStep(dx, dy, _snapHeldDx, _snapHeldDy, moving, others);
                _snapHeldDx = step.NextHeldDx;
                _snapHeldDy = step.NextHeldDy;
                _snapGuides = DrawingSnapGuides.IsSnapEngaged(_snapHeldDx, _snapHeldDy)
                    ? step.Guides
                    : Array.Empty<DrawingSnapGuides.GuideLine>();
                foreach (var it in GetSelectionMembers())
                    TranslateItem(it, step.Dx, step.Dy);
                _moveLast = p;
                if (GetHomogeneousSelectionKind() is { } moveKind
                    && moveKind is "rect" or "ellipse" or "arrow")
                {
                    RefreshSizeInfo(moveKind);
                }
                Redraw();
            }
        }
        else if (!_isMoving)
        {
            _snapGuides = Array.Empty<DrawingSnapGuides.GuideLine>();
        }

        if (_tool == Tool.Select && PrimarySelection != null && !IsMultiSelection)
        {
            var h = GetHandleAt(PrimarySelection, p);
            if (h >= 0)
                _canvas.Cursor = CursorForHandle(PrimarySelection, h);
            else if (PrimarySelection.Kind == "arrow" && !PrimarySelection.Direct)
            {
                var seg = GetArrowSegmentHandleAt(PrimarySelection, p);
                if (seg >= 0)
                {
                    Cursor? segCursor = null;
                    foreach (var handle in GetArrowSegmentHandles(PrimarySelection))
                    {
                        if (handle.SegmentIndex != seg) continue;
                        segCursor = CursorForArrowSegment(handle.IsHorizontal);
                        break;
                    }
                    _canvas.Cursor = segCursor ?? Cursors.Arrow;
                }
                else
                    _canvas.Cursor = HitTestTop(p) != null ? Cursors.SizeAll : Cursors.Arrow;
            }
            else
                _canvas.Cursor = HitTestTop(p) != null ? Cursors.SizeAll : Cursors.Arrow;
        }
        else if (_tool == Tool.Select && HasSelection)
        {
            _canvas.Cursor = HitTestTop(p) != null ? Cursors.SizeAll : Cursors.Arrow;
        }
        else if (_tool != Tool.Select)
        {
            _canvas.Cursor = Cursors.Cross;
        }
        else
        {
            _canvas.Cursor = Cursors.Arrow;
        }

        if (_tool == Tool.Arrow)
        {
            var hovered = HitTestAnchorableShape(p);
            if (!ReferenceEquals(hovered, _hoveredAnchorShape))
            {
                _hoveredAnchorShape = hovered;
                Redraw();
            }
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();

        if (_isMarqueeSelecting)
        {
            _isMarqueeSelecting = false;
            var rect = NormalizeRect(_marqueeStart, _marqueeCurrent);
            if (rect.Width >= 3 || rect.Height >= 3)
            {
                var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (!ctrl)
                    _selection.Clear();
                foreach (var it in _items)
                {
                    if (!BoundsIntersect(rect, it)) continue;
                    foreach (var m in ExpandItemMembers(it))
                        _selection.Add(m);
                }
                UpdatePropertyPanelForSelection();
            }
            Redraw();
        }

        if (_isDrawing && _drawingItem != null)
        {
            var b = GetBounds(_drawingItem);
            if (_drawingItem.Kind != "freehand" && _drawingItem.Kind != "arrow" && (b.Width < 3 || b.Height < 3))
            {
                _items.Remove(_drawingItem);
                _selection.Remove(_drawingItem);
            }
            else if (_drawingItem.Kind == "arrow")
            {
                var dx = _drawingItem.P2.X - _drawingItem.P1.X;
                var dy = _drawingItem.P2.Y - _drawingItem.P1.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 4)
                {
                    _items.Remove(_drawingItem);
                    _selection.Remove(_drawingItem);
                }
                else
                {
                    if (_arrowDrawStartShape != null)
                        _drawingItem.AnchorStartShapeId = _arrowDrawStartShape.Id;
                    if (_arrowDrawEndShape != null)
                    {
                        _drawingItem.AnchorEndShapeId = _arrowDrawEndShape.Id;
                        _drawingItem.AnchorEndIndex = _arrowDrawEndAnchorIndex;
                    }
                    else
                    {
                        var endShape = HitTestAnchorableShape(_drawingItem.P2)
                            ?? FindShapeOwningAnchorPoint(_drawingItem.P2);
                        _drawingItem.AnchorEndShapeId = endShape?.Id;
                    }
                    CommitArrowAnchors(_drawingItem);
                    ApplySimpleHeadTargetFromDrawOrder(_drawingItem);
                    if (!_drawingItem.Direct)
                    {
                        CanonicalizeArrowEndpoints(_drawingItem);
                        CommitArrowAnchors(_drawingItem);
                        InitializeArrowRouteBends(_drawingItem);
                    }
                }
            }
            else if (_drawingItem.Kind == "freehand")
            {
                if (_drawingItem.Points.Count < 2 || DrawingFreehandGeometry.PathLength(_drawingItem.Points) < 4)
                {
                    _items.Remove(_drawingItem);
                    _selection.Remove(_drawingItem);
                }
            }
        }
        var editedArrowEndpoint = _tool == Tool.Select
            && PrimarySelection?.Kind == "arrow"
            && _activeHandle >= 0;
        var editedArrowSegment = _tool == Tool.Select
            && PrimarySelection?.Kind == "arrow"
            && _activeArrowSegment >= 0;

        _isDrawing = false;
        _drawingItem = null;
        _arrowDrawStartShape = null;
        _arrowDrawEndShape = null;
        _arrowDrawEndAnchorIndex = null;
        _isMoving = false;
        _snapHeldDx = 0;
        _snapHeldDy = 0;
        _activeHandle = -1;
        _activeArrowSegment = -1;
        _pendingUndoForGesture = false;
        _snapGuides = Array.Empty<DrawingSnapGuides.GuideLine>();

        if (PrimarySelection?.Kind == "arrow")
        {
            if (editedArrowEndpoint)
                CommitArrowAnchors(PrimarySelection);
            if (editedArrowEndpoint || editedArrowSegment)
                SyncArrowRouteBendsToAnchors(PrimarySelection);
        }

        Redraw();
    }

    // ---------------- Middle-mouse pan ----------------

    private void CanvasScroll_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _canvasScroll == null) return;
        _isPanning = true;
        _panStartScreen = e.GetPosition(_canvasScroll);
        _panStartHOffset = _canvasScroll.HorizontalOffset;
        _panStartVOffset = _canvasScroll.VerticalOffset;
        _canvasScroll.CaptureMouse();
        Mouse.OverrideCursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void CanvasScroll_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || _canvasScroll == null) return;
        var p = e.GetPosition(_canvasScroll);
        _canvasScroll.ScrollToHorizontalOffset(_panStartHOffset - (p.X - _panStartScreen.X));
        _canvasScroll.ScrollToVerticalOffset(_panStartVOffset - (p.Y - _panStartScreen.Y));
        e.Handled = true;
    }

    private void CanvasScroll_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle) return;
        _isPanning = false;
        _canvasScroll?.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;
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
                if (it.Points.Count == 1)
                    return new Rect(it.Points[0], new Size(0, 0));
                var geomBounds = DrawingFreehandGeometry.CreateSmoothGeometry(it.Points).Bounds;
                return geomBounds.IsEmpty
                    ? new Rect(it.Points[0], new Size(0, 0))
                    : geomBounds;
        }
        return Rect.Empty;
    }

    private static void TranslateItem(DrawItem it, double dx, double dy)
    {
        it.P1 = new Point(it.P1.X + dx, it.P1.Y + dy);
        it.P2 = new Point(it.P2.X + dx, it.P2.Y + dy);
        if (it.Kind == "arrow")
        {
            for (var i = 0; i < it.RouteBends.Count; i++)
                it.RouteBends[i] = new Point(it.RouteBends[i].X + dx, it.RouteBends[i].Y + dy);
        }
        if (it.Kind == "freehand")
        {
            for (var i = 0; i < it.Points.Count; i++)
                it.Points[i] = new Point(it.Points[i].X + dx, it.Points[i].Y + dy);
        }
    }

    private DrawItem? HitTestTop(Point p)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (HitTest(it, p))
                return it;
        }
        return null;
    }

    private bool HitTest(DrawItem it, Point p)
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
            {
                var tol = Math.Max(6, it.StrokeThickness + 4);
                var path = GetArrowPathPoints(it);
                return HitTestArrowPath(p, path, tol);
            }
            case "freehand":
            {
                var outline = DrawingFreehandGeometry.Flatten(it.Points);
                var tol = Math.Max(6, it.StrokeThickness + 2);
                for (var i = 1; i < outline.Count; i++)
                {
                    if (PointNearSegment(p, outline[i - 1], outline[i], tol))
                        return true;
                }
                return false;
            }
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

    private void ResizeSelectedByHandle(DrawItem it, int handle, Point p)
    {
        if (it.Kind == "arrow")
        {
            if (handle == 0) MoveArrowEndpoint(it, isStart: true, p, preserveAnchorLock: false);
            else if (handle == 1)
            {
                MoveArrowEndpoint(it, isStart: false, p, preserveAnchorLock: false);
                if (it.Direct)
                    it.SimpleHeadAtP2 = true;
            }
            SyncArrowRouteBendsToAnchors(it);
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
        var skipAnchorRefresh = _tool == Tool.Select
            && (_activeHandle >= 0 || _activeArrowSegment >= 0)
            && PrimarySelection != null;
        RefreshAllAnchoredArrows(skipAnchorRefresh ? PrimarySelection : null);
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
            else if (_editingItem.Kind == "ellipse")
            {
                var innerW = Math.Max(40, b.Width * EllipseInscribedFactor - 8);
                var innerH = Math.Max(20, b.Height * EllipseInscribedFactor - 8);
                Canvas.SetLeft(preservedEditor, b.Left + (b.Width - innerW) / 2);
                Canvas.SetTop(preservedEditor, b.Top + (b.Height - innerH) / 2);
                preservedEditor.Width = innerW;
                preservedEditor.Height = innerH;
            }
            else if (_editingItem.Kind == "arrow")
            {
                var path = GetArrowPathPoints(_editingItem);
                var labelText = preservedEditor is TextBox tb ? tb.Text : _editingItem.Text;
                var size = MeasureArrowLabel(_editingItem, labelText, _canvas, path);
                var (mid, angleDeg, _) = GetArrowPathMetrics(path);
                LayoutArrowLabelElement(preservedEditor, mid, angleDeg, size);
            }
            if (_editingItem.Kind is "rect" or "ellipse")
                ApplyShapeTextEditorChrome(preservedEditor, _editingItem);
            _canvas.Children.Add(preservedEditor);
        }

        if (!_isDrawing)
        {
            foreach (var it in _selection.ToList())
            {
                if (!_items.Contains(it)) continue;
                var showHandles = !IsMultiSelection && ReferenceEquals(it, PrimarySelection);
                RenderSelectionAdorner(it, showHandles);
            }
        }

        if (_isMarqueeSelecting)
            RenderMarqueeRect();

        if (_snapGuides.Count > 0)
            RenderSnapGuides();

        if (_tool == Tool.Arrow && _hoveredAnchorShape != null)
            RenderShapeAnchorPoints(_hoveredAnchorShape);

        if (_tool == Tool.Select
            && _activeHandle >= 0
            && PrimarySelection?.Kind == "arrow")
        {
            foreach (var it in _items)
            {
                if (IsAnchorableShape(it))
                    RenderShapeAnchorPoints(it);
            }
        }
    }

    private Rect GetSelectionBounds()
    {
        var first = true;
        var left = 0d;
        var top = 0d;
        var right = 0d;
        var bottom = 0d;
        foreach (var it in GetSelectionMembers())
        {
            var b = GetBounds(it);
            if (b.Width <= 0 && b.Height <= 0) continue;
            if (first)
            {
                left = b.Left;
                top = b.Top;
                right = b.Right;
                bottom = b.Bottom;
                first = false;
            }
            else
            {
                left = Math.Min(left, b.Left);
                top = Math.Min(top, b.Top);
                right = Math.Max(right, b.Right);
                bottom = Math.Max(bottom, b.Bottom);
            }
        }
        return first ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    private void RenderSnapGuides()
    {
        var alignBrush = new SolidColorBrush(Colors.Black);
        var spaceBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
        var alignDash = new DoubleCollection { 4, 4 };
        foreach (var g in _snapGuides)
        {
            if (g.IsSpacing)
                RenderSpacingBracket(g, spaceBrush);
            else
                RenderAlignmentGuide(g, alignBrush, alignDash);
        }
    }

    private void RenderAlignmentGuide(DrawingSnapGuides.GuideLine g, Brush brush, DoubleCollection dash)
    {
        if (g.IsVertical)
        {
            AddSnapLine(g.Position, g.Start, g.Position, g.End, brush, dash);
            return;
        }
        AddSnapLine(g.Start, g.Position, g.End, g.Position, brush, dash);
    }

    /// <summary>|------| dimension bracket for equal-spacing snap (below or beside the gap).</summary>
    private void RenderSpacingBracket(DrawingSnapGuides.GuideLine g, Brush brush)
    {
        var cap = DrawingSnapGuides.SpacingCapLength;
        if (!g.IsVertical)
        {
            var y = g.Position;
            var x1 = Math.Min(g.Start, g.End);
            var x2 = Math.Max(g.Start, g.End);
            if (x2 - x1 < 2) return;
            AddSnapLine(x1, y, x2, y, brush);
            AddSnapLine(x1, y, x1, y - cap, brush);
            AddSnapLine(x2, y, x2, y - cap, brush);
            return;
        }

        var x = g.Position;
        var y1 = Math.Min(g.Start, g.End);
        var y2 = Math.Max(g.Start, g.End);
        if (y2 - y1 < 2) return;
        AddSnapLine(x, y1, x, y2, brush);
        AddSnapLine(x, y1, x - cap, y1, brush);
        AddSnapLine(x, y2, x - cap, y2, brush);
    }

    private void AddSnapLine(double x1, double y1, double x2, double y2, Brush brush, DoubleCollection? dash = null)
    {
        _overlay.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 1,
            StrokeDashArray = dash,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square,
            IsHitTestVisible = false,
        });
    }

    private void RenderMarqueeRect()
    {
        var rect = NormalizeRect(_marqueeStart, _marqueeCurrent);
        var accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        var box = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = accent,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x21, 0x96, 0xF3)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(box, rect.Left);
        Canvas.SetTop(box, rect.Top);
        _overlay.Children.Add(box);
    }

    private static Rect NormalizeRect(Point a, Point b)
        => new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));

    private static bool BoundsIntersect(Rect rect, DrawItem it)
    {
        var b = GetBounds(it);
        b.Inflate(HitPadding, HitPadding);
        return rect.IntersectsWith(b);
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
                    Fill = BuildRectFill(it),
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    RadiusX = it.CornerRadius,
                    RadiusY = it.CornerRadius,
                    IsHitTestVisible = false,
                };
                if (it.HasShadow) rect.Effect = CreateShadowEffect();
                Canvas.SetLeft(rect, b.Left);
                Canvas.SetTop(rect, b.Top);
                surface.Children.Add(rect);

                if (!string.IsNullOrEmpty(it.Text) && !ReferenceEquals(it, _editingItem))
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
                if (it.HasShadow) el.Effect = CreateShadowEffect();
                Canvas.SetLeft(el, b.Left);
                Canvas.SetTop(el, b.Top);
                surface.Children.Add(el);

                if (!string.IsNullOrEmpty(it.Text) && !ReferenceEquals(it, _editingItem))
                {
                    var innerWidth = Math.Max(0, b.Width * EllipseInscribedFactor - 8);
                    var tb = new TextBlock
                    {
                        Text = it.Text,
                        FontSize = it.FontSize,
                        FontFamily = it.FontFamily,
                        Foreground = it.TextColor,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Width = innerWidth,
                        IsHitTestVisible = false,
                    };
                    tb.Measure(new Size(innerWidth, b.Height));
                    var th = tb.DesiredSize.Height;
                    Canvas.SetLeft(tb, b.Left + (b.Width - innerWidth) / 2);
                    Canvas.SetTop(tb, b.Top + Math.Max(0, (b.Height - th) / 2));
                    surface.Children.Add(tb);
                }
                break;
            }
            case "arrow":
            {
                RenderArrowShaft(it, surface);
                AddArrowHeads(it, surface);
                RenderArrowLabel(it, surface);
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
                if (it.HasShadow) tb.Effect = CreateShadowEffect();
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
                surface.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = DrawingFreehandGeometry.CreateSmoothGeometry(it.Points),
                    Stroke = it.Stroke,
                    StrokeThickness = it.StrokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                });
                break;
            }
        }
    }

    private void AddArrowHeads(DrawItem it, Canvas surface)
    {
        if (it.ArrowHead == ArrowHeadStyle.None) return;
        var path = GetArrowPathPoints(it);
        if (path.Count < 2) return;

        var headSize = Math.Max(10, it.StrokeThickness * 4);
        var endFrom = path[^2];
        var endTo = path[^1];
        if (it.ArrowHead == ArrowHeadStyle.Double)
        {
            DrawHead(surface, endFrom, endTo, it.Stroke, it.StrokeThickness, headSize);
            DrawHead(surface, path[1], path[0], it.Stroke, it.StrokeThickness, headSize);
            return;
        }
        if (it.SimpleHeadAtP2)
            DrawHead(surface, endFrom, endTo, it.Stroke, it.StrokeThickness, headSize);
        else
            DrawHead(surface, path[1], path[0], it.Stroke, it.StrokeThickness, headSize);
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

    private void RenderSelectionAdorner(DrawItem it, bool showHandles)
    {
        var b = GetBounds(it);
        var accent = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));

        if (it.Kind is "freehand" or "text")
            return;

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
        else if (it.Direct)
        {
            var line = new Line
            {
                X1 = it.P1.X, Y1 = it.P1.Y, X2 = it.P2.X, Y2 = it.P2.Y,
                Stroke = accent,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            };
            _overlay.Children.Add(line);
        }
        else
        {
            var path = GetArrowPathPoints(it);
            for (var i = 0; i < path.Count - 1; i++)
            {
                _overlay.Children.Add(new Line
                {
                    X1 = path[i].X,
                    Y1 = path[i].Y,
                    X2 = path[i + 1].X,
                    Y2 = path[i + 1].Y,
                    Stroke = accent,
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    IsHitTestVisible = false,
                });
            }
        }

        if (!showHandles) return;

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

        if (it.Kind == "arrow" && !it.Direct)
            RenderArrowSegmentHandles(it);
    }

    // ---------------- Text editor ----------------

    private static void ApplyShapeTextEditorChrome(TextBox box, DrawItem it)
    {
        box.Background = Brushes.Transparent;
        if (it.Kind == "ellipse" && box.Width > 0 && box.Height > 0)
        {
            var w = box.Width;
            var h = box.Height;
            box.Clip = new EllipseGeometry(new Point(w / 2, h / 2), w / 2, h / 2);
        }
        else
            box.Clip = null;
    }

    private void StartTextEdit(DrawItem it, bool takeSnapshot = true)
    {
        CommitTextEdit();
        if (takeSnapshot)
            SnapshotForUndo();
        _editChangedAnything = false;
        _editingItem = it;
        var b = GetBounds(it);
        var isFreshTextItem = it.Kind == "text" && string.IsNullOrEmpty(it.Text);
        var isShapeContainer = it.Kind == "rect" || it.Kind == "ellipse";
        var isArrowLabel = it.Kind == "arrow";

        Brush boxBackground;
        Brush boxBorder;
        Thickness boxBorderThickness;
        if (isShapeContainer || isArrowLabel)
        {
            boxBackground = Brushes.Transparent;
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
            TextWrapping = isShapeContainer ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalContentAlignment = isShapeContainer || isArrowLabel ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalContentAlignment = isShapeContainer || isArrowLabel ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
        double left, top, w, h;
        if (it.Kind == "rect")
        {
            left = b.Left + 4;
            top = b.Top + 4;
            w = Math.Max(40, b.Width - 8);
            h = Math.Max(20, b.Height - 8);
        }
        else if (it.Kind == "ellipse")
        {
            var innerW = Math.Max(40, b.Width * EllipseInscribedFactor - 8);
            var innerH = Math.Max(20, b.Height * EllipseInscribedFactor - 8);
            left = b.Left + (b.Width - innerW) / 2;
            top = b.Top + (b.Height - innerH) / 2;
            w = innerW;
            h = innerH;
        }
        else if (isArrowLabel)
        {
            var path = GetArrowPathPoints(it);
            var size = MeasureArrowLabel(it, it.Text, _canvas, path);
            left = 0;
            top = 0;
            w = size.Width;
            h = size.Height;
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
        if (isArrowLabel)
        {
            var path = GetArrowPathPoints(it);
            var (mid, angleDeg, _) = GetArrowPathMetrics(path);
            LayoutArrowLabelElement(box, mid, angleDeg, new Size(w, h));
        }
        else
        {
            Canvas.SetLeft(box, left);
            Canvas.SetTop(box, top);
            box.Width = w;
            box.Height = h;
        }
        if (isShapeContainer)
            ApplyShapeTextEditorChrome(box, it);

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
            else if (it.Kind == "ellipse")
            {
                var availWidth = Math.Max(40, GetBounds(it).Width * EllipseInscribedFactor - 8);
                var ft = new FormattedText(
                    string.IsNullOrEmpty(text) ? " " : text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, box.FontSize, Brushes.Black, dpi)
                {
                    MaxTextWidth = availWidth,
                };
                var neededEllipseHeight = (ft.Height + 16) / EllipseInscribedFactor;
                var currentEllipseHeight = GetBounds(it).Height;
                if (neededEllipseHeight > currentEllipseHeight)
                {
                    it.P2 = new Point(it.P2.X, it.P1.Y + neededEllipseHeight);
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
            else if (it.Kind == "arrow")
            {
                var path = GetArrowPathPoints(it);
                var size = MeasureArrowLabel(it, text, box, path);
                var (mid, angleDeg, _) = GetArrowPathMetrics(path);
                LayoutArrowLabelElement(box, mid, angleDeg, size);
                Redraw();
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
            _selection.Remove(item);
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

        if (!TryRenderPngBytes(out var bytes, out var error))
        {
            MessageBox.Show(this, error, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertIntoEditor()
    {
        if (_items.Count == 0)
        {
            MessageBox.Show(this, "Nothing to insert — the canvas is empty.",
                "Insert drawing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryRenderPngBytes(out var pngBytes, out var error))
        {
            MessageBox.Show(this, error, "Insert drawing", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var workspaceJson = JsonSerializer.Serialize(BuildWorkspaceDto(),
            new JsonSerializerOptions { WriteIndented = true });

        var ok = _host.InsertDrawingIntoActiveEditor(pngBytes, workspaceJson, _editContext);
        if (!ok)
            return;

        Close();
    }

    private bool TryRenderPngBytes(out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = string.Empty;

        var prevSelection = _selection.ToHashSet();
        ClearSelection();
        Redraw();
        try
        {
            var size = new Size(_canvas.Width, _canvas.Height);
            _canvas.Measure(size);
            _canvas.Arrange(new Rect(size));
            var rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_canvas);

            BitmapSource source = rtb;
            var crop = ComputeContentCropRect(size);
            if (crop.Width > 0 && crop.Height > 0
                && (crop.Width < (int)size.Width || crop.Height < (int)size.Height))
            {
                source = new CroppedBitmap(rtb, crop);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            bytes = ms.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            _selection.Clear();
            foreach (var it in prevSelection)
                _selection.Add(it);
            UpdatePropertyPanelForSelection();
            Redraw();
        }
    }

    /// <summary>Union of item bounding boxes, inflated by stroke thickness + a small padding, clamped to the canvas.
    /// Used to crop the rendered PNG so it isn't padded with empty whitespace from the working area.</summary>
    private Int32Rect ComputeContentCropRect(Size canvasSize)
    {
        if (_items.Count == 0)
            return new Int32Rect(0, 0, (int)canvasSize.Width, (int)canvasSize.Height);

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        double maxStroke = 0;
        bool anyValid = false;
        foreach (var it in _items)
        {
            var b = GetBounds(it);
            if (b.IsEmpty) continue;
            anyValid = true;
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
            if (it.StrokeThickness > maxStroke) maxStroke = it.StrokeThickness;
        }
        if (!anyValid)
            return new Int32Rect(0, 0, (int)canvasSize.Width, (int)canvasSize.Height);

        double pad = InsertPaddingPx + maxStroke / 2 + 2;
        double left = Math.Max(0, Math.Floor(minX - pad));
        double top = Math.Max(0, Math.Floor(minY - pad));
        double right = Math.Min(canvasSize.Width, Math.Ceiling(maxX + pad));
        double bottom = Math.Min(canvasSize.Height, Math.Ceiling(maxY + pad));
        int w = (int)Math.Max(1, right - left);
        int h = (int)Math.Max(1, bottom - top);
        return new Int32Rect((int)left, (int)top, w, h);
    }

    private DrawingWorkspaceDto BuildWorkspaceDto()
    {
        var dto = new DrawingWorkspaceDto
        {
            CanvasWidth = _canvas.Width,
            CanvasHeight = _canvas.Height,
        };
        foreach (var it in _items)
            dto.Items.Add(ItemToDto(it));
        return dto;
    }

    private static DrawItemDto ItemToDto(DrawItem it)
    {
        var dto = new DrawItemDto
        {
            Kind = it.Kind,
            P1X = it.P1.X,
            P1Y = it.P1.Y,
            P2X = it.P2.X,
            P2Y = it.P2.Y,
            CornerRadius = it.CornerRadius,
            Fill = BrushToHex(it.Fill),
            GradientFill = BrushToHex(it.GradientFill),
            HasShadow = it.HasShadow,
            Stroke = BrushToHex(it.Stroke),
            StrokeThickness = it.StrokeThickness,
            Text = it.Text,
            FontSize = it.FontSize,
            FontFamilyName = it.FontFamily.Source,
            TextColor = BrushToHex(it.TextColor),
            ArrowHead = it.ArrowHead.ToString(),
            Direct = it.Direct,
            SimpleHeadAtP2 = it.SimpleHeadAtP2,
            FreehandGroupId = it.FreehandGroupId,
            Id = it.Id,
            AnchorStartShapeId = it.AnchorStartShapeId,
            AnchorEndShapeId = it.AnchorEndShapeId,
            AnchorStartIndex = it.AnchorStartIndex,
            AnchorEndIndex = it.AnchorEndIndex,
        };
        foreach (var p in it.Points)
        {
            dto.PointsX.Add(p.X);
            dto.PointsY.Add(p.Y);
        }
        foreach (var p in it.RouteBends)
        {
            dto.RouteBendsX.Add(p.X);
            dto.RouteBendsY.Add(p.Y);
        }
        return dto;
    }

    private static DrawItem ItemFromDto(DrawItemDto dto)
    {
        var item = new DrawItem
        {
            Kind = string.IsNullOrEmpty(dto.Kind) ? "rect" : dto.Kind,
            P1 = new Point(dto.P1X, dto.P1Y),
            P2 = new Point(dto.P2X, dto.P2Y),
            CornerRadius = dto.CornerRadius,
            Fill = ParseBrush(dto.Fill),
            GradientFill = ParseBrush(dto.GradientFill),
            HasShadow = dto.HasShadow,
            Stroke = ParseBrush(dto.Stroke),
            StrokeThickness = dto.StrokeThickness,
            Text = dto.Text ?? "",
            FontSize = dto.FontSize > 0
                ? dto.FontSize
                : (string.Equals(dto.Kind, "arrow", StringComparison.OrdinalIgnoreCase)
                    ? DrawingTheme.DefaultArrowLabelFontSize
                    : 22),
            FontFamily = new FontFamily(string.IsNullOrWhiteSpace(dto.FontFamilyName) ? "Segoe UI" : dto.FontFamilyName),
            TextColor = ParseBrush(dto.TextColor),
            ArrowHead = Enum.TryParse<ArrowHeadStyle>(dto.ArrowHead, true, out var ah) ? ah : ArrowHeadStyle.Simple,
            Direct = dto.Direct,
            SimpleHeadAtP2 = dto.SimpleHeadAtP2,
            FreehandGroupId = dto.FreehandGroupId,
            Id = dto.Id,
            AnchorStartShapeId = dto.AnchorStartShapeId,
            AnchorEndShapeId = dto.AnchorEndShapeId,
            AnchorStartIndex = dto.AnchorStartIndex,
            AnchorEndIndex = dto.AnchorEndIndex,
        };
        int pointCount = Math.Min(dto.PointsX.Count, dto.PointsY.Count);
        for (int i = 0; i < pointCount; i++)
            item.Points.Add(new Point(dto.PointsX[i], dto.PointsY[i]));
        int bendCount = Math.Min(dto.RouteBendsX.Count, dto.RouteBendsY.Count);
        for (int i = 0; i < bendCount; i++)
            item.RouteBends.Add(new Point(dto.RouteBendsX[i], dto.RouteBendsY[i]));
        return item;
    }

    private void SizeWindowToFitItems()
    {
        if (_items.Count == 0) return;

        double maxX = 0, maxY = 0;
        foreach (var it in _items)
        {
            var b = GetBounds(it);
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }

        const double itemPadding = 24;
        const double extraWidth = 230 + 22 + 16;
        const double extraHeight = 46 + 22 + 38;

        var desiredW = maxX + itemPadding + extraWidth;
        var desiredH = maxY + itemPadding + extraHeight;

        var workArea = SystemParameters.WorkArea;
        desiredW = Math.Clamp(desiredW, MinWidth, workArea.Width);
        desiredH = Math.Clamp(desiredH, MinHeight, workArea.Height);

        Width = desiredW;
        Height = desiredH;
    }

    private void LoadWorkspace(DrawingWorkspaceDto workspace)
    {
        _items.Clear();
        _selection.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        int maxGroupId = 0;
        int maxItemId = 0;
        foreach (var dto in workspace.Items)
        {
            var item = ItemFromDto(dto);
            _items.Add(item);
            if (item.FreehandGroupId > maxGroupId)
                maxGroupId = item.FreehandGroupId;
            if (item.Id > maxItemId)
                maxItemId = item.Id;
        }
        _nextFreehandGroupId = maxGroupId + 1;
        _nextItemId = maxItemId + 1;
        EnsureItemIds();
        RefreshAllAnchoredArrows();
        UpdatePropertyPanelForSelection();
        Redraw();
    }

    public static DrawingWorkspaceDto? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DrawingWorkspaceDto>(json);
        }
        catch
        {
            return null;
        }
    }
}

// ---------------- Palette color picker (shared by Drawing window + Themes) ----------------

internal static class DrawingPaletteColorPicker
{
    internal const string CustomColorTag = "__CUSTOM_COLOR__";

    private static readonly string[] ColorChoices =
    {
        "Transparent",
        "#000000", "#FFFFFF", "#555555", "#BBBBBB",
        "#E53935", "#F68A1E", "#FFD700", "#FFF6A9",
        "#4CAF50", "#A8E6A1", "#2196F3", "#B3E5FC",
        "#673AB7", "#D1C4E9", "#795548", "#3F51B5",
        "#F5F5DC",
    };

    public static ComboBox CreateCombo(bool includeCustomPicker = false)
    {
        var cb = new ComboBox { Margin = new Thickness(0) };
        Populate(cb, includeCustomPicker);
        return cb;
    }

    public static bool IsCustomColorTag(string? tag) =>
        string.Equals(tag, CustomColorTag, StringComparison.Ordinal);

    public static void Populate(ItemsControl items, bool includeCustomPicker = false)
    {
        items.Items.Clear();
        var seenHexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeCustomPicker)
            items.Items.Add(MakeCustomPickerItem());

        items.Items.Add(MakeColorChoiceItem("Transparent"));
        seenHexes.Add("Transparent");

        var service = new ColorPaletteService();
        string? lastPalette = null;
        foreach (var (paletteName, color) in service.EnumeratePaletteColors())
        {
            if (!DrawingColorUtilities.TryParseColorString(color.Hex, out var parsed) || parsed.A == 0)
                continue;
            var tag = DrawingColorUtilities.FormatHexForTheme(parsed);
            if (!seenHexes.Add(tag))
                continue;

            if (!string.Equals(lastPalette, paletteName, StringComparison.OrdinalIgnoreCase))
            {
                items.Items.Add(MakePaletteHeaderItem(paletteName));
                lastPalette = paletteName;
            }

            items.Items.Add(MakeNamedColorItem(paletteName, color.Name, tag));
        }

        foreach (var hex in ColorChoices)
        {
            if (string.Equals(hex, "Transparent", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!DrawingColorUtilities.TryParseColorString(hex, out var parsed))
                continue;
            var tag = DrawingColorUtilities.FormatHexForTheme(parsed);
            if (!seenHexes.Add(tag))
                continue;
            items.Items.Add(MakeColorChoiceItem(tag));
        }
    }

    public static string GetSelectedHex(ItemsControl items)
    {
        if (items is ComboBox cb)
        {
            if (cb.SelectedItem is ComboBoxItem item && item.Tag is string s) return s;
            if (cb.SelectedItem is string s2) return s2;
        }
        else if (items is ListBox lb && lb.SelectedItem is ComboBoxItem li && li.Tag is string s3)
            return s3;
        return "#000000";
    }

    public static void SelectColor(ItemsControl items, string? value)
    {
        var key = string.IsNullOrWhiteSpace(value) ? "Transparent" : value.Trim();
        foreach (var obj in items.Items)
        {
            if (obj is ComboBoxItem item && item.Tag is string s
                && string.Equals(s, key, StringComparison.OrdinalIgnoreCase))
            {
                if (items is ComboBox cb)
                    cb.SelectedItem = item;
                else if (items is ListBox lb)
                    lb.SelectedItem = item;
                return;
            }
        }

        var extra = MakeColorChoiceItem(key);
        items.Items.Add(extra);
        if (items is ComboBox cb2)
            cb2.SelectedItem = extra;
        else if (items is ListBox lb2)
            lb2.SelectedItem = extra;
    }

    private static ComboBoxItem MakeColorChoiceItem(string value)
    {
        var item = new ComboBoxItem { Tag = value };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(MakeSwatchBorder(value, 20, 14));
        sp.Children.Add(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center });
        item.Content = sp;
        return item;
    }

    private static ComboBoxItem MakeCustomPickerItem()
    {
        var item = new ComboBoxItem { Tag = CustomColorTag };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(MakePickerIcon(20, 14));
        sp.Children.Add(new TextBlock
        {
            Text = "Custom color…",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        });
        item.Content = sp;
        return item;
    }

    private static Border MakePickerIcon(double width, double height)
    {
        var grid = new Grid { Width = width, Height = height };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        void AddQuad(int row, int col, Brush fill)
        {
            var r = new System.Windows.Shapes.Rectangle { Fill = fill };
            Grid.SetRow(r, row);
            Grid.SetColumn(r, col);
            grid.Children.Add(r);
        }

        AddQuad(0, 0, new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));
        AddQuad(0, 1, new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)));
        AddQuad(1, 0, new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)));
        AddQuad(1, 1, new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));

        return new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 0, 6, 0),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    private static ComboBoxItem MakeNamedColorItem(string paletteName, string colorName, string tagHex)
    {
        var displayName = string.IsNullOrWhiteSpace(paletteName)
            ? colorName
            : $"{paletteName} · {colorName}";
        var item = new ComboBoxItem { Tag = tagHex };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(MakeSwatchBorder(tagHex, 20, 14));
        sp.Children.Add(new TextBlock { Text = displayName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        sp.Children.Add(new TextBlock { Text = tagHex, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray });
        item.Content = sp;
        return item;
    }

    private static ComboBoxItem MakePaletteHeaderItem(string paletteName)
    {
        return new ComboBoxItem
        {
            Content = paletteName,
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Gray,
            Padding = new Thickness(4, 6, 4, 2),
        };
    }

    internal static Border MakeSwatchBorder(string hex, double width, double height)
    {
        Brush background = string.Equals(hex, "Transparent", StringComparison.OrdinalIgnoreCase)
            ? CompactColorPicker.CreateCheckeredBrush()
            : DrawingWindow.ParseBrush(hex);
        return new Border
        {
            Width = width,
            Height = height,
            Margin = new Thickness(0, 0, 6, 0),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Background = background,
        };
    }
}

internal sealed class CompactColorPicker : Border
{
    internal const string CustomPaletteName = "Custom Colors";

    private static event Action? PalettesUpdated;

    private readonly ComboBox _combo;
    private readonly Border _swatch;
    private readonly Action<Brush> _onChanged;
    private readonly Action<Brush>? _onPreview;
    private readonly Action? _onBeginCustomPick;
    private bool _suppress;
    private string _currentHex = "#000000";

    public CompactColorPicker(
        Brush initial,
        Action<Brush> onChanged,
        Action<Brush>? onPreview = null,
        Action? onBeginCustomPick = null)
    {
        _onChanged = onChanged;
        _onPreview = onPreview;
        _onBeginCustomPick = onBeginCustomPick;
        Width = 52;
        Height = 28;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip = "Click to choose a color";

        PalettesUpdated += OnPalettesUpdated;
        Unloaded += (_, _) => PalettesUpdated -= OnPalettesUpdated;

        _swatch = new Border
        {
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
        };

        _combo = DrawingPaletteColorPicker.CreateCombo(includeCustomPicker: true);
        _combo.Background = Brushes.Transparent;
        _combo.BorderThickness = new Thickness(0);
        _combo.Opacity = 0;
        _combo.IsHitTestVisible = true;
        _combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _combo.VerticalAlignment = VerticalAlignment.Stretch;
        _combo.MaxDropDownHeight = 320;
        _combo.MinWidth = 280;

        _combo.DropDownOpened += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_combo.Items.Count == 0) return;
                var first = _combo.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                first?.BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };

        _combo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            if (_combo.SelectedItem is ComboBoxItem { IsEnabled: false })
                return;
            if (_combo.SelectedItem is ComboBoxItem ci && ci.Tag is string tag
                && DrawingPaletteColorPicker.IsCustomColorTag(tag))
            {
                Dispatcher.BeginInvoke(new Action(OpenCustomPicker),
                    System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
            var hex = DrawingPaletteColorPicker.GetSelectedHex(_combo);
            if (string.Equals(hex, _currentHex, StringComparison.OrdinalIgnoreCase))
                return;
            _currentHex = hex;
            UpdateSwatch(DrawingWindow.ParseBrush(hex));
            _onChanged(DrawingWindow.ParseBrush(hex));
        };

        var grid = new Grid();
        grid.Children.Add(_swatch);
        grid.Children.Add(_combo);
        Child = grid;

        SetSelected(initial);
    }

    public void Refresh() => DrawingPaletteColorPicker.Populate(_combo, includeCustomPicker: true);

    public void SetSelected(Brush brush)
    {
        _suppress = true;
        try
        {
            _currentHex = DrawingWindow.BrushToHex(brush);
            DrawingPaletteColorPicker.SelectColor(_combo, _currentHex);
            UpdateSwatch(brush);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OpenCustomPicker()
    {
        var prevHex = _currentHex;
        var prevBrush = DrawingWindow.ParseBrush(prevHex);
        _suppress = true;
        try { DrawingPaletteColorPicker.SelectColor(_combo, prevHex); }
        finally { _suppress = false; }

        var owner = Window.GetWindow(this);
        var dlg = new DrawingColorPickerWindow(prevHex);
        if (owner != null) dlg.Owner = owner;

        _onBeginCustomPick?.Invoke();
        var preview = _onPreview ?? _onChanged;

        dlg.ColorChanged += c =>
        {
            var hex = DrawingColorUtilities.FormatHexForTheme(c);
            _currentHex = hex;
            var b = DrawingWindow.ParseBrush(hex);
            UpdateSwatch(b);
            preview(b);
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ResultHex))
        {
            var hex = dlg.ResultHex!;
            SaveToCustomPalette(hex);
            _currentHex = hex;
            _suppress = true;
            try
            {
                DrawingPaletteColorPicker.Populate(_combo, includeCustomPicker: true);
                DrawingPaletteColorPicker.SelectColor(_combo, _currentHex);
            }
            finally { _suppress = false; }
            var brush = DrawingWindow.ParseBrush(_currentHex);
            UpdateSwatch(brush);
            preview(brush);
            PalettesUpdated?.Invoke();
        }
        else
        {
            _currentHex = prevHex;
            UpdateSwatch(prevBrush);
            preview(prevBrush);
        }
    }

    private void OnPalettesUpdated()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnPalettesUpdated));
            return;
        }
        _suppress = true;
        try
        {
            DrawingPaletteColorPicker.Populate(_combo, includeCustomPicker: true);
            DrawingPaletteColorPicker.SelectColor(_combo, _currentHex);
        }
        finally { _suppress = false; }
    }

    private static void SaveToCustomPalette(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        try
        {
            var service = new ColorPaletteService();
            service.AddOrUpdateColor(CustomPaletteName, new NamedColor { Name = hex, Hex = hex });
        }
        catch
        {
            // non-critical
        }
    }

    private void UpdateSwatch(Brush brush)
    {
        _swatch.Background = string.Equals(_currentHex, "Transparent", StringComparison.OrdinalIgnoreCase)
            ? CreateCheckeredBrush()
            : brush;
    }

    internal static DrawingBrush CreateCheckeredBrush()
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

internal static class DrawingThemePreview
{
    public static FontFamily SafeFont(string name)
    {
        try { return new FontFamily(name); }
        catch { return new FontFamily("Segoe UI"); }
    }

    public static void AppendCaption(Canvas c, double left, double top, string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            Foreground = Brushes.DimGray,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        c.Children.Add(tb);
    }

    public static void AppendRect(Canvas c, ThemeToolStyle st, double x, double y, double w, double h)
    {
        var fill = DrawingWindow.ParseBrush(st.Fill);
        var stroke = DrawingWindow.ParseBrush(st.Stroke);
        var radius = Math.Min(Math.Max(0, st.CornerRadius), Math.Min(w, h) / 2);
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = st.Thickness,
            RadiusX = radius,
            RadiusY = radius,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        c.Children.Add(rect);
        var fs = Math.Clamp(st.FontSize * 0.42, 8, Math.Max(8, h * 0.55));
        var label = new TextBlock
        {
            Text = "Aa",
            FontSize = fs,
            FontFamily = SafeFont(st.FontFamilyName),
            Foreground = DrawingWindow.ParseBrush(st.TextColor),
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
        };
        label.Measure(new Size(w - 8, h - 8));
        Canvas.SetLeft(label, x + 4);
        Canvas.SetTop(label, y + Math.Max(0, (h - label.DesiredSize.Height) / 2));
        c.Children.Add(label);
    }

    public static void AppendEllipse(Canvas c, ThemeToolStyle st, double x, double y, double w, double h)
    {
        var el = new Ellipse
        {
            Width = w,
            Height = h,
            Fill = DrawingWindow.ParseBrush(st.Fill),
            Stroke = DrawingWindow.ParseBrush(st.Stroke),
            StrokeThickness = st.Thickness,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        c.Children.Add(el);
        var fs = Math.Clamp(st.FontSize * 0.42, 8, Math.Max(8, h * 0.55));
        var innerW = Math.Max(0, w * 0.70710678118654752 - 8);
        var label = new TextBlock
        {
            Text = "Aa",
            FontSize = fs,
            FontFamily = SafeFont(st.FontFamilyName),
            Foreground = DrawingWindow.ParseBrush(st.TextColor),
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
        };
        label.Measure(new Size(innerW, h - 8));
        Canvas.SetLeft(label, x + (w - innerW) / 2);
        Canvas.SetTop(label, y + Math.Max(0, (h - label.DesiredSize.Height) / 2));
        c.Children.Add(label);
    }

    public static void AppendArrow(Canvas c, ThemeToolStyle st, Point p1, Point p2)
    {
        var stroke = DrawingWindow.ParseBrush(st.Stroke);
        var th = Math.Max(1.5, st.Thickness);
        c.Children.Add(new Line
        {
            X1 = p1.X,
            Y1 = p1.Y,
            X2 = p2.X,
            Y2 = p2.Y,
            Stroke = stroke,
            StrokeThickness = th,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
        AddArrowHeads(c, p1, p2, stroke, th, st.ArrowHead);
    }

    private static void AddArrowHeads(Canvas c, Point p1, Point p2, Brush stroke, double thickness, string arrowHead)
    {
        if (string.Equals(arrowHead, "None", StringComparison.OrdinalIgnoreCase)) return;
        var headSize = Math.Max(8, thickness * 3.5);
        if (string.Equals(arrowHead, "Double", StringComparison.OrdinalIgnoreCase))
        {
            DrawArrowHead(c, p2, p1, stroke, thickness, headSize);
            DrawArrowHead(c, p1, p2, stroke, thickness, headSize);
            return;
        }
        DrawArrowHead(c, p1, p2, stroke, thickness, headSize);
    }

    private static void DrawArrowHead(Canvas c, Point from, Point to, Brush stroke, double thickness, double size)
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
        AddHeadLine(c, to.X, to.Y, leftX, leftY, stroke, thickness);
        AddHeadLine(c, to.X, to.Y, rightX, rightY, stroke, thickness);
    }

    private static void AddHeadLine(Canvas c, double x1, double y1, double x2, double y2, Brush stroke, double th)
    {
        c.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = th,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
    }

    public static void AppendText(Canvas c, ThemeToolStyle st, double x, double y, string sample)
    {
        var tb = new TextBlock
        {
            Text = sample,
            FontSize = Math.Clamp(st.FontSize * 0.55, 8, 28),
            FontFamily = SafeFont(st.FontFamilyName),
            Foreground = DrawingWindow.ParseBrush(st.TextColor),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        c.Children.Add(tb);
    }

    public static void AppendFreehand(Canvas c, ThemeToolStyle st, double x, double yTop, double width)
    {
        var stroke = DrawingWindow.ParseBrush(st.Stroke);
        var th = Math.Max(2, st.FreeThickness);
        const int steps = 26;
        var amp = Math.Clamp(th * 1.8, 5, 14);
        var samples = new List<Point>(steps + 1);
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            samples.Add(new Point(x + t * width, yTop + amp * Math.Sin(t * Math.PI * 3)));
        }
        c.Children.Add(new System.Windows.Shapes.Path
        {
            Data = DrawingFreehandGeometry.CreateSmoothGeometry(samples),
            Stroke = stroke,
            StrokeThickness = th,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });
    }
}

internal sealed class ThemeSettingsWindow : Window
{
    public List<DrawingTheme> Themes { get; private set; }

    private readonly ListBox _list;
    private readonly StackPanel _editor;
    private DrawingTheme? _editing;
    private bool _suppressUpdate;

    private TextBox? _nameBox;
    private TextBlock? _builtInHint;
    private TabControl? _tabs;

    private sealed class VariantUi
    {
        public CheckBox? Enabled;
        public ComboBox? Fill, Stroke, TextColor, Font, ArrowHead;
        public TextBox? FontSize, Thickness, Corner, FreeThickness;
    }

    // Per-kind UI controls, index-aligned with the editing theme's variant lists.
    private readonly Dictionary<string, List<VariantUi>> _kindUi = new()
    {
        ["rect"] = new(),
        ["ellipse"] = new(),
        ["arrow"] = new(),
        ["text"] = new(),
        ["freehand"] = new(),
    };

    // Per-kind tab panel (rebuilt on theme change, add/remove variant).
    private readonly Dictionary<string, StackPanel> _kindPanels = new();

    private Canvas? _previewCanvas;

    private static readonly string[] FontChoices =
    {
        "Segoe UI", "Arial", "Calibri", "Cambria", "Consolas", "Courier New",
        "Georgia", "Helvetica", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana",
        "Comic Sans MS", "Impact",
    };

    public ThemeSettingsWindow(List<DrawingTheme> existing)
    {
        Themes = existing.Select(t => t.Clone()).ToList();
        Title = "Drawing Themes";
        Width = 1240;
        Height = 760;
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
                var baseName = DrawingThemeStore.IsBuiltInName(t.Name)
                    ? t.Name[..^DrawingThemeStore.BuiltInSuffix.Length]
                    : t.Name;
                copy.Name = baseName + " copy";
                Themes.Add(copy);
                _list.ItemsSource = null;
                _list.ItemsSource = Themes;
                _list.SelectedItem = copy;
            }
        };
        btnRemove.Click += (_, _) =>
        {
            if (_list.SelectedItem is DrawingTheme t
                && !DrawingThemeStore.IsBuiltInName(t.Name)
                && Themes.Count > 1)
            {
                Themes.Remove(t);
                _list.ItemsSource = null;
                _list.ItemsSource = Themes;
                _list.SelectedIndex = 0;
            }
        };
        _list.SelectionChanged += (_, _) =>
        {
            btnRemove.IsEnabled = _list.SelectedItem is DrawingTheme sel
                && !DrawingThemeStore.IsBuiltInName(sel.Name);
        };

        // Center: editor + live preview
        var centerGrid = new Grid();
        centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        centerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });

        var editorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _editor = new StackPanel();
        editorScroll.Content = _editor;
        AttachWheelScroll(editorScroll);
        Grid.SetColumn(editorScroll, 0);
        centerGrid.Children.Add(editorScroll);

        var previewWrap = new Border
        {
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(10, 8, 10, 10),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        var previewStack = new DockPanel();
        var previewHeader = new StackPanel();
        DockPanel.SetDock(previewHeader, Dock.Top);
        previewHeader.Children.Add(EditorLabel("Live preview"));
        previewHeader.Children.Add(new TextBlock
        {
            Text = "Shows every enabled variant for the selected theme.",
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        previewStack.Children.Add(previewHeader);
        _previewCanvas = new Canvas
        {
            Width = 220,
            Background = Brushes.White,
        };
        var previewScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _previewCanvas,
        };
        previewStack.Children.Add(previewScroll);
        previewWrap.Child = previewStack;
        Grid.SetColumn(previewWrap, 1);
        centerGrid.Children.Add(previewWrap);

        root.Children.Add(centerGrid);

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

        _builtInHint = new TextBlock
        {
            Text = "Built-in theme — read-only. Use Duplicate to customize.",
            Foreground = Brushes.DarkOrange,
            FontStyle = FontStyles.Italic,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        _editor.Children.Add(_builtInHint);

        var customColorsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var btnCustomColors = new Button { Content = "Custom colors…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        btnCustomColors.Click += (_, _) =>
        {
            var w = new DrawingNamedColorsWindow { Owner = this };
            if (w.ShowDialog() == true)
                RefreshThemeColorCombosFromEditing();
        };
        customColorsRow.Children.Add(btnCustomColors);
        customColorsRow.Children.Add(new TextBlock
        {
            Text = "First 3 variants are toolbar slots; add more for the property panel.",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.Gray,
            FontSize = 11,
        });
        _editor.Children.Add(customColorsRow);

        _tabs = new TabControl { MinHeight = 460, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (kind, header) in new[]
        {
            ("rect", "Rectangle"), ("ellipse", "Circle"), ("arrow", "Arrow"),
            ("text", "Text"), ("freehand", "Freeform"),
        })
        {
            var panel = new StackPanel();
            _kindPanels[kind] = panel;
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel,
            };
            AttachWheelScroll(sv);
            _tabs.Items.Add(new TabItem { Header = header, Content = sv });
        }
        _editor.Children.Add(_tabs);
    }

    private void RebuildKindPane(string kind)
    {
        if (_editing == null) return;
        _editing.EnsureToolStyles();
        var variants = _editing.GetVariants(kind);
        if (!_kindPanels.TryGetValue(kind, out var panel)) return;
        panel.Children.Clear();

        var uiList = _kindUi[kind];
        uiList.Clear();
        for (var i = 0; i < variants.Count; i++) uiList.Add(new VariantUi());

        var slotsHeader = new TextBlock
        {
            Text = "Toolbar slots (top 3) — appear as buttons in the drawing toolbar.",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(4, 2, 4, 4),
        };
        panel.Children.Add(slotsHeader);

        var slotGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < DrawingTheme.ToolbarSlotCount; i++)
            slotGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var slotCount = Math.Min(variants.Count, DrawingTheme.ToolbarSlotCount);
        for (var i = 0; i < slotCount; i++)
        {
            var card = BuildVariantCard(uiList[i], i, kind, isExtra: false);
            Grid.SetColumn(card, i);
            slotGrid.Children.Add(card);
        }
        panel.Children.Add(slotGrid);

        var extraCount = variants.Count - DrawingTheme.ToolbarSlotCount;
        if (extraCount > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Extra variants — appear after Size in the property panel.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(4, 8, 4, 4),
            });

            var extrasGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            for (var i = 0; i < DrawingTheme.ToolbarSlotCount; i++)
                extrasGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rowsNeeded = (extraCount + DrawingTheme.ToolbarSlotCount - 1) / DrawingTheme.ToolbarSlotCount;
            for (var r = 0; r < rowsNeeded; r++)
                extrasGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var idx = DrawingTheme.ToolbarSlotCount; idx < variants.Count; idx++)
            {
                var rel = idx - DrawingTheme.ToolbarSlotCount;
                var card = BuildVariantCard(uiList[idx], idx, kind, isExtra: true);
                Grid.SetColumn(card, rel % DrawingTheme.ToolbarSlotCount);
                Grid.SetRow(card, rel / DrawingTheme.ToolbarSlotCount);
                extrasGrid.Children.Add(card);
            }
            panel.Children.Add(extrasGrid);
        }

        var addBtn = new Button
        {
            Content = "+ Add variant",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(4, 4, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        addBtn.Click += (_, _) => AddVariant(kind);
        panel.Children.Add(addBtn);

        _suppressUpdate = true;
        try
        {
            for (var i = 0; i < variants.Count; i++)
                LoadVariantUi(uiList[i], variants[i]);
        }
        finally
        {
            _suppressUpdate = false;
        }
    }

    private void AddVariant(string kind)
    {
        if (_editing == null) return;
        _editing.EnsureToolStyles();
        var variants = _editing.GetVariants(kind);
        var seed = variants.Count > 0 ? variants[0].Clone() : new ThemeToolStyle();
        seed.Enabled = false;
        variants.Add(seed);
        RebuildKindPane(kind);
        RebuildThemePreview();
    }

    private void RemoveVariant(string kind, int index)
    {
        if (_editing == null) return;
        _editing.EnsureToolStyles();
        var variants = _editing.GetVariants(kind);
        // Slot variants (0..ToolbarSlotCount-1) are permanent; only extras can be removed.
        if (index < DrawingTheme.ToolbarSlotCount || index >= variants.Count) return;
        variants.RemoveAt(index);
        var activeIdx = _editing.GetActiveIndex(kind);
        if (activeIdx >= variants.Count) _editing.SetActiveIndex(kind, Math.Max(0, variants.Count - 1));
        else if (activeIdx > index) _editing.SetActiveIndex(kind, activeIdx - 1);
        RebuildKindPane(kind);
        RebuildThemePreview();
    }

    private FrameworkElement BuildVariantCard(VariantUi ui, int index, string kind, bool isExtra)
    {
        var sp = new StackPanel { Margin = new Thickness(2) };
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new TextBlock
        {
            Text = $"Variant {index + 1}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(headerText, 0);
        headerRow.Children.Add(headerText);
        if (isExtra)
        {
            var removeBtn = new Button
            {
                Content = "Remove",
                Padding = new Thickness(6, 1, 6, 1),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) => RemoveVariant(kind, index);
            Grid.SetColumn(removeBtn, 1);
            headerRow.Children.Add(removeBtn);
        }
        sp.Children.Add(headerRow);

        ui.Enabled = new CheckBox { Content = "Enabled", Margin = new Thickness(0, 0, 0, 8) };
        ui.Enabled.Checked += (_, _) => OnEnabledChanged(kind, index, true);
        ui.Enabled.Unchecked += (_, _) => OnEnabledChanged(kind, index, false);
        sp.Children.Add(ui.Enabled);

        switch (kind)
        {
            case "rect":
                BuildShapeFields(sp, ui, includeFill: true, includeText: true, includeCorner: true, kind, index);
                break;
            case "ellipse":
                BuildShapeFields(sp, ui, includeFill: true, includeText: true, includeCorner: false, kind, index);
                break;
            case "arrow":
                BuildShapeFields(sp, ui, includeFill: false, includeText: true, includeCorner: false, kind, index);
                sp.Children.Add(EditorLabel("Arrow head"));
                ui.ArrowHead = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                ui.ArrowHead.Items.Add("None");
                ui.ArrowHead.Items.Add("Simple");
                ui.ArrowHead.Items.Add("Double");
                sp.Children.Add(ui.ArrowHead);
                ui.ArrowHead.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.ArrowHead = ui.ArrowHead!.SelectedItem as string ?? "Simple");
                break;
            case "text":
                sp.Children.Add(EditorLabel("Text color"));
                ui.TextColor = MakeColorCombo();
                sp.Children.Add(MakeThemeColorRow(ui.TextColor));
                sp.Children.Add(EditorLabel("Font family"));
                ui.Font = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                foreach (var n in FontChoices) ui.Font.Items.Add(n);
                sp.Children.Add(ui.Font);
                sp.Children.Add(EditorLabel("Font size"));
                ui.FontSize = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(ui.FontSize);
                sp.Children.Add(EditorLabel("Thickness (outline)"));
                ui.Thickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(ui.Thickness);

                ui.TextColor.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.TextColor = DrawingPaletteColorPicker.GetSelectedHex(ui.TextColor!));
                ui.Font.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.FontFamilyName = ui.Font!.SelectedItem as string ?? s.FontFamilyName);
                ui.FontSize.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.FontSize!.Text, out var v)) s.FontSize = Math.Max(4, v); });
                ui.Thickness.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.Thickness!.Text, out var v)) s.Thickness = Math.Max(0.5, v); });
                break;
            case "freehand":
                sp.Children.Add(EditorLabel("Stroke"));
                ui.Stroke = MakeColorCombo();
                sp.Children.Add(MakeThemeColorRow(ui.Stroke));
                sp.Children.Add(EditorLabel("Free draw thickness"));
                ui.FreeThickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
                sp.Children.Add(ui.FreeThickness);

                ui.Stroke.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.Stroke = DrawingPaletteColorPicker.GetSelectedHex(ui.Stroke!));
                ui.FreeThickness.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.FreeThickness!.Text, out var v)) s.FreeThickness = Math.Max(1, v); });
                break;
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4),
            Padding = new Thickness(8),
            Child = sp,
        };
    }

    private void BuildShapeFields(StackPanel sp, VariantUi ui, bool includeFill, bool includeText, bool includeCorner, string kind, int index)
    {
        if (includeFill)
        {
            sp.Children.Add(EditorLabel("Fill"));
            ui.Fill = MakeColorCombo();
            sp.Children.Add(MakeThemeColorRow(ui.Fill));
            ui.Fill.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.Fill = DrawingPaletteColorPicker.GetSelectedHex(ui.Fill!));
        }

        sp.Children.Add(EditorLabel("Stroke / frame"));
        ui.Stroke = MakeColorCombo();
        sp.Children.Add(MakeThemeColorRow(ui.Stroke));
        ui.Stroke.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.Stroke = DrawingPaletteColorPicker.GetSelectedHex(ui.Stroke!));

        if (includeText)
        {
            sp.Children.Add(EditorLabel("Text color"));
            ui.TextColor = MakeColorCombo();
            sp.Children.Add(MakeThemeColorRow(ui.TextColor));
            sp.Children.Add(EditorLabel("Font family"));
            ui.Font = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var n in FontChoices) ui.Font.Items.Add(n);
            sp.Children.Add(ui.Font);
            sp.Children.Add(EditorLabel("Font size"));
            ui.FontSize = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(ui.FontSize);
            ui.TextColor.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.TextColor = DrawingPaletteColorPicker.GetSelectedHex(ui.TextColor!));
            ui.Font.SelectionChanged += (_, _) => MutateVariant(kind, index, s => s.FontFamilyName = ui.Font!.SelectedItem as string ?? s.FontFamilyName);
            ui.FontSize.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.FontSize!.Text, out var v)) s.FontSize = Math.Max(4, v); });
        }

        sp.Children.Add(EditorLabel("Stroke thickness"));
        ui.Thickness = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(ui.Thickness);
        ui.Thickness.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.Thickness!.Text, out var v)) s.Thickness = Math.Max(0.5, v); });

        if (includeCorner)
        {
            sp.Children.Add(EditorLabel("Corner radius"));
            ui.Corner = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(ui.Corner);
            ui.Corner.TextChanged += (_, _) => MutateVariant(kind, index, s => { if (double.TryParse(ui.Corner!.Text, out var v)) s.CornerRadius = Math.Max(0, v); });
        }
    }

    private void MutateVariant(string kind, int index, Action<ThemeToolStyle> set)
    {
        if (_suppressUpdate || _editing == null) return;
        _editing.EnsureToolStyles();
        var variants = _editing.GetVariants(kind);
        if (index < 0 || index >= variants.Count) return;
        set(variants[index]);
        RebuildThemePreview();
    }

    private void OnEnabledChanged(string kind, int index, bool enabled)
    {
        if (_suppressUpdate || _editing == null) return;
        _editing.EnsureToolStyles();
        var variants = _editing.GetVariants(kind);
        if (index < 0 || index >= variants.Count) return;
        variants[index].Enabled = enabled;
        var activeIdx = _editing.GetActiveIndex(kind);
        if (!enabled && activeIdx == index)
        {
            for (var i = 0; i < variants.Count; i++)
            {
                if (variants[i].Enabled)
                {
                    _editing.SetActiveIndex(kind, i);
                    break;
                }
            }
        }
        RebuildThemePreview();
    }

    private void RefreshThemeColorCombosFromEditing()
    {
        if (_editing == null) return;
        _suppressUpdate = true;
        try
        {
            _editing.EnsureToolStyles();
            foreach (var (kind, uiList) in _kindUi)
            {
                var variants = _editing.GetVariants(kind);
                var n = Math.Min(uiList.Count, variants.Count);
                for (var i = 0; i < n; i++)
                    RepopulateColorCombos(uiList[i], variants[i]);
            }
            RebuildThemePreview();
        }
        finally
        {
            _suppressUpdate = false;
        }
    }

    private static void RepopulateColorCombos(VariantUi ui, ThemeToolStyle style)
    {
        if (ui.Fill != null) { DrawingPaletteColorPicker.Populate(ui.Fill); DrawingPaletteColorPicker.SelectColor(ui.Fill, style.Fill); }
        if (ui.Stroke != null) { DrawingPaletteColorPicker.Populate(ui.Stroke); DrawingPaletteColorPicker.SelectColor(ui.Stroke, style.Stroke); }
        if (ui.TextColor != null) { DrawingPaletteColorPicker.Populate(ui.TextColor); DrawingPaletteColorPicker.SelectColor(ui.TextColor, style.TextColor); }
    }

    private static ComboBox MakeColorCombo() => DrawingPaletteColorPicker.CreateCombo();

    private Grid MakeThemeColorRow(ComboBox cb)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(cb, 0);
        cb.VerticalAlignment = VerticalAlignment.Center;
        cb.Margin = new Thickness(0, 0, 8, 0);

        var swatch = new Border
        {
            Width = 30,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Background = Brushes.White,
            Cursor = Cursors.Hand,
            ToolTip = "Graphical color picker",
        };

        void UpdateSwatch()
        {
            var key = DrawingPaletteColorPicker.GetSelectedHex(cb);
            swatch.Background = DrawingWindow.ParseBrush(key);
        }

        cb.SelectionChanged += (_, _) => UpdateSwatch();

        swatch.MouseLeftButtonDown += (_, _) =>
        {
            var dlg = new DrawingColorPickerWindow(DrawingPaletteColorPicker.GetSelectedHex(cb)) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ResultHex))
            {
                DrawingPaletteColorPicker.SelectColor(cb, dlg.ResultHex!);
                UpdateSwatch();
            }
        };

        Grid.SetColumn(swatch, 1);
        grid.Children.Add(cb);
        grid.Children.Add(swatch);
        UpdateSwatch();
        return grid;
    }

    private void LoadEditor()
    {
        if (_editing == null)
        {
            _suppressUpdate = true;
            try
            {
                _nameBox!.Text = "";
                foreach (var panel in _kindPanels.Values) panel.Children.Clear();
                foreach (var uiList in _kindUi.Values) uiList.Clear();
                RebuildThemePreview();
            }
            finally
            {
                _suppressUpdate = false;
            }
            ApplyBuiltInLock(false);
            return;
        }

        _nameBox!.Text = _editing.Name;
        _editing.EnsureToolStyles();
        foreach (var kind in _kindPanels.Keys)
            RebuildKindPane(kind);
        RebuildThemePreview();
        ApplyBuiltInLock(DrawingThemeStore.IsBuiltInName(_editing.Name));
    }

    private void ApplyBuiltInLock(bool locked)
    {
        if (_nameBox != null) _nameBox.IsEnabled = !locked;
        if (_tabs != null) _tabs.IsEnabled = !locked;
        if (_builtInHint != null)
            _builtInHint.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void LoadVariantUi(VariantUi ui, ThemeToolStyle style)
    {
        if (ui.Enabled != null) ui.Enabled.IsChecked = style.Enabled;
        if (ui.Fill != null) { DrawingPaletteColorPicker.Populate(ui.Fill); DrawingPaletteColorPicker.SelectColor(ui.Fill, style.Fill); }
        if (ui.Stroke != null) { DrawingPaletteColorPicker.Populate(ui.Stroke); DrawingPaletteColorPicker.SelectColor(ui.Stroke, style.Stroke); }
        if (ui.TextColor != null) { DrawingPaletteColorPicker.Populate(ui.TextColor); DrawingPaletteColorPicker.SelectColor(ui.TextColor, style.TextColor); }
        if (ui.Font != null)
        {
            ui.Font.SelectedItem = style.FontFamilyName;
            if (ui.Font.SelectedItem == null) ui.Font.SelectedIndex = 0;
        }
        if (ui.FontSize != null) ui.FontSize.Text = style.FontSize.ToString();
        if (ui.Thickness != null) ui.Thickness.Text = style.Thickness.ToString();
        if (ui.Corner != null) ui.Corner.Text = style.CornerRadius.ToString();
        if (ui.ArrowHead != null)
        {
            ui.ArrowHead.SelectedItem = style.ArrowHead;
            if (ui.ArrowHead.SelectedItem == null) ui.ArrowHead.SelectedIndex = 1;
        }
        if (ui.FreeThickness != null) ui.FreeThickness.Text = style.FreeThickness.ToString();
    }

    private void RebuildThemePreview()
    {
        if (_previewCanvas == null) return;
        _previewCanvas.Children.Clear();
        if (_editing == null)
        {
            _previewCanvas.Height = 0;
            return;
        }
        _editing.EnsureToolStyles();

        const double x = 4;
        const double w = 208;
        var y = 4d;

        void Section(string caption, IReadOnlyList<ThemeToolStyle> variants, Action<ThemeToolStyle, double> drawOne, double itemHeight)
        {
            var enabled = variants.Where(v => v.Enabled).ToList();
            if (enabled.Count == 0) return;
            DrawingThemePreview.AppendCaption(_previewCanvas!, x, y, caption);
            y += 14;
            foreach (var st in enabled)
            {
                var rowTop = y;
                drawOne(st, rowTop);
                y += itemHeight;
            }
            y += 8;
        }

        Section("Rectangle", _editing.Rectangles, (st, top) => DrawingThemePreview.AppendRect(_previewCanvas!, st, x, top, w, 36), 42);
        Section("Circle", _editing.Ellipses, (st, top) => DrawingThemePreview.AppendEllipse(_previewCanvas!, st, x, top, w, 36), 42);
        Section("Arrow", _editing.Arrows, (st, top) => DrawingThemePreview.AppendArrow(_previewCanvas!, st, new Point(x + 12, top + 14), new Point(x + w - 4, top + 4)), 22);
        Section("Text", _editing.Texts, (st, top) => DrawingThemePreview.AppendText(_previewCanvas!, st, x + 2, top, "Sample text"), 26);
        Section("Freeform", _editing.Freehands, (st, top) => DrawingThemePreview.AppendFreehand(_previewCanvas!, st, x, top + 10, w), 26);

        _previewCanvas.Height = Math.Max(80, y + 4);
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

    /// <summary>Force mouse-wheel events on this ScrollViewer to scroll it, even when child
    /// controls (ComboBox, TextBox) would otherwise capture the wheel.</summary>
    private static void AttachWheelScroll(ScrollViewer sv)
    {
        sv.PreviewMouseWheel += (_, e) =>
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        };
    }
}
