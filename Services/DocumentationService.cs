using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Persists Documentation packages under <c>{BackupFolder}/doc-packages/</c> as <c>.docp</c> zip files.
/// Each <c>.docp</c> is a zip that contains:
///   <list type="bullet">
///     <item><c>package.json</c> — the <see cref="DocPackage"/> tree (page <see cref="DocNode.Content"/> emptied; stored separately).</item>
///     <item><c>pages/{nodeId}.md</c> — one plain-text file per Page/SubPage node.</item>
///     <item><c>images/{filename}.png</c> — pasted images referenced by <c>^&lt;name.png&gt;</c> markers in page text.</item>
///   </list>
/// Legacy <c>doc-package-{Id}.json</c> files are still read; they are migrated to <c>.docp</c> on the next save.
/// </summary>
public sealed class DocumentationService
{
    public const string SubfolderName = "doc-packages";
    public const string IndexFileName = "_index.json";
    public const string PackageFilePrefix = "doc-package-";
    public const string PackageFileExtension = ".docp";
    public const string LegacyPackageFileExtension = ".json";

    public const string ZipPackageEntryName = "package.json";
    public const string ZipPagesFolder = "pages/";
    public const string ZipImagesFolder = "images/";

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public string GetSubfolderPath(string backupFolder)
        => Path.Combine(backupFolder, SubfolderName);

    public string GetIndexPath(string backupFolder)
        => Path.Combine(GetSubfolderPath(backupFolder), IndexFileName);

    public string GetPackagePath(string backupFolder, string packageId)
        => Path.Combine(GetSubfolderPath(backupFolder), PackageFilePrefix + packageId + PackageFileExtension);

    public string GetLegacyPackagePath(string backupFolder, string packageId)
        => Path.Combine(GetSubfolderPath(backupFolder), PackageFilePrefix + packageId + LegacyPackageFileExtension);

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

        foreach (var file in Directory.EnumerateFiles(folder, "*" + PackageFileExtension)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var pkg = TryLoadDocPackageZip(file);
            if (pkg is null || string.IsNullOrEmpty(pkg.Id))
                continue;
            if (!byId.ContainsKey(pkg.Id))
                byId[pkg.Id] = pkg;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*" + LegacyPackageFileExtension)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            if (IsPackagesIndexFile(file))
                continue;
            var pkg = TryLoadLegacyJsonPackage(file);
            if (pkg is null || string.IsNullOrEmpty(pkg.Id))
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
        WritePackageZip(path, package);

        var legacyPath = GetLegacyPackagePath(backupFolder, package.Id);
        if (File.Exists(legacyPath))
        {
            try { File.Delete(legacyPath); }
            catch { /* best effort */ }
        }
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

        var docpPath = GetPackagePath(backupFolder, packageId);
        if (File.Exists(docpPath))
        {
            try { File.Delete(docpPath); }
            catch { /* best effort */ }
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*" + LegacyPackageFileExtension))
            {
                if (IsPackagesIndexFile(file))
                    continue;
                var pkg = TryLoadLegacyJsonPackage(file);
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

    /// <summary>Reads an image entry stored under <c>images/{fileName}</c> inside the package's <c>.docp</c> zip.</summary>
    public byte[]? TryReadImage(string backupFolder, string packageId, string fileName)
    {
        if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(fileName))
            return null;
        var path = GetPackagePath(backupFolder, packageId);
        if (!File.Exists(path))
            return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry(ZipImagesFolder + fileName);
            if (entry == null)
                return null;
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns true when the package zip contains an image entry with the given filename.</summary>
    public bool ImageExists(string backupFolder, string packageId, string fileName)
    {
        if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(fileName))
            return false;
        var path = GetPackagePath(backupFolder, packageId);
        if (!File.Exists(path))
            return false;
        try
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            return zip.GetEntry(ZipImagesFolder + fileName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Picks an image filename that doesn't yet exist inside the package zip.</summary>
    public string EnsureUniqueImageName(string backupFolder, string packageId, string desiredFileName)
    {
        if (string.IsNullOrEmpty(desiredFileName))
            return desiredFileName;

        var path = GetPackagePath(backupFolder, packageId);
        if (!File.Exists(path))
            return desiredFileName;

        try
        {
            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            return ChooseUniqueImageName(zip, desiredFileName);
        }
        catch
        {
            return desiredFileName;
        }
    }

    /// <summary>Writes (or overwrites) <paramref name="bytes"/> as <c>images/{fileName}</c> inside the package zip.
    /// Creates the zip if it doesn't exist yet (preserving any existing package state).</summary>
    public void WriteImage(string backupFolder, string packageId, string fileName, byte[] bytes)
    {
        if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(fileName))
            return;
        EnsureFolderExists(backupFolder);
        var path = GetPackagePath(backupFolder, packageId);

        if (!File.Exists(path))
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            WriteZipEntryBytes(zip, ZipImagesFolder + fileName, bytes);
            return;
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
        {
            var existing = zip.GetEntry(ZipImagesFolder + fileName);
            existing?.Delete();
            WriteZipEntryBytes(zip, ZipImagesFolder + fileName, bytes);
        }
    }

    static bool IsPackagesIndexFile(string filePath)
        => string.Equals(Path.GetFileName(filePath), IndexFileName, StringComparison.OrdinalIgnoreCase);

    static DocPackage? TryLoadLegacyJsonPackage(string filePath)
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

    static DocPackage? TryLoadDocPackageZip(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var packageEntry = zip.GetEntry(ZipPackageEntryName);
            if (packageEntry == null)
                return null;

            DocPackage? pkg;
            using (var stream = packageEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                pkg = JsonSerializer.Deserialize<DocPackage>(reader.ReadToEnd());
            }
            if (pkg == null || string.IsNullOrEmpty(pkg.Id))
                return null;

            FillNodeContentFromZip(pkg.Nodes, zip);
            return pkg;
        }
        catch
        {
            return null;
        }
    }

    static void FillNodeContentFromZip(IEnumerable<DocNode> nodes, ZipArchive zip)
    {
        foreach (var node in nodes)
        {
            if (node.Kind is DocNodeKind.Page or DocNodeKind.SubPage)
            {
                var pageEntry = zip.GetEntry(ZipPagesFolder + node.Id + ".md");
                if (pageEntry != null)
                {
                    using var s = pageEntry.Open();
                    using var r = new StreamReader(s, Encoding.UTF8);
                    node.Content = r.ReadToEnd();
                }
            }
            FillNodeContentFromZip(node.Children, zip);
        }
    }

    static void WritePackageZip(string path, DocPackage package)
    {
        var pageIds = new HashSet<string>(StringComparer.Ordinal);
        CollectPageIds(package.Nodes, pageIds);

        var packageJson = JsonSerializer.Serialize(BuildSkeletonPackage(package), WriteOptions);

        var pageContents = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectPageContents(package.Nodes, pageContents);

        if (!File.Exists(path))
        {
            WriteFreshZip(path, packageJson, pageContents);
            return;
        }

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Update);

            zip.GetEntry(ZipPackageEntryName)?.Delete();
            WriteZipEntryText(zip, ZipPackageEntryName, packageJson);

            var orphanedPageEntries = zip.Entries
                .Where(e => e.FullName.StartsWith(ZipPagesFolder, StringComparison.Ordinal))
                .ToList();
            foreach (var orphan in orphanedPageEntries)
                orphan.Delete();

            foreach (var (pageId, content) in pageContents)
                WriteZipEntryText(zip, ZipPagesFolder + pageId + ".md", content ?? string.Empty);
        }
        catch
        {
            WriteFreshZip(path, packageJson, pageContents);
        }
    }

    static void WriteFreshZip(string path, string packageJson, Dictionary<string, string> pageContents)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        WriteZipEntryText(zip, ZipPackageEntryName, packageJson);
        foreach (var (pageId, content) in pageContents)
            WriteZipEntryText(zip, ZipPagesFolder + pageId + ".md", content ?? string.Empty);
    }

    static void WriteZipEntryText(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        w.Write(text);
    }

    static void WriteZipEntryBytes(ZipArchive zip, string entryName, byte[] bytes)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    static string ChooseUniqueImageName(ZipArchive zip, string desiredFileName)
    {
        if (zip.GetEntry(ZipImagesFolder + desiredFileName) == null)
            return desiredFileName;

        var baseName = Path.GetFileNameWithoutExtension(desiredFileName);
        var extension = Path.GetExtension(desiredFileName);
        for (int suffix = 1; suffix < 10000; suffix++)
        {
            var candidate = $"{baseName}-{suffix:00}{extension}";
            if (zip.GetEntry(ZipImagesFolder + candidate) == null)
                return candidate;
        }
        return desiredFileName;
    }

    static void CollectPageIds(IEnumerable<DocNode> nodes, HashSet<string> ids)
    {
        foreach (var node in nodes)
        {
            if (node.Kind is DocNodeKind.Page or DocNodeKind.SubPage)
                ids.Add(node.Id);
            CollectPageIds(node.Children, ids);
        }
    }

    static void CollectPageContents(IEnumerable<DocNode> nodes, Dictionary<string, string> contents)
    {
        foreach (var node in nodes)
        {
            if (node.Kind is DocNodeKind.Page or DocNodeKind.SubPage)
                contents[node.Id] = node.Content ?? string.Empty;
            CollectPageContents(node.Children, contents);
        }
    }

    /// <summary>Returns a copy of <paramref name="package"/> where Page/SubPage <c>Content</c> is cleared (stored separately).</summary>
    static DocPackage BuildSkeletonPackage(DocPackage package)
        => new()
        {
            Id = package.Id,
            Name = package.Name,
            CurrentNodeId = package.CurrentNodeId,
            Nodes = package.Nodes.Select(BuildSkeletonNode).ToList()
        };

    static DocNode BuildSkeletonNode(DocNode source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            Content = source.Kind is DocNodeKind.Page or DocNodeKind.SubPage ? null : source.Content,
            Metadata = source.Metadata,
            Children = source.Children.Select(BuildSkeletonNode).ToList()
        };
}
