using System.IO;
using System.Linq;
using System.Text.Json;

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
/// App-wide store for named color palettes. Shared across plugins via <c>color-palettes.json</c>.
/// The "Main" palette always exists; other palettes can be created/renamed/deleted by users.
/// </summary>
public sealed class ColorPaletteService
{
    public const string DefaultPaletteName = "Main";

    private const string FolderName = @"c:\tools\backup\noted";
    private const string FileName = "color-palettes.json";
    private const string LegacyNamedColorsFileName = "drawing-named-colors.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(FolderName, FileName);

    public List<ColorPalette> LoadPalettes()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<List<ColorPalette>>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    NormalizePalettes(loaded);
                    return loaded;
                }
            }

            var migrated = TryMigrateFromLegacy();
            NormalizePalettes(migrated);
            SavePalettes(migrated);
            return migrated;
        }
        catch
        {
            var fallback = new List<ColorPalette> { new() { Name = DefaultPaletteName } };
            return fallback;
        }
    }

    public ColorPalette GetOrCreateDefault()
    {
        var palettes = LoadPalettes();
        var main = FindDefault(palettes);
        if (main != null)
            return main;

        var created = new ColorPalette { Name = DefaultPaletteName };
        palettes.Insert(0, created);
        SavePalettes(palettes);
        return created;
    }

    public void SavePalettes(List<ColorPalette> palettes)
    {
        try
        {
            NormalizePalettes(palettes);
            Directory.CreateDirectory(FolderName);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(palettes, JsonOptions));
        }
        catch
        {
            // non-critical
        }
    }

    public void AddOrUpdateColor(string paletteName, NamedColor color)
    {
        if (color == null || string.IsNullOrWhiteSpace(color.Name) || string.IsNullOrWhiteSpace(color.Hex))
            return;

        var palettes = LoadPalettes();
        var target = palettes.FirstOrDefault(p => p.Name.Equals(paletteName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            target = new ColorPalette { Name = paletteName.Trim() };
            palettes.Add(target);
        }

        var existing = target.Colors.FirstOrDefault(c => c.Name.Equals(color.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Hex = color.Hex.Trim();
        else
            target.Colors.Add(new NamedColor { Name = color.Name.Trim(), Hex = color.Hex.Trim() });

        SavePalettes(palettes);
    }

    public void RemoveColor(string paletteName, string colorName)
    {
        if (string.IsNullOrWhiteSpace(paletteName) || string.IsNullOrWhiteSpace(colorName))
            return;

        var palettes = LoadPalettes();
        var target = palettes.FirstOrDefault(p => p.Name.Equals(paletteName, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return;

        var removed = target.Colors.RemoveAll(c => c.Name.Equals(colorName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            SavePalettes(palettes);
    }

    public bool CreatePalette(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
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
        if (string.IsNullOrWhiteSpace(oldName) || trimmedNew.Length == 0)
            return false;

        if (IsDefaultName(oldName))
            return false;

        var palettes = LoadPalettes();
        var target = palettes.FirstOrDefault(p => p.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
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
        if (string.IsNullOrWhiteSpace(name) || IsDefaultName(name))
            return false;

        var palettes = LoadPalettes();
        var removed = palettes.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        SavePalettes(palettes);
        return true;
    }

    public bool IsDefaultPalette(string? name) => IsDefaultName(name);

    /// <summary>Flattened list of all named colors across every palette (used by plugin pickers).</summary>
    public List<NamedColor> GetAllNamedColors()
    {
        var result = new List<NamedColor>();
        foreach (var p in LoadPalettes())
        {
            foreach (var c in p.Colors)
            {
                if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Hex))
                    continue;
                result.Add(new NamedColor { Name = c.Name, Hex = c.Hex });
            }
        }
        return result;
    }

    private static bool IsDefaultName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && name.Trim().Equals(DefaultPaletteName, StringComparison.OrdinalIgnoreCase);

    private static ColorPalette? FindDefault(List<ColorPalette> palettes)
        => palettes.FirstOrDefault(p => IsDefaultName(p.Name));

    private static void NormalizePalettes(List<ColorPalette> palettes)
    {
        foreach (var p in palettes)
        {
            p.Name = (p.Name ?? "").Trim();
            p.Colors ??= new List<NamedColor>();
            foreach (var c in p.Colors)
            {
                c.Name = (c.Name ?? "").Trim();
                c.Hex = (c.Hex ?? "").Trim();
            }
        }

        if (FindDefault(palettes) == null)
            palettes.Insert(0, new ColorPalette { Name = DefaultPaletteName });
    }

    private static List<ColorPalette> TryMigrateFromLegacy()
    {
        var palettes = new List<ColorPalette>();
        try
        {
            var legacyPath = Path.Combine(FolderName, LegacyNamedColorsFileName);
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
                main.Colors.Add(new NamedColor { Name = n.Name.Trim(), Hex = n.Hex.Trim() });
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
