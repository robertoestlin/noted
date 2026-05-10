using System.IO;
using System.Linq;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Persists Documentation packages under <c>{BackupFolder}/doc-packages/</c>.
/// Any <c>*.json</c> in that folder is attempted as a <see cref="DocPackage"/> except <c>_index.json</c>
/// (package ordering). Saves still use <c>doc-package-{Id}.json</c>.
/// </summary>
public sealed class DocumentationService
{
    public const string SubfolderName = "doc-packages";
    public const string IndexFileName = "_index.json";
    public const string PackageFilePrefix = "doc-package-";
    public const string PackageFileSuffix = ".json";

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public string GetSubfolderPath(string backupFolder)
        => Path.Combine(backupFolder, SubfolderName);

    public string GetIndexPath(string backupFolder)
        => Path.Combine(GetSubfolderPath(backupFolder), IndexFileName);

    public string GetPackagePath(string backupFolder, string packageId)
        => Path.Combine(GetSubfolderPath(backupFolder), PackageFilePrefix + packageId + PackageFileSuffix);

    public void EnsureFolderExists(string backupFolder)
    {
        try { Directory.CreateDirectory(GetSubfolderPath(backupFolder)); }
        catch { /* best effort */ }
    }

    public List<DocPackage> LoadAllPackages(string backupFolder)
    {
        var result = new List<DocPackage>();
        var folder = GetSubfolderPath(backupFolder);
        if (!Directory.Exists(folder))
            return result;

        var byId = new Dictionary<string, DocPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            if (IsPackagesIndexFile(file))
                continue;
            var pkg = TryLoadDocPackage(file);
            if (pkg is null)
                continue;
            if (!byId.ContainsKey(pkg.Id))
                byId[pkg.Id] = pkg;
        }

        foreach (var pkg in byId.Values)
            result.Add(pkg);
        return result;
    }

    public DocPackagesIndex LoadIndex(string backupFolder)
    {
        var path = GetIndexPath(backupFolder);
        if (!File.Exists(path))
            return new DocPackagesIndex();
        try
        {
            return JsonSerializer.Deserialize<DocPackagesIndex>(File.ReadAllText(path)) ?? new DocPackagesIndex();
        }
        catch
        {
            return new DocPackagesIndex();
        }
    }

    public void SavePackage(string backupFolder, DocPackage package)
    {
        if (string.IsNullOrEmpty(package.Id))
            return;
        EnsureFolderExists(backupFolder);
        var path = GetPackagePath(backupFolder, package.Id);
        var json = JsonSerializer.Serialize(package, WriteOptions);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(path, json);
        DeleteAlternatePackageJsonFiles(backupFolder, package.Id, path);
    }

    public void SaveIndex(string backupFolder, DocPackagesIndex index)
    {
        EnsureFolderExists(backupFolder);
        var path = GetIndexPath(backupFolder);
        var json = JsonSerializer.Serialize(index, WriteOptions);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(path, json);
    }

    public void DeletePackage(string backupFolder, string packageId)
    {
        if (string.IsNullOrEmpty(packageId))
            return;
        var folder = GetSubfolderPath(backupFolder);
        if (!Directory.Exists(folder))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (IsPackagesIndexFile(file))
                    continue;
                var pkg = TryLoadDocPackage(file);
                if (pkg is null)
                    continue;
                if (!string.Equals(pkg.Id, packageId, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(file); }
                catch { /* best effort */ }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    static bool IsPackagesIndexFile(string filePath)
        => string.Equals(Path.GetFileName(filePath), IndexFileName, StringComparison.OrdinalIgnoreCase);

    static DocPackage? TryLoadDocPackage(string filePath)
    {
        try
        {
            var pkg = JsonSerializer.Deserialize<DocPackage>(File.ReadAllText(filePath));
            if (pkg is null || string.IsNullOrEmpty(pkg.Id))
                return null;
            return pkg;
        }
        catch
        {
            return null;
        }
    }

    static void DeleteAlternatePackageJsonFiles(string backupFolder, string packageId, string canonicalPathFull)
    {
        var folder = Path.Combine(backupFolder, SubfolderName);
        if (!Directory.Exists(folder))
            return;
        var canonicalFull = Path.GetFullPath(canonicalPathFull);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (IsPackagesIndexFile(file))
                continue;
            if (string.Equals(Path.GetFullPath(file), canonicalFull, StringComparison.OrdinalIgnoreCase))
                continue;
            var pkg = TryLoadDocPackage(file);
            if (pkg is null)
                continue;
            if (!string.Equals(pkg.Id, packageId, StringComparison.OrdinalIgnoreCase))
                continue;
            try { File.Delete(file); }
            catch { /* best effort */ }
        }
    }
}
