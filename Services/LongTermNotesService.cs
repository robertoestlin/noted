using System.IO;
using System.Text.Json;
using Noted.Models;

namespace Noted.Services;

/// <summary>
/// Persists Long-Term Notes notebooks to <c>{BackupFolder}/notebooks/</c> as one
/// JSON file per notebook plus an <c>_index.json</c> file holding ordering and the
/// last-selected notebook id.
/// </summary>
public sealed class LongTermNotesService
{
    public const string SubfolderName = "notebooks";
    public const string IndexFileName = "_index.json";
    public const string NotebookFilePrefix = "notebook-";
    public const string NotebookFileSuffix = ".json";

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public string GetSubfolderPath(string backupFolder)
        => Path.Combine(backupFolder, SubfolderName);

    public string GetIndexPath(string backupFolder)
        => Path.Combine(GetSubfolderPath(backupFolder), IndexFileName);

    public string GetNotebookPath(string backupFolder, string notebookId)
        => Path.Combine(GetSubfolderPath(backupFolder), NotebookFilePrefix + notebookId + NotebookFileSuffix);

    public void EnsureFolderExists(string backupFolder)
    {
        try { Directory.CreateDirectory(GetSubfolderPath(backupFolder)); }
        catch { /* best effort */ }
    }

    /// <summary>Loads every <c>notebook-*.json</c> file in the folder. Returns empty list if folder missing.</summary>
    public List<Notebook> LoadAllNotebooks(string backupFolder)
    {
        var result = new List<Notebook>();
        var folder = GetSubfolderPath(backupFolder);
        if (!Directory.Exists(folder))
            return result;

        foreach (var file in Directory.EnumerateFiles(folder, NotebookFilePrefix + "*" + NotebookFileSuffix))
        {
            try
            {
                var nb = JsonSerializer.Deserialize<Notebook>(File.ReadAllText(file));
                if (nb is null || string.IsNullOrEmpty(nb.Id))
                    continue;
                result.Add(nb);
            }
            catch
            {
                // Skip unreadable file — best effort.
            }
        }
        return result;
    }

    public NotebooksIndex LoadIndex(string backupFolder)
    {
        var path = GetIndexPath(backupFolder);
        if (!File.Exists(path))
            return new NotebooksIndex();
        try
        {
            return JsonSerializer.Deserialize<NotebooksIndex>(File.ReadAllText(path)) ?? new NotebooksIndex();
        }
        catch
        {
            return new NotebooksIndex();
        }
    }

    /// <summary>
    /// Writes a single notebook to disk if the JSON differs from what's already there
    /// (semantic comparison — no needless writes when nothing changed).
    /// </summary>
    public void SaveNotebook(string backupFolder, Notebook notebook)
    {
        if (string.IsNullOrEmpty(notebook.Id))
            return;
        EnsureFolderExists(backupFolder);
        var path = GetNotebookPath(backupFolder, notebook.Id);
        var json = JsonSerializer.Serialize(notebook, WriteOptions);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(path, json);
    }

    public void SaveIndex(string backupFolder, NotebooksIndex index)
    {
        EnsureFolderExists(backupFolder);
        var path = GetIndexPath(backupFolder);
        var json = JsonSerializer.Serialize(index, WriteOptions);
        WindowSettingsStore.WriteUtf8IfSemanticJsonChanged(path, json);
    }

    public void DeleteNotebook(string backupFolder, string notebookId)
    {
        if (string.IsNullOrEmpty(notebookId))
            return;
        try
        {
            var path = GetNotebookPath(backupFolder, notebookId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort; don't crash the app on a deletion error.
        }
    }
}
