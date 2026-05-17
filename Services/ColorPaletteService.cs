using System.IO;
using System.Linq;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

public sealed class NamedColor
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#000000";

    public override string ToString() => $"{Name}    {Hex}";
}

public sealed class ColorPalette
{
    public string Name { get; set; } = "";
    public List<NamedColor> Colors { get; set; } = new();
}

/// <summary>
/// App-wide store for named color palettes. Built-in palettes (Main, Diagrams) are defined in code;
/// user additions/removals are stored in <c>settings.json</c>. Custom palettes live in <c>color-palettes.json</c>.
/// </summary>
public sealed class ColorPaletteService
{
    public const string DefaultPaletteName = "Main";
    public const string DiagramsPaletteName = "Diagrams";

    private const string PalettesFileName = "color-palettes.json";
    private const string SettingsFileName = "settings.json";
    private const string LegacyNamedColorsFileName = "drawing-named-colors.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] BuiltInPaletteNames = [DefaultPaletteName, DiagramsPaletteName];

    /// <summary>When set (e.g. from <see cref="MainWindow"/> on load), palette files and settings use this folder.</summary>
    public static string? SharedBackupFolder { get; set; }

    private readonly string _backupFolder;
    private readonly WindowSettingsStore _settingsStore = new();

    public ColorPaletteService(string? backupFolder = null)
    {
        _backupFolder = NormalizeBackupFolder(backupFolder ?? SharedBackupFolder);
    }

    public static string DefaultBackupFolder() => @"c:\tools\backup\noted";

    public string FilePath => Path.Combine(_backupFolder, PalettesFileName);

    private string SettingsPath => Path.Combine(_backupFolder, SettingsFileName);

    public List<ColorPalette> LoadPalettes()
    {
        try
        {
            var customizations = LoadCustomizations();
            List<ColorPalette> userPalettes;
            var firstRun = !File.Exists(FilePath);

            if (firstRun)
            {
                userPalettes = TryMigrateFromLegacy();
            }
            else
            {
                userPalettes = JsonSerializer.Deserialize<List<ColorPalette>>(File.ReadAllText(FilePath))
                               ?? new List<ColorPalette>();
            }

            var migratedCustomizations = false;
            foreach (var builtInName in BuiltInPaletteNames)
            {
                var fromFile = userPalettes.FirstOrDefault(p =>
                    p.Name.Equals(builtInName, StringComparison.OrdinalIgnoreCase));
                if (fromFile != null && fromFile.Colors.Count > 0
                    && !customizations.ContainsKey(builtInName))
                {
                    customizations[builtInName] = ComputeCustomization(builtInName, fromFile.Colors);
                    migratedCustomizations = true;
                }
            }

            var strippedBuiltInsFromFile = userPalettes.RemoveAll(p => IsBuiltInPalette(p.Name)) > 0;

            var palettes = BuildPaletteList(userPalettes, customizations);
            NormalizePalettes(palettes);

            if (firstRun || migratedCustomizations || strippedBuiltInsFromFile)
            {
                if (migratedCustomizations)
                    PersistCustomizations(customizations);
                SaveUserPalettesOnly(userPalettes);
            }

            return palettes;
        }
        catch
        {
            var customizations = LoadCustomizations();
            var fallback = BuildPaletteList(new List<ColorPalette>(), customizations);
            NormalizePalettes(fallback);
            return fallback;
        }
    }

    public ColorPalette GetOrCreateDefault()
    {
        var palettes = LoadPalettes();
        var main = FindPalette(palettes, DefaultPaletteName);
        if (main != null)
            return main;

        var created = new ColorPalette { Name = DefaultPaletteName, Colors = MergeBuiltInPalette(DefaultPaletteName, null) };
        palettes.Insert(0, created);
        SavePalettes(palettes);
        return created;
    }

    public void SavePalettes(List<ColorPalette> palettes)
    {
        try
        {
            NormalizePalettes(palettes);
            var customizations = LoadCustomizations();

            foreach (var builtInName in BuiltInPaletteNames)
            {
                var builtIn = FindPalette(palettes, builtInName);
                if (builtIn == null)
                    continue;
                customizations[builtInName] = ComputeCustomization(builtInName, builtIn.Colors);
            }

            PersistCustomizations(customizations);

            var userOnly = palettes.Where(p => !IsBuiltInPalette(p.Name)).ToList();
            SaveUserPalettesOnly(userOnly);
        }
        catch
        {
            // non-critical
        }
    }

    public void SaveBuiltInPaletteColors(string paletteName, List<NamedColor> effectiveColors)
    {
        if (!IsBuiltInPalette(paletteName))
            return;
        PersistBuiltInPaletteCustomization(paletteName, effectiveColors);
    }

    public void AddOrUpdateColor(string paletteName, NamedColor color)
    {
        if (color == null || string.IsNullOrWhiteSpace(color.Name) || string.IsNullOrWhiteSpace(color.Hex))
            return;

        if (IsBuiltInPalette(paletteName))
        {
            var palettes = LoadPalettes();
            var builtIn = FindPalette(palettes, paletteName);
            if (builtIn == null)
                return;

            var normalizedHex = NormalizeHex(color.Hex);
            var existing = builtIn.Colors.FirstOrDefault(c =>
                c.Name.Equals(color.Name, StringComparison.OrdinalIgnoreCase)
                || NormalizeHex(c.Hex).Equals(normalizedHex, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = color.Name.Trim();
                existing.Hex = normalizedHex;
            }
            else
            {
                builtIn.Colors.Add(new NamedColor { Name = color.Name.Trim(), Hex = normalizedHex });
            }

            PersistBuiltInPaletteCustomization(paletteName, builtIn.Colors);
            return;
        }

        var all = LoadPalettes();
        var target = FindPalette(all, paletteName);
        if (target == null)
        {
            target = new ColorPalette { Name = paletteName.Trim() };
            all.Add(target);
        }

        var match = target.Colors.FirstOrDefault(c => c.Name.Equals(color.Name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            match.Hex = NormalizeHex(color.Hex);
        else
            target.Colors.Add(new NamedColor { Name = color.Name.Trim(), Hex = NormalizeHex(color.Hex) });

        SavePalettes(all);
    }

    public void RemoveColor(string paletteName, string colorName)
    {
        if (string.IsNullOrWhiteSpace(paletteName) || string.IsNullOrWhiteSpace(colorName))
            return;

        if (IsBuiltInPalette(paletteName))
        {
            var palettes = LoadPalettes();
            var builtIn = FindPalette(palettes, paletteName);
            if (builtIn == null)
                return;

            var entry = builtIn.Colors.FirstOrDefault(c =>
                c.Name.Equals(colorName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return;

            builtIn.Colors.RemoveAll(c => c.Name.Equals(colorName, StringComparison.OrdinalIgnoreCase));
            PersistBuiltInPaletteCustomization(paletteName, builtIn.Colors);
            return;
        }

        var all = LoadPalettes();
        var target = FindPalette(all, paletteName);
        if (target == null)
            return;

        var removed = target.Colors.RemoveAll(c => c.Name.Equals(colorName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            SavePalettes(all);
    }

    private void PersistBuiltInPaletteCustomization(string paletteName, List<NamedColor> effectiveColors)
    {
        var customizations = LoadCustomizations();
        customizations[paletteName] = ComputeCustomization(paletteName, effectiveColors);
        PersistCustomizations(customizations);
    }

    public bool CreatePalette(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || IsBuiltInPalette(trimmed))
            return false;

        var palettes = LoadPalettes();
        if (palettes.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            return false;

        palettes.Add(new ColorPalette { Name = trimmed });
        SavePalettes(palettes);
        return true;
    }

    public bool RenamePalette(string oldName, string newName)
    {
        var trimmedNew = (newName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(oldName) || trimmedNew.Length == 0 || IsBuiltInPalette(oldName))
            return false;

        var palettes = LoadPalettes();
        var target = FindPalette(palettes, oldName);
        if (target == null)
            return false;

        if (palettes.Any(p => !ReferenceEquals(p, target) && p.Name.Equals(trimmedNew, StringComparison.OrdinalIgnoreCase)))
            return false;

        target.Name = trimmedNew;
        SavePalettes(palettes);
        return true;
    }

    public bool DeletePalette(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || IsBuiltInPalette(name))
            return false;

        var palettes = LoadPalettes();
        var removed = palettes.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        SavePalettes(palettes);
        return true;
    }

    public bool IsDefaultPalette(string? name) => IsDefaultName(name);

    public bool IsBuiltInPalette(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && BuiltInPaletteNames.Any(n => n.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Flattened list of all named colors across every palette (used by plugin pickers).</summary>
    public List<NamedColor> GetAllNamedColors()
    {
        var result = new List<NamedColor>();
        foreach (var (_, color) in EnumeratePaletteColors())
            result.Add(new NamedColor { Name = color.Name, Hex = color.Hex });
        return result;
    }

    /// <summary>All named colors with their source palette (preserves palette order).</summary>
    public IEnumerable<(string PaletteName, NamedColor Color)> EnumeratePaletteColors()
    {
        foreach (var p in LoadPalettes())
        {
            foreach (var c in p.Colors)
            {
                if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Hex))
                    continue;
                yield return (p.Name, c);
            }
        }
    }

    private List<ColorPalette> BuildPaletteList(List<ColorPalette> userPalettes, Dictionary<string, PaletteCustomization> customizations)
    {
        var palettes = new List<ColorPalette>();
        foreach (var builtInName in BuiltInPaletteNames)
        {
            customizations.TryGetValue(builtInName, out var custom);
            palettes.Add(new ColorPalette
            {
                Name = builtInName,
                Colors = MergeBuiltInPalette(builtInName, custom),
            });
        }

        palettes.AddRange(userPalettes);
        return palettes;
    }

    private static ColorPalette? FindPalette(List<ColorPalette> palettes, string name)
        => palettes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsDefaultName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Trim().Equals(DefaultPaletteName, StringComparison.OrdinalIgnoreCase);

    private static void NormalizePalettes(List<ColorPalette> palettes)
    {
        foreach (var p in palettes)
        {
            p.Name = (p.Name ?? "").Trim();
            p.Colors ??= new List<NamedColor>();
            foreach (var c in p.Colors)
            {
                c.Name = (c.Name ?? "").Trim();
                c.Hex = NormalizeHex(c.Hex);
            }
        }
    }

    private void SaveUserPalettesOnly(List<ColorPalette> userPalettes)
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(userPalettes, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    private Dictionary<string, PaletteCustomization> LoadCustomizations()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new Dictionary<string, PaletteCustomization>(StringComparer.OrdinalIgnoreCase);

            var settings = _settingsStore.Load<WindowSettings>(SettingsPath);
            if (settings?.ColorPaletteCustomizations == null)
                return new Dictionary<string, PaletteCustomization>(StringComparer.OrdinalIgnoreCase);

            return settings.ColorPaletteCustomizations
                .ToDictionary(kv => kv.Key, kv => SanitizeCustomization(kv.Value), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, PaletteCustomization>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistCustomizations(Dictionary<string, PaletteCustomization> customizations)
    {
        try
        {
            Directory.CreateDirectory(_backupFolder);
            var settings = _settingsStore.Load<WindowSettings>(SettingsPath) ?? new WindowSettings();
            settings.ColorPaletteCustomizations = PruneEmptyCustomizations(customizations);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(SettingsPath, json);
        }
        catch
        {
            // non-critical
        }
    }

    private static Dictionary<string, PaletteCustomization>? PruneEmptyCustomizations(
        Dictionary<string, PaletteCustomization> customizations)
    {
        var result = new Dictionary<string, PaletteCustomization>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, custom) in customizations)
        {
            var sanitized = SanitizeCustomization(custom);
            if (sanitized.Added.Count == 0 && sanitized.RemovedHexes.Count == 0 && sanitized.HexOrder == null)
                continue;
            result[name] = sanitized;
        }

        return result.Count > 0 ? result : null;
    }

    private static PaletteCustomization SanitizeCustomization(PaletteCustomization? custom)
    {
        var result = new PaletteCustomization();
        if (custom == null)
            return result;

        foreach (var hex in custom.RemovedHexes ?? [])
        {
            var normalized = NormalizeHex(hex);
            if (normalized.Length > 0 && !result.RemovedHexes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                result.RemovedHexes.Add(normalized);
        }

        var addedHexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in custom.Added ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Hex))
                continue;
            var normalized = NormalizeHex(entry.Hex);
            if (normalized.Length == 0 || !addedHexes.Add(normalized))
                continue;
            result.Added.Add(new PaletteColorEntry { Name = entry.Name.Trim(), Hex = normalized });
        }

        if (custom.HexOrder is { Count: > 0 })
        {
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hex in custom.HexOrder)
            {
                var normalized = NormalizeHex(hex);
                if (normalized.Length > 0 && seen.Add(normalized))
                    order.Add(normalized);
            }

            if (order.Count > 0)
                result.HexOrder = order;
        }

        return result;
    }

    /// <summary>
    /// Built-in palette merge (Main, Diagrams, …): all code defaults, then user-added colors from
    /// <c>settings.json</c>, then remove user-removed hex values.
    /// </summary>
    private static List<NamedColor> MergeBuiltInPalette(string paletteName, PaletteCustomization? custom)
    {
        if (!BuiltInColorDefinitions.TryGetValue(paletteName, out var defaults))
            return new List<NamedColor>();

        var result = new List<NamedColor>();

        // 1. All default colors from code (updated on each app version).
        foreach (var (name, hex) in defaults)
        {
            var normalized = NormalizeHex(hex);
            if (normalized.Length == 0)
                continue;
            result.Add(new NamedColor { Name = name, Hex = normalized });
        }

        // 2. Colors the user added (persisted in settings.json).
        if (custom?.Added is { Count: > 0 })
        {
            var existingHexes = new HashSet<string>(
                result.Select(c => NormalizeHex(c.Hex)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in custom.Added)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Hex))
                    continue;
                var normalized = NormalizeHex(entry.Hex);
                if (normalized.Length == 0 || !existingHexes.Add(normalized))
                    continue;
                result.Add(new NamedColor { Name = entry.Name.Trim(), Hex = normalized });
            }
        }

        // 3. Colors the user removed (persisted in settings.json).
        if (custom?.RemovedHexes is { Count: > 0 })
        {
            var removed = new HashSet<string>(
                custom.RemovedHexes.Select(NormalizeHex).Where(h => h.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            result.RemoveAll(c => removed.Contains(NormalizeHex(c.Hex)));
        }

        if (custom?.HexOrder is { Count: > 0 })
            result = ApplyHexOrder(result, custom.HexOrder);

        return result;
    }

    private static List<NamedColor> ApplyHexOrder(List<NamedColor> colors, List<string> hexOrder)
    {
        var byHex = colors.ToDictionary(c => NormalizeHex(c.Hex), c => c, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<NamedColor>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hex in hexOrder)
        {
            var normalized = NormalizeHex(hex);
            if (byHex.TryGetValue(normalized, out var color) && used.Add(normalized))
                ordered.Add(color);
        }

        foreach (var color in colors)
        {
            var normalized = NormalizeHex(color.Hex);
            if (used.Add(normalized))
                ordered.Add(color);
        }

        return ordered;
    }

    private static PaletteCustomization ComputeCustomization(string paletteName, List<NamedColor> effectiveColors)
    {
        if (!BuiltInColorDefinitions.TryGetValue(paletteName, out var defaults))
            return new PaletteCustomization();

        var defaultHexes = new HashSet<string>(
            defaults.Select(d => NormalizeHex(d.Hex)),
            StringComparer.OrdinalIgnoreCase);

        var effective = effectiveColors
            .Where(c => !string.IsNullOrWhiteSpace(c.Hex))
            .Select(c => new NamedColor { Name = c.Name.Trim(), Hex = NormalizeHex(c.Hex) })
            .ToList();

        var effectiveHexes = effective.Select(c => c.Hex).ToList();
        var effectiveHexSet = new HashSet<string>(effectiveHexes, StringComparer.OrdinalIgnoreCase);

        var removed = defaults
            .Select(d => NormalizeHex(d.Hex))
            .Where(h => h.Length > 0 && !effectiveHexSet.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var added = new List<PaletteColorEntry>();
        var addedHexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var color in effective)
        {
            if (defaultHexes.Contains(color.Hex) || !addedHexes.Add(color.Hex))
                continue;
            added.Add(new PaletteColorEntry { Name = color.Name, Hex = color.Hex });
        }

        var mergedDefault = MergeBuiltInPalette(paletteName, new PaletteCustomization
        {
            RemovedHexes = removed,
            Added = added,
        });

        var customization = new PaletteCustomization
        {
            RemovedHexes = removed,
            Added = added,
        };

        if (!HexOrdersMatch(effective, mergedDefault))
            customization.HexOrder = effectiveHexes;

        return customization;
    }

    private static bool HexOrdersMatch(List<NamedColor> a, List<NamedColor> b)
    {
        var ah = a.Select(c => NormalizeHex(c.Hex)).ToList();
        var bh = b.Select(c => NormalizeHex(c.Hex)).ToList();
        return ah.SequenceEqual(bh, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "";
        var trimmed = hex.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;
        return trimmed.ToUpperInvariant();
    }

    private static string NormalizeBackupFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return DefaultBackupFolder();
        try
        {
            return Path.GetFullPath(folder.Trim());
        }
        catch
        {
            return DefaultBackupFolder();
        }
    }

    private static readonly Dictionary<string, (string Name, string Hex)[]> BuiltInColorDefinitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultPaletteName] =
            [
                ("Black",        "#000000"),
                ("White",        "#FFFFFF"),
                ("Dark gray",    "#555555"),
                ("Light gray",   "#BBBBBB"),
                ("Red",          "#E53935"),
                ("Orange",       "#F68A1E"),
                ("Gold",         "#FFD700"),
                ("Pale yellow",  "#FFF6A9"),
                ("Green",        "#4CAF50"),
                ("Light green",  "#A8E6A1"),
                ("Blue",         "#2196F3"),
                ("Light blue",   "#B3E5FC"),
                ("Purple",       "#673AB7"),
                ("Lavender",     "#D1C4E9"),
                ("Brown",        "#795548"),
                ("Indigo",       "#3F51B5"),
                ("Beige",        "#F5F5DC"),
            ],
            [DiagramsPaletteName] =
            [
                ("Periwinkle",  "#DAE8FC"),
                ("Mint",        "#D5E8D4"),
                ("Cream",       "#FFF2CC"),
                ("Smoke",       "#F5F5F5"),
                ("Aqua",        "#B0E3E6"),
                ("Silver",      "#B3B3B3"),
                ("Steel blue",  "#7EA6E0"),
                ("Sage",        "#97D077"),
            ],
        };

    private static List<ColorPalette> TryMigrateFromLegacy()
    {
        var palettes = new List<ColorPalette>();
        try
        {
            var legacyPath = Path.Combine(DefaultBackupFolder(), LegacyNamedColorsFileName);
            if (!File.Exists(legacyPath))
                return palettes;

            var legacy = JsonSerializer.Deserialize<List<LegacyNamedColor>>(File.ReadAllText(legacyPath));
            if (legacy == null || legacy.Count == 0)
                return palettes;

            var main = new ColorPalette { Name = DefaultPaletteName };
            foreach (var n in legacy)
            {
                if (n == null || string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Hex))
                    continue;
                main.Colors.Add(new NamedColor { Name = n.Name.Trim(), Hex = NormalizeHex(n.Hex) });
            }
            palettes.Add(main);
        }
        catch
        {
            // best effort migration
        }
        return palettes;
    }

    private sealed class LegacyNamedColor
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "#000000";
    }
}
