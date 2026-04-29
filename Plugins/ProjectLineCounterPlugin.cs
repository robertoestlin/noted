using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Noted.Models;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    private static readonly string[] DefaultProjectLineCounterIgnoredFileTypes =
    [
        ".exe", ".dll", ".pdb", ".obj", ".bin", ".cache", ".class", ".jar",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".zip", ".7z", ".pdf", ".mid", ".midi", ".gitignore", ".md"
    ];
    private List<ProjectLineCounterProject> BuildProjectLineCounterProjectsSnapshot()
        => NormalizeProjectLineCounterProjects(
            _projectLineCounterProjects,
            _projectLineCounterTypes,
            _projectLineCounterIgnoredFileTypes);

    private List<ProjectLineCounterType> BuildProjectLineCounterTypesSnapshot()
        => NormalizeProjectLineCounterTypes(_projectLineCounterTypes);

    private List<string> BuildProjectLineCounterIgnoredFileTypesSnapshot()
        => NormalizeProjectLineCounterIgnoredFileTypes(_projectLineCounterIgnoredFileTypes);

    private void ApplyProjectLineCounterSettings(
        IEnumerable<ProjectLineCounterProject>? projects,
        IEnumerable<ProjectLineCounterType>? types,
        IEnumerable<string>? ignoredFileTypes)
    {
        _projectLineCounterTypes = NormalizeProjectLineCounterTypes(types);
        _projectLineCounterIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(ignoredFileTypes);
        _projectLineCounterProjects = NormalizeProjectLineCounterProjects(
            projects,
            _projectLineCounterTypes,
            _projectLineCounterIgnoredFileTypes);
    }

    private static string? NormalizeProjectLineCounterFileType(string? fileType)
    {
        var value = (fileType ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
            return null;

        if (value.StartsWith("*."))
            value = value[1..];
        if (!value.StartsWith('.'))
            value = "." + value;

        if (value.Length < 2)
            return null;
        return value;
    }

    private static List<string> NormalizeProjectLineCounterFileTypes(IEnumerable<string>? fileTypes)
    {
        return (fileTypes ?? [])
            .Select(NormalizeProjectLineCounterFileType)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeProjectLineCounterIgnoredFileTypes(IEnumerable<string>? fileTypes)
    {
        var normalized = NormalizeProjectLineCounterFileTypes(fileTypes);
        if (normalized.Count == 0)
        {
            normalized = DefaultProjectLineCounterIgnoredFileTypes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!normalized.Contains(".md", StringComparer.OrdinalIgnoreCase))
            normalized.Add(".md");
        if (!normalized.Contains(".mid", StringComparer.OrdinalIgnoreCase))
            normalized.Add(".mid");
        if (!normalized.Contains(".midi", StringComparer.OrdinalIgnoreCase))
            normalized.Add(".midi");

        return normalized
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeProjectLineCounterFolderRule(string? folderRule)
    {
        var value = (folderRule ?? string.Empty).Trim();
        if (value.Length == 0)
            return null;

        value = value.Replace('\\', '/').Trim('/');
        if (value.Length == 0)
            return null;
        return value.ToLowerInvariant();
    }

    private static List<string> NormalizeProjectLineCounterIgnoredFolders(IEnumerable<string>? ignoredFolders)
    {
        return (ignoredFolders ?? [])
            .Select(NormalizeProjectLineCounterFolderRule)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Parses lines from <see cref="CountProjectLines"/> not-counted output: <c>relativePath [label] - reason</c>.</summary>
    private static bool TryParseNotCountedDisplayLine(string line, out string relativePath, out string bracketLabel)
    {
        relativePath = string.Empty;
        bracketLabel = string.Empty;
        const string reasonSep = "] - ";
        var closeIdx = line.IndexOf(reasonSep, StringComparison.Ordinal);
        if (closeIdx < 0)
            return false;
        var openIdx = line.LastIndexOf(" [", closeIdx, StringComparison.Ordinal);
        if (openIdx < 0)
            return false;
        relativePath = line[..openIdx].TrimEnd();
        bracketLabel = line.Substring(openIdx + 2, closeIdx - (openIdx + 2)).Trim();
        return relativePath.Length > 0 && bracketLabel.Length > 0;
    }

    private static void SelectListBoxItemUnderMouse(ListBox listBox, MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(listBox, e.GetPosition(listBox))?.VisualHit;
        for (var d = hit as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem { Content: string s })
            {
                listBox.SelectedItem = s;
                return;
            }
        }
    }

    private static List<ProjectLineCounterType> BuildDefaultProjectLineCounterTypes()
        =>
        [
            new ProjectLineCounterType
            {
                Name = "C#",
                FileTypes = [".cs", ".csproj", ".sln", ".xaml", ".xml", ".json", ".yaml", ".yml", ".config", ".ps1", ".sh"],
                IgnoredFileTypes = [],
                IgnoredFolders = [".git", "bin", "obj"]
            },
            new ProjectLineCounterType
            {
                Name = "Web",
                FileTypes = [".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".scss", ".json"],
                IgnoredFileTypes = [],
                IgnoredFolders = []
            }
        ];

    private static List<ProjectLineCounterType> NormalizeProjectLineCounterTypes(IEnumerable<ProjectLineCounterType>? types)
    {
        var normalized = (types ?? BuildDefaultProjectLineCounterTypes())
            .Select(type => new ProjectLineCounterType
            {
                Name = (type?.Name ?? string.Empty).Trim(),
                FileTypes = NormalizeProjectLineCounterFileTypes(type?.FileTypes),
                IgnoredFileTypes = NormalizeProjectLineCounterFileTypes(type?.IgnoredFileTypes),
                IgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(type?.IgnoredFolders)
            })
            .Where(type => type.Name.Length > 0
                && !string.Equals(type.Name, "Python", StringComparison.OrdinalIgnoreCase))
            .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProjectLineCounterType
            {
                Name = group.First().Name,
                FileTypes = group.SelectMany(type => type.FileTypes ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                IgnoredFileTypes = group.SelectMany(type => type.IgnoredFileTypes ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                IgnoredFolders = group.SelectMany(type => type.IgnoredFolders ?? [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            normalized = BuildDefaultProjectLineCounterTypes();

        var csharpType = normalized.FirstOrDefault(type => string.Equals(type.Name, "C#", StringComparison.OrdinalIgnoreCase));
        if (csharpType != null)
        {
            csharpType.FileTypes ??= [];
            foreach (var required in new[] { ".config", ".ps1", ".sh", ".yaml", ".yml" })
            {
                if (!csharpType.FileTypes.Contains(required, StringComparer.OrdinalIgnoreCase))
                    csharpType.FileTypes.Add(required);
            }

            csharpType.FileTypes = csharpType.FileTypes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            csharpType.IgnoredFolders ??= [];
            foreach (var requiredFolder in new[] { ".git", "bin", "obj" })
            {
                if (!csharpType.IgnoredFolders.Contains(requiredFolder, StringComparer.OrdinalIgnoreCase))
                    csharpType.IgnoredFolders.Add(requiredFolder);
            }

            csharpType.IgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(csharpType.IgnoredFolders);
        }

        return normalized;
    }

    private static List<ProjectLineCounterProject> NormalizeProjectLineCounterProjects(
        IEnumerable<ProjectLineCounterProject>? projects,
        IReadOnlyCollection<ProjectLineCounterType> knownTypes,
        IReadOnlyCollection<string>? globalIgnoredFileTypes)
    {
        var defaultTypeName = knownTypes.FirstOrDefault()?.Name ?? string.Empty;
        var knownTypeNames = new HashSet<string>(knownTypes.Select(type => type.Name), StringComparer.OrdinalIgnoreCase);
        var globals = NormalizeProjectLineCounterFileTypes(globalIgnoredFileTypes);

        return (projects ?? [])
            .Select(project =>
            {
                var trimmedName = (project?.Name ?? string.Empty).Trim();
                var folderPath = (project?.FolderPath ?? string.Empty).Trim();
                var typeName = (project?.TypeName ?? string.Empty).Trim();
                if (!knownTypeNames.Contains(typeName))
                    typeName = defaultTypeName;

                try
                {
                    if (folderPath.Length > 0)
                        folderPath = Path.GetFullPath(folderPath);
                }
                catch
                {
                    // Keep raw path when not parseable.
                }

                var typeForProject = knownTypes.FirstOrDefault(t =>
                    string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
                var typeFileTypes = NormalizeProjectLineCounterFileTypes(typeForProject?.FileTypes);
                var typeIncludeSet = new HashSet<string>(typeFileTypes, StringComparer.OrdinalIgnoreCase);
                var rawIncludeExtras = NormalizeProjectLineCounterFileTypes(project?.IncludedFileTypeOverrides);
                var includeExtrasOnly = rawIncludeExtras
                    .Where(ext => !typeIncludeSet.Contains(ext))
                    .ToList();
                includeExtrasOnly = NormalizeProjectLineCounterFileTypes(includeExtrasOnly);

                var typeIgnores = NormalizeProjectLineCounterFileTypes(typeForProject?.IgnoredFileTypes);
                var baseExcludeSet = new HashSet<string>(
                    NormalizeProjectLineCounterFileTypes(globals.Concat(typeIgnores)),
                    StringComparer.OrdinalIgnoreCase);
                var rawExcludeExtras = NormalizeProjectLineCounterFileTypes(project?.ExcludedFileTypeOverrides);
                var excludeExtrasOnly = rawExcludeExtras
                    .Where(ext => !baseExcludeSet.Contains(ext))
                    .ToList();
                excludeExtrasOnly = NormalizeProjectLineCounterFileTypes(excludeExtrasOnly);

                return new ProjectLineCounterProject
                {
                    Name = trimmedName,
                    FolderPath = folderPath,
                    TypeName = typeName,
                    IncludedFileTypeOverrides = includeExtrasOnly,
                    ExcludedFileTypeOverrides = excludeExtrasOnly
                };
            })
            .Where(project => project.Name.Length > 0)
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class ProjectLineCounterResultRow
    {
        public string FileType { get; init; } = string.Empty;
        public int FileCount { get; init; }
        public long LineCount { get; init; }
    }

    private sealed class ProjectLineCounterResult
    {
        public List<ProjectLineCounterResultRow> Rows { get; } = [];
        public List<string> NotCountedFiles { get; } = [];
        public int CountedFiles { get; set; }
        public long CountedLines { get; set; }
        public int NotCountedFileCount { get; set; }
    }

    private static bool TryMatchIgnoredFolderRule(string rootFolder, string directoryPath, IReadOnlyCollection<string> ignoredFolderRules, out string matchedRule)
    {
        matchedRule = string.Empty;
        if (ignoredFolderRules.Count == 0)
            return false;

        string normalizedRelativePath;
        try
        {
            normalizedRelativePath = Path.GetRelativePath(rootFolder, directoryPath).Replace('\\', '/').Trim('/').ToLowerInvariant();
        }
        catch
        {
            normalizedRelativePath = directoryPath.Replace('\\', '/').Trim('/').ToLowerInvariant();
        }

        var normalizedDirectoryName = Path.GetFileName(directoryPath).Trim().ToLowerInvariant();
        foreach (var rule in ignoredFolderRules)
        {
            if (string.IsNullOrWhiteSpace(rule))
                continue;

            var normalizedRule = rule.Replace('\\', '/').Trim('/').ToLowerInvariant();
            if (normalizedRule.Length == 0)
                continue;

            if (!normalizedRule.Contains('/'))
            {
                if (string.Equals(normalizedDirectoryName, normalizedRule, StringComparison.OrdinalIgnoreCase))
                {
                    matchedRule = rule;
                    return true;
                }
                continue;
            }

            if (string.Equals(normalizedRelativePath, normalizedRule, StringComparison.OrdinalIgnoreCase)
                || normalizedRelativePath.StartsWith(normalizedRule + "/", StringComparison.OrdinalIgnoreCase)
                || normalizedRelativePath.Contains("/" + normalizedRule + "/", StringComparison.OrdinalIgnoreCase))
            {
                matchedRule = rule;
                return true;
            }
        }

        return false;
    }

    private static long CountFileLinesSafe(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            long lines = 0;
            while (reader.ReadLine() != null)
                lines++;
            return lines;
        }
        catch
        {
            return 0;
        }
    }

    private static ProjectLineCounterResult CountProjectLines(
        string projectFolder,
        IReadOnlyCollection<string> selectedTypeFileTypes,
        IReadOnlyCollection<string> ignoredFileTypes,
        IReadOnlyCollection<string> ignoredFolders)
    {
        var result = new ProjectLineCounterResult();
        var typeSet = new HashSet<string>(selectedTypeFileTypes, StringComparer.OrdinalIgnoreCase);
        var ignoredSet = new HashSet<string>(ignoredFileTypes, StringComparer.OrdinalIgnoreCase);
        var ignoredFolderSet = new HashSet<string>(ignoredFolders, StringComparer.OrdinalIgnoreCase);
        var byFileType = new Dictionary<string, (int Files, long Lines)>(StringComparer.OrdinalIgnoreCase);

        var pending = new Stack<string>();
        pending.Push(projectFolder);
        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();
            try
            {
                foreach (var childDirectory in Directory.GetDirectories(currentDirectory))
                {
                    if (TryMatchIgnoredFolderRule(projectFolder, childDirectory, ignoredFolderSet, out _))
                    {
                        continue;
                    }

                    pending.Push(childDirectory);
                }
            }
            catch
            {
                // Ignore directories we cannot access.
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory);
            }
            catch
            {
                files = [];
            }

            foreach (var filePath in files)
            {
                var relativePath = filePath;
                try
                {
                    relativePath = Path.GetRelativePath(projectFolder, filePath);
                }
                catch
                {
                    // Keep absolute path if relative path fails.
                }

                if (string.Equals(Path.GetFileName(filePath), ".gitignore", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileType;
                var normalizedFileType = NormalizeProjectLineCounterFileType(Path.GetExtension(filePath));
                if (normalizedFileType == null)
                    fileType = "(no extension)";
                else
                    fileType = normalizedFileType;

                if (ignoredSet.Contains(fileType))
                {
                    continue;
                }

                if (!typeSet.Contains(fileType))
                {
                    result.NotCountedFileCount++;
                    result.NotCountedFiles.Add($"{relativePath} [{fileType}] - not in selected project type");
                    continue;
                }

                var lineCount = CountFileLinesSafe(filePath);
                if (byFileType.TryGetValue(fileType, out var aggregate))
                    byFileType[fileType] = (aggregate.Files + 1, aggregate.Lines + lineCount);
                else
                    byFileType[fileType] = (1, lineCount);
            }
        }

        foreach (var pair in byFileType.OrderByDescending(item => item.Value.Files).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Rows.Add(new ProjectLineCounterResultRow
            {
                FileType = pair.Key,
                FileCount = pair.Value.Files,
                LineCount = pair.Value.Lines
            });
            result.CountedFiles += pair.Value.Files;
            result.CountedLines += pair.Value.Lines;
        }

        result.NotCountedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private void ShowProjectLineCounterDialog()
    {
        var workingTypes = NormalizeProjectLineCounterTypes(_projectLineCounterTypes);
        var workingIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(_projectLineCounterIgnoredFileTypes);
        var workingProjects = NormalizeProjectLineCounterProjects(
            _projectLineCounterProjects,
            workingTypes,
            workingIgnoredFileTypes);
        var projectResultsByKey = new Dictionary<string, ProjectLineCounterResult>(StringComparer.OrdinalIgnoreCase);
        var selectedProjectIndex = -1;
        var selectedIncludeOverrideIndex = -1;
        var selectedExcludeOverrideIndex = -1;
        var isProjectSelectionUpdating = false;
        var isProjectTypeSelectionUpdating = false;

        var dlg = new Window
        {
            Title = "Project Line Counter",
            Width = 1080,
            Height = 740,
            MinWidth = 980,
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var root = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var btnOk = new Button { Content = "OK", Width = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var btnCancel = new Button { Content = "Cancel", Width = 85, IsCancel = true };
        footer.Children.Add(btnOk);
        footer.Children.Add(btnCancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var btnSettings = new Button
        {
            Content = "⚙",
            Width = 34,
            Height = 30,
            FontSize = 16,
            ToolTip = "Project Line Counter settings"
        };
        Grid.SetColumn(btnSettings, 1);
        header.Children.Add(btnSettings);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(body);
        dlg.Content = root;

        var projectPanel = new DockPanel();
        Grid.SetColumn(projectPanel, 0);
        body.Children.Add(projectPanel);
        var projectButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var btnAddProject = new Button { Content = "+", Width = 30, Height = 30, ToolTip = "Add project", Margin = new Thickness(0, 0, 6, 0) };
        var btnRemoveProject = new Button { Content = "-", Width = 30, Height = 30, ToolTip = "Remove project" };
        projectButtons.Children.Add(btnAddProject);
        projectButtons.Children.Add(btnRemoveProject);
        DockPanel.SetDock(projectButtons, Dock.Top);
        projectPanel.Children.Add(projectButtons);
        var projectList = new ListBox();
        projectPanel.Children.Add(projectList);

        var rightTabs = new TabControl();
        Grid.SetColumn(rightTabs, 2);
        body.Children.Add(rightTabs);

        var projectTabRoot = new DockPanel { Margin = new Thickness(10) };

        var projectForm = new Grid();
        for (var i = 0; i < 4; i++)
            projectForm.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        projectForm.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        projectForm.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        projectForm.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtProjectName = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        var txtProjectFolder = new TextBox { Margin = new Thickness(0, 0, 8, 8) };
        var btnBrowseFolder = new Button { Content = "Browse...", Width = 95, Margin = new Thickness(0, 0, 0, 8) };
        var cmbProjectType = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        var btnCountSelectedProject = new Button
        {
            Content = "Count Selected Project",
            Width = 180,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 8)
        };

        void AddProjectLabel(int row, string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 8, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            projectForm.Children.Add(label);
        }

        AddProjectLabel(0, "Project name");
        AddProjectLabel(1, "Folder");
        AddProjectLabel(2, "Project type");

        Grid.SetRow(txtProjectName, 0);
        Grid.SetColumn(txtProjectName, 1);
        Grid.SetColumnSpan(txtProjectName, 2);
        projectForm.Children.Add(txtProjectName);

        Grid.SetRow(txtProjectFolder, 1);
        Grid.SetColumn(txtProjectFolder, 1);
        projectForm.Children.Add(txtProjectFolder);
        Grid.SetRow(btnBrowseFolder, 1);
        Grid.SetColumn(btnBrowseFolder, 2);
        projectForm.Children.Add(btnBrowseFolder);

        Grid.SetRow(cmbProjectType, 2);
        Grid.SetColumn(cmbProjectType, 1);
        Grid.SetColumnSpan(cmbProjectType, 2);
        projectForm.Children.Add(cmbProjectType);

        Grid.SetRow(btnCountSelectedProject, 3);
        Grid.SetColumn(btnCountSelectedProject, 1);
        projectForm.Children.Add(btnCountSelectedProject);

        DockPanel.SetDock(projectForm, Dock.Top);
        projectTabRoot.Children.Add(projectForm);

        var overridesGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        overridesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overridesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        overridesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        DockPanel BuildProjectOverridePanel(
            string title,
            string hint,
            string addToolTip,
            out TextBox txtAdd,
            out Button btnAdd,
            out Button btnRemove,
            out ListBox listBox)
        {
            var panel = new DockPanel();
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = Brushes.DimGray,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            DockPanel.SetDock(headerStack, Dock.Top);
            panel.Children.Add(headerStack);

            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            txtAdd = new TextBox { Width = 120, Margin = new Thickness(0, 0, 6, 0), ToolTip = addToolTip };
            btnAdd = new Button { Content = "Add", Width = 60, Margin = new Thickness(0, 0, 6, 0) };
            btnRemove = new Button { Content = "Remove", Width = 70 };
            addRow.Children.Add(txtAdd);
            addRow.Children.Add(btnAdd);
            addRow.Children.Add(btnRemove);
            DockPanel.SetDock(addRow, Dock.Top);
            panel.Children.Add(addRow);

            listBox = new ListBox();
            panel.Children.Add(listBox);
            return panel;
        }

        var includePanel = BuildProjectOverridePanel(
            "Included file types override",
            "Optional extensions counted in addition to the project type's file types. Leave empty when you do not need project-only extras.",
            "Use format .cs or cs",
            out var txtAddIncludeOverride,
            out var btnAddIncludeOverride,
            out var btnRemoveIncludeOverride,
            out var listIncludeOverrides);
        Grid.SetColumn(includePanel, 0);
        overridesGrid.Children.Add(includePanel);

        var excludePanel = BuildProjectOverridePanel(
            "Excluded file types override",
            "Optional extensions skipped in addition to global Ignore Files and this type's ignored file types. Leave empty when you do not need project-only extras.",
            "Use format .dll or dll",
            out var txtAddExcludeOverride,
            out var btnAddExcludeOverride,
            out var btnRemoveExcludeOverride,
            out var listExcludeOverrides);
        Grid.SetColumn(excludePanel, 2);
        overridesGrid.Children.Add(excludePanel);

        projectTabRoot.Children.Add(overridesGrid);

        rightTabs.Items.Add(new TabItem
        {
            Header = "Project",
            Content = new ScrollViewer
            {
                Content = projectTabRoot,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        });

        var resultsRoot = new Grid { Margin = new Thickness(10) };
        resultsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resultsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        resultsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resultsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var resultsHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        resultsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resultsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtSummary = new TextBlock
        {
            Text = "Select a project and click 'Count Selected Project'.",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(txtSummary, 0);
        resultsHeaderRow.Children.Add(txtSummary);
        var btnRefreshResults = new Button
        {
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Recount selected project",
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new TextBlock
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Text = "\uE72C",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(btnRefreshResults, 1);
        resultsHeaderRow.Children.Add(btnRefreshResults);
        Grid.SetRow(resultsHeaderRow, 0);
        resultsRoot.Children.Add(resultsHeaderRow);

        var resultsList = new ListView();
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = "File type", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding("FileType") });
        gridView.Columns.Add(new GridViewColumn { Header = "Files", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("FileCount") });
        gridView.Columns.Add(new GridViewColumn { Header = "Lines", Width = 160, DisplayMemberBinding = new System.Windows.Data.Binding("LineCount") });
        resultsList.View = gridView;
        Grid.SetRow(resultsList, 1);
        resultsRoot.Children.Add(resultsList);

        var resultsTotalsBorder = new Border
        {
            Margin = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(0, 6, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Brushes.LightGray,
            Visibility = Visibility.Collapsed
        };
        var resultsTotalsGrid = new Grid();
        resultsTotalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        resultsTotalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        resultsTotalsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        var txtResultsTotalLabel = new TextBlock
        {
            Text = "Total",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var txtResultsTotalFiles = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 12, 0)
        };
        var txtResultsTotalLines = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(txtResultsTotalLabel, 0);
        Grid.SetColumn(txtResultsTotalFiles, 1);
        Grid.SetColumn(txtResultsTotalLines, 2);
        resultsTotalsGrid.Children.Add(txtResultsTotalLabel);
        resultsTotalsGrid.Children.Add(txtResultsTotalFiles);
        resultsTotalsGrid.Children.Add(txtResultsTotalLines);
        resultsTotalsBorder.Child = resultsTotalsGrid;
        Grid.SetRow(resultsTotalsBorder, 2);
        resultsRoot.Children.Add(resultsTotalsBorder);

        var unknownPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var unknownHeader = new TextBlock
        {
            Text = "Files not counted or ignored:",
            FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(unknownHeader, Dock.Top);
        unknownPanel.Children.Add(unknownHeader);
        var unknownList = new ListBox { Height = 160, Margin = new Thickness(0, 4, 0, 0) };
        unknownList.ContextMenu = new ContextMenu();
        unknownList.PreviewMouseRightButtonDown += (_, e) => SelectListBoxItemUnderMouse(unknownList, e);
        unknownPanel.Children.Add(unknownList);
        Grid.SetRow(unknownPanel, 3);
        resultsRoot.Children.Add(unknownPanel);

        rightTabs.Items.Add(new TabItem
        {
            Header = "Results",
            Content = resultsRoot
        });

        void RefreshProjectTypeSelector()
        {
            isProjectTypeSelectionUpdating = true;
            cmbProjectType.Items.Clear();
            foreach (var type in workingTypes)
                cmbProjectType.Items.Add(type.Name);
            isProjectTypeSelectionUpdating = false;
        }

        void RefreshProjectsList()
        {
            isProjectSelectionUpdating = true;
            projectList.Items.Clear();
            foreach (var project in workingProjects)
            {
                var folderName = project.FolderPath.Length == 0 ? "(no folder)" : project.FolderPath;
                var typeName = project.TypeName.Length == 0 ? "(no type)" : project.TypeName;
                projectList.Items.Add($"{project.Name} [{typeName}] - {folderName}");
            }

            var maxIndex = workingProjects.Count - 1;
            if (maxIndex < 0)
            {
                selectedProjectIndex = -1;
                projectList.SelectedIndex = -1;
            }
            else
            {
                selectedProjectIndex = Math.Max(0, Math.Min(selectedProjectIndex, maxIndex));
                projectList.SelectedIndex = selectedProjectIndex;
            }
            isProjectSelectionUpdating = false;
        }

        ProjectLineCounterType? FindType(string typeName) =>
            workingTypes.FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase));

        List<string> EffectiveIncludeFileTypes(ProjectLineCounterProject project)
        {
            var baseIncludes = NormalizeProjectLineCounterFileTypes(FindType(project.TypeName)?.FileTypes);
            var extras = NormalizeProjectLineCounterFileTypes(project.IncludedFileTypeOverrides);
            if (extras.Count == 0)
                return baseIncludes;
            return NormalizeProjectLineCounterFileTypes(baseIncludes.Concat(extras));
        }

        List<string> EffectiveExcludeFileTypes(ProjectLineCounterProject project)
        {
            var typeIgnores = FindType(project.TypeName)?.IgnoredFileTypes ?? [];
            var baseExcludes = NormalizeProjectLineCounterFileTypes(workingIgnoredFileTypes.Concat(typeIgnores));
            var extras = NormalizeProjectLineCounterFileTypes(project.ExcludedFileTypeOverrides);
            if (extras.Count == 0)
                return baseExcludes;
            return NormalizeProjectLineCounterFileTypes(baseExcludes.Concat(extras));
        }

        void StripRedundantIncludeExcludeExtras(ProjectLineCounterProject p)
        {
            var t = FindType(p.TypeName);
            var typeFileTypes = NormalizeProjectLineCounterFileTypes(t?.FileTypes);
            var typeIncludeSet = new HashSet<string>(typeFileTypes, StringComparer.OrdinalIgnoreCase);
            p.IncludedFileTypeOverrides = NormalizeProjectLineCounterFileTypes(
                (p.IncludedFileTypeOverrides ?? []).Where(ext => !typeIncludeSet.Contains(ext)));

            var typeIgnores = NormalizeProjectLineCounterFileTypes(t?.IgnoredFileTypes);
            var baseExcludeSet = new HashSet<string>(
                NormalizeProjectLineCounterFileTypes(workingIgnoredFileTypes.Concat(typeIgnores)),
                StringComparer.OrdinalIgnoreCase);
            p.ExcludedFileTypeOverrides = NormalizeProjectLineCounterFileTypes(
                (p.ExcludedFileTypeOverrides ?? []).Where(ext => !baseExcludeSet.Contains(ext)));
        }

        void RefreshIncludeOverrides()
        {
            listIncludeOverrides.Items.Clear();
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            var project = workingProjects[selectedProjectIndex];
            project.IncludedFileTypeOverrides ??= [];
            foreach (var fileType in project.IncludedFileTypeOverrides)
                listIncludeOverrides.Items.Add(fileType);
        }

        void RefreshExcludeOverrides()
        {
            listExcludeOverrides.Items.Clear();
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            var project = workingProjects[selectedProjectIndex];
            project.ExcludedFileTypeOverrides ??= [];
            foreach (var fileType in project.ExcludedFileTypeOverrides)
                listExcludeOverrides.Items.Add(fileType);
        }

        void RefreshProjectEditor()
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
            {
                txtProjectName.IsEnabled = false;
                txtProjectFolder.IsEnabled = false;
                btnBrowseFolder.IsEnabled = false;
                cmbProjectType.IsEnabled = false;
                btnCountSelectedProject.IsEnabled = false;
                btnRemoveProject.IsEnabled = false;
                txtAddIncludeOverride.IsEnabled = false;
                btnAddIncludeOverride.IsEnabled = false;
                btnRemoveIncludeOverride.IsEnabled = false;
                txtAddExcludeOverride.IsEnabled = false;
                btnAddExcludeOverride.IsEnabled = false;
                btnRemoveExcludeOverride.IsEnabled = false;
                txtProjectName.Text = string.Empty;
                txtProjectFolder.Text = string.Empty;
                cmbProjectType.SelectedIndex = -1;
                listIncludeOverrides.Items.Clear();
                listExcludeOverrides.Items.Clear();
                return;
            }

            var project = workingProjects[selectedProjectIndex];
            StripRedundantIncludeExcludeExtras(project);
            txtProjectName.IsEnabled = true;
            txtProjectFolder.IsEnabled = true;
            btnBrowseFolder.IsEnabled = true;
            btnSettings.IsEnabled = true;
            cmbProjectType.IsEnabled = workingTypes.Count > 0;
            btnCountSelectedProject.IsEnabled = true;
            btnRemoveProject.IsEnabled = true;
            txtAddIncludeOverride.IsEnabled = true;
            btnAddIncludeOverride.IsEnabled = true;
            btnRemoveIncludeOverride.IsEnabled = true;
            txtAddExcludeOverride.IsEnabled = true;
            btnAddExcludeOverride.IsEnabled = true;
            btnRemoveExcludeOverride.IsEnabled = true;
            txtProjectName.Text = project.Name;
            txtProjectFolder.Text = project.FolderPath;
            cmbProjectType.SelectedItem = project.TypeName;

            RefreshIncludeOverrides();
            RefreshExcludeOverrides();
        }

        void NormalizeProjectTypeAssignments()
        {
            var fallbackTypeName = workingTypes.FirstOrDefault()?.Name ?? string.Empty;
            var validTypeNames = new HashSet<string>(workingTypes.Select(type => type.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var project in workingProjects)
            {
                if (!validTypeNames.Contains(project.TypeName))
                    project.TypeName = fallbackTypeName;
            }
        }

        string BuildProjectResultKey(ProjectLineCounterProject project)
        {
            string normalizedFolderPath;
            try
            {
                normalizedFolderPath = Path.GetFullPath(project.FolderPath ?? string.Empty).Trim().ToLowerInvariant();
            }
            catch
            {
                normalizedFolderPath = (project.FolderPath ?? string.Empty).Trim().ToLowerInvariant();
            }

            var normalizedTypeName = (project.TypeName ?? string.Empty).Trim().ToLowerInvariant();
            var includeFingerprint = "i:" + string.Join(",", EffectiveIncludeFileTypes(project)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            var excludeFingerprint = "e:" + string.Join(",", EffectiveExcludeFileTypes(project)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

            return $"{normalizedFolderPath}|{normalizedTypeName}|{includeFingerprint}|{excludeFingerprint}";
        }

        void RenderResultForSelectedProject()
        {
            resultsList.Items.Clear();
            unknownList.Items.Clear();
            resultsTotalsBorder.Visibility = Visibility.Collapsed;

            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
            {
                txtSummary.Text = "Select a project and click 'Count Selected Project'.";
                return;
            }

            var selectedProject = workingProjects[selectedProjectIndex];
            var resultKey = BuildProjectResultKey(selectedProject);
            if (!projectResultsByKey.TryGetValue(resultKey, out var result))
            {
                txtSummary.Text = $"Project: {selectedProject.Name} | No result yet. Click 'Count Selected Project'.";
                return;
            }

            foreach (var row in result.Rows)
                resultsList.Items.Add(row);
            foreach (var filePath in result.NotCountedFiles)
                unknownList.Items.Add(filePath);
            txtSummary.Text =
                $"Project: {selectedProject.Name} | Counted files: {result.CountedFiles} | Counted lines: {result.CountedLines} | Not counted or ignored files: {result.NotCountedFileCount}";

            if (result.Rows.Count > 0)
            {
                txtResultsTotalFiles.Text = result.CountedFiles.ToString();
                txtResultsTotalLines.Text = result.CountedLines.ToString();
                resultsTotalsBorder.Visibility = Visibility.Visible;
            }
        }

        void PopulateNotCountedContextMenu()
        {
            var ctx = unknownList.ContextMenu;
            if (ctx == null)
                return;
            ctx.Items.Clear();

            if (unknownList.SelectedItem is not string line
                || !TryParseNotCountedDisplayLine(line, out var relativePath, out var bracketLabel))
            {
                ctx.Items.Add(new MenuItem { Header = "Select a file row", IsEnabled = false });
                return;
            }

            var hasProject = selectedProjectIndex >= 0 && selectedProjectIndex < workingProjects.Count;
            var csharpType = workingTypes.FirstOrDefault(t => string.Equals(t.Name, "C#", StringComparison.OrdinalIgnoreCase));
            var hasCSharp = csharpType != null;
            var hasFileExtension = !string.Equals(bracketLabel, "(no extension)", StringComparison.OrdinalIgnoreCase);
            var normalizedExt = hasFileExtension ? NormalizeProjectLineCounterFileType(bracketLabel) : null;
            if (normalizedExt == null)
                hasFileExtension = false;

            var rawDir = Path.GetDirectoryName(relativePath.Replace('\\', Path.DirectorySeparatorChar));
            var folderRule = string.IsNullOrWhiteSpace(rawDir) ? null : NormalizeProjectLineCounterFolderRule(rawDir);

            void AddItem(string header, string toolTip, bool enabled, RoutedEventHandler onClick)
            {
                var m = new MenuItem { Header = header, ToolTip = toolTip, IsEnabled = enabled };
                m.Click += onClick;
                ctx.Items.Add(m);
            }

            AddItem(
                "Add extension to this project's include overrides",
                "Adds one extension counted in addition to the project type's file types (ignored if it is already a type file type).",
                hasProject && hasFileExtension,
                (_, _) =>
                {
                    if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count || !hasFileExtension)
                        return;
                    var ext = NormalizeProjectLineCounterFileType(bracketLabel);
                    if (ext == null)
                        return;
                    var project = workingProjects[selectedProjectIndex];
                    project.IncludedFileTypeOverrides ??= [];
                    if (!project.IncludedFileTypeOverrides.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        project.IncludedFileTypeOverrides.Add(ext);
                    project.IncludedFileTypeOverrides = NormalizeProjectLineCounterFileTypes(project.IncludedFileTypeOverrides);
                    RecountAfterNotCountedMutation();
                });

            AddItem(
                "Add extension to this project's exclude overrides",
                "Adds one extension to the project-only list (global and type ignores still apply).",
                hasProject && hasFileExtension,
                (_, _) =>
                {
                    if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count || !hasFileExtension)
                        return;
                    var ext = NormalizeProjectLineCounterFileType(bracketLabel);
                    if (ext == null)
                        return;
                    var project = workingProjects[selectedProjectIndex];
                    project.ExcludedFileTypeOverrides ??= [];
                    if (!project.ExcludedFileTypeOverrides.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        project.ExcludedFileTypeOverrides.Add(ext);
                    project.ExcludedFileTypeOverrides = NormalizeProjectLineCounterFileTypes(project.ExcludedFileTypeOverrides);
                    RecountAfterNotCountedMutation();
                });

            ctx.Items.Add(new Separator());

            AddItem(
                "Add extension to C# counted file types",
                hasCSharp
                    ? "Counts files with this extension when the project type is C#."
                    : "Add a project type named \"C#\" in Settings first.",
                hasCSharp && hasFileExtension,
                (_, _) =>
                {
                    var ext = NormalizeProjectLineCounterFileType(bracketLabel);
                    if (csharpType == null || ext == null)
                        return;
                    csharpType.FileTypes ??= [];
                    if (!csharpType.FileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        csharpType.FileTypes.Add(ext);
                    csharpType.FileTypes = NormalizeProjectLineCounterFileTypes(csharpType.FileTypes);
                    RecountAfterNotCountedMutation();
                });

            AddItem(
                "Skip this extension for the C# type",
                hasCSharp
                    ? "Adds this extension to the C# type's ignored file types (merged with global ignore when counting C# projects)."
                    : "Add a project type named \"C#\" in Settings first.",
                hasCSharp && hasFileExtension,
                (_, _) =>
                {
                    var ext = NormalizeProjectLineCounterFileType(bracketLabel);
                    if (csharpType == null || ext == null)
                        return;
                    csharpType.IgnoredFileTypes ??= [];
                    if (!csharpType.IgnoredFileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        csharpType.IgnoredFileTypes.Add(ext);
                    csharpType.IgnoredFileTypes = NormalizeProjectLineCounterFileTypes(csharpType.IgnoredFileTypes);
                    RecountAfterNotCountedMutation();
                });

            AddItem(
                "Ignore this file's folder for the C# type",
                hasCSharp && folderRule != null
                    ? $"Adds \"{folderRule}\" to the C# type's ignored folders."
                    : hasCSharp
                        ? "This file is at the project root, so there is no subfolder to ignore."
                        : "Add a project type named \"C#\" in Settings first.",
                hasCSharp && folderRule != null,
                (_, _) =>
                {
                    if (csharpType == null || folderRule == null)
                        return;
                    csharpType.IgnoredFolders ??= [];
                    if (!csharpType.IgnoredFolders.Contains(folderRule, StringComparer.OrdinalIgnoreCase))
                        csharpType.IgnoredFolders.Add(folderRule);
                    csharpType.IgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(csharpType.IgnoredFolders);
                    RecountAfterNotCountedMutation();
                });

            ctx.Items.Add(new Separator());

            AddItem(
                "Always ignore this extension (global)",
                "Adds this extension to Settings → Ignore Files. Applies to all projects until you remove it.",
                hasFileExtension,
                (_, _) =>
                {
                    var ext = NormalizeProjectLineCounterFileType(bracketLabel);
                    if (ext == null)
                        return;
                    if (!workingIgnoredFileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        workingIgnoredFileTypes.Add(ext);
                    workingIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(workingIgnoredFileTypes);
                    RecountAfterNotCountedMutation();
                });
        }

        bool ApplyCountForSelectedProject()
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return false;

            var selectedProject = workingProjects[selectedProjectIndex];
            if (selectedProject.FolderPath.Length == 0)
            {
                MessageBox.Show(dlg, "Choose a folder for the project first.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            string fullFolderPath;
            try
            {
                fullFolderPath = Path.GetFullPath(selectedProject.FolderPath);
            }
            catch
            {
                MessageBox.Show(dlg, "The selected folder path is not valid.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(fullFolderPath))
            {
                MessageBox.Show(dlg, "The selected folder does not exist.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            selectedProject.FolderPath = fullFolderPath;
            RefreshProjectsList();
            projectList.SelectedIndex = selectedProjectIndex;

            var selectedType = FindType(selectedProject.TypeName);
            var typeIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(selectedType?.IgnoredFolders);
            var includes = EffectiveIncludeFileTypes(selectedProject);
            var excludes = EffectiveExcludeFileTypes(selectedProject);
            var result = CountProjectLines(fullFolderPath, includes, excludes, typeIgnoredFolders);
            var resultKey = BuildProjectResultKey(selectedProject);
            projectResultsByKey[resultKey] = result;
            RenderResultForSelectedProject();
            rightTabs.SelectedIndex = 1;
            return true;
        }

        void RecountAfterNotCountedMutation()
        {
            projectResultsByKey.Clear();
            RefreshProjectEditor();
            if (!ApplyCountForSelectedProject())
                RenderResultForSelectedProject();
        }

        void ShowProjectLineCounterSettingsDialog()
        {
            var settingsTypes = NormalizeProjectLineCounterTypes(workingTypes);
            var settingsIgnored = NormalizeProjectLineCounterIgnoredFileTypes(workingIgnoredFileTypes);
            var settingsSelectedTypeIndex = -1;
            var settingsSelectedTypeFileTypeIndex = -1;
            var settingsSelectedTypeIgnoredFolderIndex = -1;
            var settingsSelectedTypeIgnoredFileTypeIndex = -1;
            var settingsSelectedIgnoredFileTypeIndex = -1;
            var isTypeSelectionUpdating = false;

            var settingsDlg = new Window
            {
                Title = "Project Line Counter Settings",
                Width = 760,
                Height = 720,
                MinWidth = 680,
                MinHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = dlg
            };

            var settingsRoot = new DockPanel { Margin = new Thickness(12) };
            var settingsFooter = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var btnSettingsOk = new Button { Content = "OK", Width = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var btnSettingsCancel = new Button { Content = "Cancel", Width = 85, IsCancel = true };
            settingsFooter.Children.Add(btnSettingsOk);
            settingsFooter.Children.Add(btnSettingsCancel);
            DockPanel.SetDock(settingsFooter, Dock.Bottom);
            settingsRoot.Children.Add(settingsFooter);

            var settingsTabs = new TabControl();
            settingsRoot.Children.Add(settingsTabs);
            settingsDlg.Content = settingsRoot;

            var typesRoot = new Grid { Margin = new Thickness(10) };
            typesRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            typesRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            typesRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            typesRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            typesRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var typeHeaderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            typeHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            typeHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            typeHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            typeHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            typeHeaderRow.Children.Add(new TextBlock
            {
                Text = "Project types",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var cmbTypes = new ComboBox { Margin = new Thickness(8, 0, 8, 0), MinWidth = 220 };
            Grid.SetColumn(cmbTypes, 1);
            typeHeaderRow.Children.Add(cmbTypes);
            var btnAddType = new Button { Content = "+", Width = 30, Height = 30, ToolTip = "Add project type", Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(btnAddType, 2);
            typeHeaderRow.Children.Add(btnAddType);
            var btnRemoveType = new Button { Content = "-", Width = 30, Height = 30, ToolTip = "Remove selected type" };
            Grid.SetColumn(btnRemoveType, 3);
            typeHeaderRow.Children.Add(btnRemoveType);
            Grid.SetRow(typeHeaderRow, 0);
            typesRoot.Children.Add(typeHeaderRow);

            var typeNameRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            typeNameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            typeNameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            typeNameRow.Children.Add(new TextBlock
            {
                Text = "Type name",
                Width = 85,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var txtTypeName = new TextBox();
            Grid.SetColumn(txtTypeName, 1);
            typeNameRow.Children.Add(txtTypeName);
            Grid.SetRow(typeNameRow, 1);
            typesRoot.Children.Add(typeNameRow);

            var typeFileTypesPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var typeFileTypeButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            typeFileTypeButtons.Children.Add(new TextBlock
            {
                Text = "File types",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var txtAddTypeFileType = new TextBox { Width = 120, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use format .cs or cs" };
            var btnAddTypeFileType = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveTypeFileType = new Button { Content = "Remove", Width = 80 };
            typeFileTypeButtons.Children.Add(txtAddTypeFileType);
            typeFileTypeButtons.Children.Add(btnAddTypeFileType);
            typeFileTypeButtons.Children.Add(btnRemoveTypeFileType);
            DockPanel.SetDock(typeFileTypeButtons, Dock.Top);
            typeFileTypesPanel.Children.Add(typeFileTypeButtons);
            var listTypeFileTypes = new ListBox();
            typeFileTypesPanel.Children.Add(listTypeFileTypes);
            Grid.SetRow(typeFileTypesPanel, 2);
            typesRoot.Children.Add(typeFileTypesPanel);

            var typeIgnoredFileTypesPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var typeIgnoredFileTypeButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            typeIgnoredFileTypeButtons.Children.Add(new TextBlock
            {
                Text = "Ignored file types",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var txtAddTypeIgnoredFileType = new TextBox { Width = 120, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use format .map or map; merged with Ignore Files tab for this type" };
            var btnAddTypeIgnoredFileType = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveTypeIgnoredFileType = new Button { Content = "Remove", Width = 80 };
            typeIgnoredFileTypeButtons.Children.Add(txtAddTypeIgnoredFileType);
            typeIgnoredFileTypeButtons.Children.Add(btnAddTypeIgnoredFileType);
            typeIgnoredFileTypeButtons.Children.Add(btnRemoveTypeIgnoredFileType);
            DockPanel.SetDock(typeIgnoredFileTypeButtons, Dock.Top);
            typeIgnoredFileTypesPanel.Children.Add(typeIgnoredFileTypeButtons);
            var listTypeIgnoredFileTypes = new ListBox();
            typeIgnoredFileTypesPanel.Children.Add(listTypeIgnoredFileTypes);
            Grid.SetRow(typeIgnoredFileTypesPanel, 3);
            typesRoot.Children.Add(typeIgnoredFileTypesPanel);

            var typeIgnoredFoldersPanel = new DockPanel();
            var typeIgnoredFolderButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            typeIgnoredFolderButtons.Children.Add(new TextBlock
            {
                Text = "Ignored folders",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            var txtAddTypeIgnoredFolder = new TextBox { Width = 180, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use folder name or relative path, e.g. bin or build/output" };
            var btnAddTypeIgnoredFolder = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveTypeIgnoredFolder = new Button { Content = "Remove", Width = 80 };
            typeIgnoredFolderButtons.Children.Add(txtAddTypeIgnoredFolder);
            typeIgnoredFolderButtons.Children.Add(btnAddTypeIgnoredFolder);
            typeIgnoredFolderButtons.Children.Add(btnRemoveTypeIgnoredFolder);
            DockPanel.SetDock(typeIgnoredFolderButtons, Dock.Top);
            typeIgnoredFoldersPanel.Children.Add(typeIgnoredFolderButtons);
            var listTypeIgnoredFolders = new ListBox();
            typeIgnoredFoldersPanel.Children.Add(listTypeIgnoredFolders);
            Grid.SetRow(typeIgnoredFoldersPanel, 4);
            typesRoot.Children.Add(typeIgnoredFoldersPanel);

            settingsTabs.Items.Add(new TabItem { Header = "Types", Content = typesRoot });

            var ignoreFilesRoot = new DockPanel { Margin = new Thickness(10) };
            var ignoreFilesHeader = new TextBlock
            {
                Text = "Always ignored file types",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(ignoreFilesHeader, Dock.Top);
            ignoreFilesRoot.Children.Add(ignoreFilesHeader);
            var ignoredButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var txtAddIgnoredFileType = new TextBox { Width = 120, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use format .dll or dll" };
            var btnAddIgnoredFileType = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveIgnoredFileType = new Button { Content = "Remove", Width = 80 };
            ignoredButtons.Children.Add(txtAddIgnoredFileType);
            ignoredButtons.Children.Add(btnAddIgnoredFileType);
            ignoredButtons.Children.Add(btnRemoveIgnoredFileType);
            DockPanel.SetDock(ignoredButtons, Dock.Top);
            ignoreFilesRoot.Children.Add(ignoredButtons);
            var listIgnoredFileTypes = new ListBox();
            ignoreFilesRoot.Children.Add(listIgnoredFileTypes);
            settingsTabs.Items.Add(new TabItem { Header = "Ignore Files", Content = ignoreFilesRoot });

            void RefreshSettingsTypes()
            {
                isTypeSelectionUpdating = true;
                cmbTypes.Items.Clear();
                foreach (var type in settingsTypes)
                    cmbTypes.Items.Add(type.Name);

                if (settingsTypes.Count == 0)
                {
                    settingsSelectedTypeIndex = -1;
                    cmbTypes.SelectedIndex = -1;
                }
                else
                {
                    settingsSelectedTypeIndex = Math.Max(0, Math.Min(settingsSelectedTypeIndex, settingsTypes.Count - 1));
                    cmbTypes.SelectedIndex = settingsSelectedTypeIndex;
                }
                isTypeSelectionUpdating = false;
            }

            void RefreshSettingsTypeDetails()
            {
                listTypeFileTypes.Items.Clear();
                listTypeIgnoredFileTypes.Items.Clear();
                listTypeIgnoredFolders.Items.Clear();
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                {
                    txtTypeName.IsEnabled = false;
                    btnRemoveType.IsEnabled = false;
                    btnAddTypeFileType.IsEnabled = false;
                    btnRemoveTypeFileType.IsEnabled = false;
                    btnAddTypeIgnoredFileType.IsEnabled = false;
                    btnRemoveTypeIgnoredFileType.IsEnabled = false;
                    btnAddTypeIgnoredFolder.IsEnabled = false;
                    btnRemoveTypeIgnoredFolder.IsEnabled = false;
                    txtTypeName.Text = string.Empty;
                    return;
                }

                txtTypeName.IsEnabled = true;
                btnRemoveType.IsEnabled = settingsTypes.Count > 1;
                btnAddTypeFileType.IsEnabled = true;
                btnRemoveTypeFileType.IsEnabled = true;
                btnAddTypeIgnoredFileType.IsEnabled = true;
                btnRemoveTypeIgnoredFileType.IsEnabled = true;
                btnAddTypeIgnoredFolder.IsEnabled = true;
                btnRemoveTypeIgnoredFolder.IsEnabled = true;
                txtTypeName.Text = settingsTypes[settingsSelectedTypeIndex].Name;
                foreach (var fileType in settingsTypes[settingsSelectedTypeIndex].FileTypes ?? [])
                    listTypeFileTypes.Items.Add(fileType);
                foreach (var ignoredExt in settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes ?? [])
                    listTypeIgnoredFileTypes.Items.Add(ignoredExt);
                foreach (var folder in settingsTypes[settingsSelectedTypeIndex].IgnoredFolders ?? [])
                    listTypeIgnoredFolders.Items.Add(folder);
            }

            void RefreshSettingsIgnored()
            {
                listIgnoredFileTypes.Items.Clear();
                foreach (var fileType in settingsIgnored)
                    listIgnoredFileTypes.Items.Add(fileType);
            }

            cmbTypes.SelectionChanged += (_, _) =>
            {
                if (isTypeSelectionUpdating)
                    return;
                settingsSelectedTypeIndex = cmbTypes.SelectedIndex;
                settingsSelectedTypeFileTypeIndex = -1;
                settingsSelectedTypeIgnoredFileTypeIndex = -1;
                settingsSelectedTypeIgnoredFolderIndex = -1;
                RefreshSettingsTypeDetails();
            };

            txtTypeName.TextChanged += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;

                var previousName = settingsTypes[settingsSelectedTypeIndex].Name;
                var updatedName = txtTypeName.Text ?? string.Empty;
                if (string.Equals(previousName, updatedName, StringComparison.Ordinal))
                    return;
                settingsTypes[settingsSelectedTypeIndex].Name = updatedName;
                RefreshSettingsTypes();
                cmbTypes.SelectedIndex = settingsSelectedTypeIndex;
            };

            btnAddType.Click += (_, _) =>
            {
                settingsTypes.Add(new ProjectLineCounterType
                {
                    Name = "New Type",
                    FileTypes = [".txt"],
                    IgnoredFileTypes = [],
                    IgnoredFolders = []
                });
                settingsSelectedTypeIndex = settingsTypes.Count - 1;
                RefreshSettingsTypes();
                cmbTypes.SelectedIndex = settingsSelectedTypeIndex;
                RefreshSettingsTypeDetails();
                txtTypeName.Focus();
                txtTypeName.SelectAll();
            };

            btnRemoveType.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                if (settingsTypes.Count <= 1)
                {
                    MessageBox.Show(settingsDlg, "At least one project type is required.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                settingsTypes.RemoveAt(settingsSelectedTypeIndex);
                if (settingsSelectedTypeIndex >= settingsTypes.Count)
                    settingsSelectedTypeIndex = settingsTypes.Count - 1;
                RefreshSettingsTypes();
                cmbTypes.SelectedIndex = settingsSelectedTypeIndex;
                RefreshSettingsTypeDetails();
            };

            listTypeFileTypes.SelectionChanged += (_, _) => settingsSelectedTypeFileTypeIndex = listTypeFileTypes.SelectedIndex;
            btnAddTypeFileType.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                var normalized = NormalizeProjectLineCounterFileType(txtAddTypeFileType.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid file type, for example .cs.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                settingsTypes[settingsSelectedTypeIndex].FileTypes ??= [];
                if (!settingsTypes[settingsSelectedTypeIndex].FileTypes!.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsTypes[settingsSelectedTypeIndex].FileTypes!.Add(normalized);
                settingsTypes[settingsSelectedTypeIndex].FileTypes = settingsTypes[settingsSelectedTypeIndex].FileTypes!
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddTypeFileType.Text = string.Empty;
                RefreshSettingsTypeDetails();
            };

            btnRemoveTypeFileType.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                if (settingsSelectedTypeFileTypeIndex < 0 || settingsSelectedTypeFileTypeIndex >= listTypeFileTypes.Items.Count)
                    return;
                var selected = listTypeFileTypes.Items[settingsSelectedTypeFileTypeIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsTypes[settingsSelectedTypeIndex].FileTypes ??= [];
                settingsTypes[settingsSelectedTypeIndex].FileTypes!.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsTypeDetails();
            };

            listTypeIgnoredFileTypes.SelectionChanged += (_, _) => settingsSelectedTypeIgnoredFileTypeIndex = listTypeIgnoredFileTypes.SelectedIndex;
            btnAddTypeIgnoredFileType.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                var normalized = NormalizeProjectLineCounterFileType(txtAddTypeIgnoredFileType.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid file type, for example .map.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes ??= [];
                if (!settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes!.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes!.Add(normalized);
                settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes = settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes!
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddTypeIgnoredFileType.Text = string.Empty;
                RefreshSettingsTypeDetails();
            };

            btnRemoveTypeIgnoredFileType.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                if (settingsSelectedTypeIgnoredFileTypeIndex < 0 || settingsSelectedTypeIgnoredFileTypeIndex >= listTypeIgnoredFileTypes.Items.Count)
                    return;
                var selected = listTypeIgnoredFileTypes.Items[settingsSelectedTypeIgnoredFileTypeIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes ??= [];
                settingsTypes[settingsSelectedTypeIndex].IgnoredFileTypes!.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsTypeDetails();
            };

            listTypeIgnoredFolders.SelectionChanged += (_, _) => settingsSelectedTypeIgnoredFolderIndex = listTypeIgnoredFolders.SelectedIndex;
            btnAddTypeIgnoredFolder.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                var normalized = NormalizeProjectLineCounterFolderRule(txtAddTypeIgnoredFolder.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid folder rule, for example bin or src/generated.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                settingsTypes[settingsSelectedTypeIndex].IgnoredFolders ??= [];
                if (!settingsTypes[settingsSelectedTypeIndex].IgnoredFolders!.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsTypes[settingsSelectedTypeIndex].IgnoredFolders!.Add(normalized);
                settingsTypes[settingsSelectedTypeIndex].IgnoredFolders = settingsTypes[settingsSelectedTypeIndex].IgnoredFolders!
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddTypeIgnoredFolder.Text = string.Empty;
                RefreshSettingsTypeDetails();
            };

            btnRemoveTypeIgnoredFolder.Click += (_, _) =>
            {
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                    return;
                if (settingsSelectedTypeIgnoredFolderIndex < 0 || settingsSelectedTypeIgnoredFolderIndex >= listTypeIgnoredFolders.Items.Count)
                    return;
                var selected = listTypeIgnoredFolders.Items[settingsSelectedTypeIgnoredFolderIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsTypes[settingsSelectedTypeIndex].IgnoredFolders ??= [];
                settingsTypes[settingsSelectedTypeIndex].IgnoredFolders!.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsTypeDetails();
            };

            listIgnoredFileTypes.SelectionChanged += (_, _) => settingsSelectedIgnoredFileTypeIndex = listIgnoredFileTypes.SelectedIndex;
            btnAddIgnoredFileType.Click += (_, _) =>
            {
                var normalized = NormalizeProjectLineCounterFileType(txtAddIgnoredFileType.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid file type, for example .dll.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!settingsIgnored.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsIgnored.Add(normalized);
                settingsIgnored = settingsIgnored
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddIgnoredFileType.Text = string.Empty;
                RefreshSettingsIgnored();
            };

            btnRemoveIgnoredFileType.Click += (_, _) =>
            {
                if (settingsSelectedIgnoredFileTypeIndex < 0 || settingsSelectedIgnoredFileTypeIndex >= listIgnoredFileTypes.Items.Count)
                    return;
                var selected = listIgnoredFileTypes.Items[settingsSelectedIgnoredFileTypeIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsIgnored.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsIgnored();
            };

            btnSettingsOk.Click += (_, _) =>
            {
                var normalizedTypes = NormalizeProjectLineCounterTypes(settingsTypes);
                if (normalizedTypes.Any(type => string.IsNullOrWhiteSpace(type.Name)))
                {
                    MessageBox.Show(settingsDlg, "Each project type needs a name.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                workingTypes = normalizedTypes;
                workingIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(settingsIgnored);
                NormalizeProjectTypeAssignments();
                workingProjects = NormalizeProjectLineCounterProjects(
                    workingProjects,
                    workingTypes,
                    workingIgnoredFileTypes);
                RefreshProjectTypeSelector();
                RefreshProjectsList();
                RefreshProjectEditor();
                projectResultsByKey.Clear();
                RenderResultForSelectedProject();
                settingsDlg.DialogResult = true;
            };

            settingsDlg.Loaded += (_, _) =>
            {
                RefreshSettingsTypes();
                RefreshSettingsTypeDetails();
                RefreshSettingsIgnored();
            };

            settingsDlg.ShowDialog();
        }

        btnAddProject.Click += (_, _) =>
        {
            var typeName = workingTypes.FirstOrDefault()?.Name ?? string.Empty;
            workingProjects.Add(new ProjectLineCounterProject
            {
                Name = "New project",
                FolderPath = string.Empty,
                TypeName = typeName,
                IncludedFileTypeOverrides = [],
                ExcludedFileTypeOverrides = []
            });
            selectedProjectIndex = workingProjects.Count - 1;
            RefreshProjectsList();
            RefreshProjectEditor();
            RenderResultForSelectedProject();
            txtProjectName.Focus();
            txtProjectName.SelectAll();
        };

        btnRemoveProject.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            workingProjects.RemoveAt(selectedProjectIndex);
            if (selectedProjectIndex >= workingProjects.Count)
                selectedProjectIndex = workingProjects.Count - 1;
            RefreshProjectsList();
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        projectList.SelectionChanged += (_, _) =>
        {
            if (isProjectSelectionUpdating)
                return;
            selectedProjectIndex = projectList.SelectedIndex;
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        txtProjectName.TextChanged += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            workingProjects[selectedProjectIndex].Name = txtProjectName.Text ?? string.Empty;
            var keepFocus = txtProjectName.IsKeyboardFocusWithin;
            var caret = txtProjectName.CaretIndex;
            RefreshProjectsList();
            projectList.SelectedIndex = selectedProjectIndex;
            if (keepFocus)
            {
                txtProjectName.Focus();
                txtProjectName.CaretIndex = Math.Min(caret, (txtProjectName.Text ?? string.Empty).Length);
            }
            RenderResultForSelectedProject();
        };

        txtProjectFolder.TextChanged += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            workingProjects[selectedProjectIndex].FolderPath = txtProjectFolder.Text ?? string.Empty;
            RefreshProjectsList();
            projectList.SelectedIndex = selectedProjectIndex;
            RenderResultForSelectedProject();
        };

        cmbProjectType.SelectionChanged += (_, _) =>
        {
            if (isProjectTypeSelectionUpdating)
                return;
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            if (cmbProjectType.SelectedItem is string selectedTypeName)
            {
                workingProjects[selectedProjectIndex].TypeName = selectedTypeName;
                RefreshProjectsList();
                projectList.SelectedIndex = selectedProjectIndex;
                RefreshProjectEditor();
                RenderResultForSelectedProject();
            }
        };

        btnBrowseFolder.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;

            var picker = new VistaFolderBrowserDialog
            {
                Description = "Select project folder",
                UseDescriptionForTitle = true
            };

            var rawPath = (txtProjectFolder.Text ?? string.Empty).Trim();
            try
            {
                if (rawPath.Length > 0)
                {
                    var fullPath = Path.GetFullPath(rawPath);
                    if (Directory.Exists(fullPath))
                        picker.SelectedPath = fullPath;
                    else
                    {
                        var parent = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                            picker.SelectedPath = parent;
                    }
                }
            }
            catch
            {
                // Ignore invalid pre-filled path.
            }

            if (picker.ShowDialog(dlg) == true)
                txtProjectFolder.Text = picker.SelectedPath;
        };

        listIncludeOverrides.SelectionChanged += (_, _) => selectedIncludeOverrideIndex = listIncludeOverrides.SelectedIndex;
        btnAddIncludeOverride.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            var normalized = NormalizeProjectLineCounterFileType(txtAddIncludeOverride.Text);
            if (normalized == null)
            {
                MessageBox.Show(dlg, "Enter a valid file type, for example .cs.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var project = workingProjects[selectedProjectIndex];
            project.IncludedFileTypeOverrides ??= [];
            if (!project.IncludedFileTypeOverrides.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                project.IncludedFileTypeOverrides.Add(normalized);
            project.IncludedFileTypeOverrides = project.IncludedFileTypeOverrides
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            txtAddIncludeOverride.Text = string.Empty;
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        btnRemoveIncludeOverride.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            if (selectedIncludeOverrideIndex < 0 || selectedIncludeOverrideIndex >= listIncludeOverrides.Items.Count)
                return;
            var selected = listIncludeOverrides.Items[selectedIncludeOverrideIndex]?.ToString();
            if (string.IsNullOrWhiteSpace(selected))
                return;
            var project = workingProjects[selectedProjectIndex];
            project.IncludedFileTypeOverrides ??= [];
            project.IncludedFileTypeOverrides.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        listExcludeOverrides.SelectionChanged += (_, _) => selectedExcludeOverrideIndex = listExcludeOverrides.SelectedIndex;
        btnAddExcludeOverride.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            var normalized = NormalizeProjectLineCounterFileType(txtAddExcludeOverride.Text);
            if (normalized == null)
            {
                MessageBox.Show(dlg, "Enter a valid file type, for example .dll.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var project = workingProjects[selectedProjectIndex];
            project.ExcludedFileTypeOverrides ??= [];
            if (!project.ExcludedFileTypeOverrides.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                project.ExcludedFileTypeOverrides.Add(normalized);
            project.ExcludedFileTypeOverrides = project.ExcludedFileTypeOverrides
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            txtAddExcludeOverride.Text = string.Empty;
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        btnRemoveExcludeOverride.Click += (_, _) =>
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;
            if (selectedExcludeOverrideIndex < 0 || selectedExcludeOverrideIndex >= listExcludeOverrides.Items.Count)
                return;
            var selected = listExcludeOverrides.Items[selectedExcludeOverrideIndex]?.ToString();
            if (string.IsNullOrWhiteSpace(selected))
                return;
            var project = workingProjects[selectedProjectIndex];
            project.ExcludedFileTypeOverrides ??= [];
            project.ExcludedFileTypeOverrides.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
            RefreshProjectEditor();
            RenderResultForSelectedProject();
        };

        btnCountSelectedProject.Click += (_, _) => ApplyCountForSelectedProject();
        btnRefreshResults.Click += (_, _) => ApplyCountForSelectedProject();
        btnSettings.Click += (_, _) => ShowProjectLineCounterSettingsDialog();

        btnOk.Click += (_, _) =>
        {
            var normalizedTypes = NormalizeProjectLineCounterTypes(workingTypes);
            var normalizedIgnored = NormalizeProjectLineCounterIgnoredFileTypes(workingIgnoredFileTypes);
            var normalizedProjects = NormalizeProjectLineCounterProjects(
                workingProjects,
                normalizedTypes,
                normalizedIgnored);

            if (normalizedProjects.Any(project => string.IsNullOrWhiteSpace(project.Name)))
            {
                MessageBox.Show(dlg, "Each project needs a name.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (normalizedTypes.Any(type => string.IsNullOrWhiteSpace(type.Name)))
            {
                MessageBox.Show(dlg, "Each project type needs a name.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _projectLineCounterTypes = normalizedTypes;
            _projectLineCounterProjects = normalizedProjects;
            _projectLineCounterIgnoredFileTypes = normalizedIgnored;
            SaveWindowSettings();
            dlg.DialogResult = true;
        };

        dlg.Loaded += (_, _) =>
        {
            unknownList.ContextMenuOpening += (_, _) => PopulateNotCountedContextMenu();
            RefreshProjectTypeSelector();
            RefreshProjectsList();
            RefreshProjectEditor();
            RenderResultForSelectedProject();

            if (workingProjects.Count == 0)
                btnAddProject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        dlg.ShowDialog();
    }
}
