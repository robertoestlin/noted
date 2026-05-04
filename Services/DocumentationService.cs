using System.IO;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Persists Documentation packages to <c>{BackupFolder}/doc-packages/</c> as one
/// JSON file per package plus an <c>_index.json</c> file holding ordering and the
/// last-selected package id.
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

        foreach (var file in Directory.EnumerateFiles(folder, PackageFilePrefix + "*" + PackageFileSuffix))
        {
            try
            {
                var pkg = JsonSerializer.Deserialize<DocPackage>(File.ReadAllText(file));
                if (pkg is null || string.IsNullOrEmpty(pkg.Id))
                    continue;
                result.Add(pkg);
            }
            catch
            {
                // Skip unreadable file — best effort.
            }
        }
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
        try
        {
            var path = GetPackagePath(backupFolder, packageId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
