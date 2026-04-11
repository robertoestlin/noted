using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Noted.Models;
using Ookii.Dialogs.Wpf;

namespace Noted;

public partial class MainWindow
{
    private static readonly string[] DefaultProjectLineCounterAutoDetectedFileTypes =
    [
        ".cs", ".xaml", ".xml", ".json", ".md", ".txt", ".js", ".ts", ".tsx", ".jsx",
        ".css", ".scss", ".html", ".sql", ".ps1", ".py", ".java", ".go", ".rs", ".yml", ".yaml",
        ".config", ".sh"
    ];
    private static readonly string[] DefaultProjectLineCounterIgnoredFileTypes =
    [
        ".exe", ".dll", ".pdb", ".obj", ".bin", ".cache", ".class", ".jar",
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".zip", ".7z", ".pdf", ".gitignore", ".md"
    ];
    private static readonly string[] DefaultProjectLineCounterIgnoredFolders =
    [
        ".git", "bin", "obj"
    ];

    private List<ProjectLineCounterProject> BuildProjectLineCounterProjectsSnapshot()
        => NormalizeProjectLineCounterProjects(_projectLineCounterProjects, _projectLineCounterTypes);

    private List<ProjectLineCounterType> BuildProjectLineCounterTypesSnapshot()
        => NormalizeProjectLineCounterTypes(_projectLineCounterTypes);

    private List<string> BuildProjectLineCounterAutoDetectedFileTypesSnapshot()
        => NormalizeProjectLineCounterAutoDetectedFileTypes(_projectLineCounterAutoDetectedFileTypes);

    private List<string> BuildProjectLineCounterIgnoredFileTypesSnapshot()
        => NormalizeProjectLineCounterIgnoredFileTypes(_projectLineCounterIgnoredFileTypes);
    private List<string> BuildProjectLineCounterIgnoredFoldersSnapshot()
        => NormalizeProjectLineCounterIgnoredFolders(_projectLineCounterIgnoredFolders);

    private void ApplyProjectLineCounterSettings(
        IEnumerable<ProjectLineCounterProject>? projects,
        IEnumerable<ProjectLineCounterType>? types,
        IEnumerable<string>? autoDetectedFileTypes,
        IEnumerable<string>? ignoredFileTypes,
        IEnumerable<string>? ignoredFolders)
    {
        _projectLineCounterTypes = NormalizeProjectLineCounterTypes(types);
        _projectLineCounterAutoDetectedFileTypes = NormalizeProjectLineCounterAutoDetectedFileTypes(autoDetectedFileTypes);
        _projectLineCounterIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(ignoredFileTypes);
        _projectLineCounterIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(ignoredFolders);
        _projectLineCounterProjects = NormalizeProjectLineCounterProjects(projects, _projectLineCounterTypes);
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

    private static List<string> NormalizeProjectLineCounterAutoDetectedFileTypes(IEnumerable<string>? fileTypes)
    {
        var normalized = NormalizeProjectLineCounterFileTypes(fileTypes);
        if (normalized.Count == 0)
        {
            normalized = DefaultProjectLineCounterAutoDetectedFileTypes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        foreach (var required in new[] { ".config", ".sh" })
        {
            if (!normalized.Contains(required, StringComparer.OrdinalIgnoreCase))
                normalized.Add(required);
        }

        return normalized
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
        var normalized = (ignoredFolders ?? [])
            .Select(NormalizeProjectLineCounterFolderRule)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized = DefaultProjectLineCounterIgnoredFolders
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!normalized.Contains(".git", StringComparer.OrdinalIgnoreCase))
        {
            normalized.Add(".git");
            normalized = normalized
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }

    private static List<ProjectLineCounterType> BuildDefaultProjectLineCounterTypes()
        =>
        [
            new ProjectLineCounterType { Name = "C#", FileTypes = [".cs", ".csproj", ".sln", ".xaml", ".xml", ".json", ".config", ".sh"] },
            new ProjectLineCounterType { Name = "Web", FileTypes = [".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".scss", ".json"] }
        ];

    private static List<ProjectLineCounterType> NormalizeProjectLineCounterTypes(IEnumerable<ProjectLineCounterType>? types)
    {
        var normalized = (types ?? BuildDefaultProjectLineCounterTypes())
            .Select(type => new ProjectLineCounterType
            {
                Name = (type?.Name ?? string.Empty).Trim(),
                FileTypes = NormalizeProjectLineCounterFileTypes(type?.FileTypes)
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
            foreach (var required in new[] { ".config", ".sh" })
            {
                if (!csharpType.FileTypes.Contains(required, StringComparer.OrdinalIgnoreCase))
                    csharpType.FileTypes.Add(required);
            }

            csharpType.FileTypes = csharpType.FileTypes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }

    private static List<ProjectLineCounterProject> NormalizeProjectLineCounterProjects(
        IEnumerable<ProjectLineCounterProject>? projects,
        IReadOnlyCollection<ProjectLineCounterType> knownTypes)
    {
        var defaultTypeName = knownTypes.FirstOrDefault()?.Name ?? string.Empty;
        var knownTypeNames = new HashSet<string>(knownTypes.Select(type => type.Name), StringComparer.OrdinalIgnoreCase);

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

                return new ProjectLineCounterProject
                {
                    Name = trimmedName,
                    FolderPath = folderPath,
                    TypeName = typeName
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
        public string Source { get; init; } = string.Empty;
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
        IReadOnlyCollection<string> autoDetectedFileTypes,
        IReadOnlyCollection<string> ignoredFileTypes,
        IReadOnlyCollection<string> ignoredFolders)
    {
        var result = new ProjectLineCounterResult();
        var typeSet = new HashSet<string>(selectedTypeFileTypes, StringComparer.OrdinalIgnoreCase);
        var autoSet = new HashSet<string>(autoDetectedFileTypes, StringComparer.OrdinalIgnoreCase);
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
            var inAuto = autoSet.Contains(pair.Key);
            var source = inAuto ? "Type + Auto" : "Type";

            result.Rows.Add(new ProjectLineCounterResultRow
            {
                FileType = pair.Key,
                FileCount = pair.Value.Files,
                LineCount = pair.Value.Lines,
                Source = source
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
        var workingProjects = NormalizeProjectLineCounterProjects(_projectLineCounterProjects, workingTypes);
        var workingAutoDetectedFileTypes = NormalizeProjectLineCounterAutoDetectedFileTypes(_projectLineCounterAutoDetectedFileTypes);
        var workingIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(_projectLineCounterIgnoredFileTypes);
        var workingIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(_projectLineCounterIgnoredFolders);
        var projectResultsByKey = new Dictionary<string, ProjectLineCounterResult>(StringComparer.OrdinalIgnoreCase);
        var selectedProjectIndex = -1;
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

        var projectTabGrid = new Grid { Margin = new Thickness(10) };
        for (var i = 0; i < 8; i++)
            projectTabGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        projectTabGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        projectTabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        projectTabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        projectTabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txtProjectName = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        var txtProjectFolder = new TextBox { Margin = new Thickness(0, 0, 8, 8) };
        var btnBrowseFolder = new Button { Content = "Browse...", Width = 95, Margin = new Thickness(0, 0, 0, 8) };
        var cmbProjectType = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        var btnCountSelectedProject = new Button
        {
            Content = "Count Selected Project",
            Width = 180,
            Height = 32,
            Margin = new Thickness(0, 6, 0, 8)
        };
        var lblSelectedTypeInfo = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
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
            projectTabGrid.Children.Add(label);
        }

        AddProjectLabel(0, "Project name");
        AddProjectLabel(1, "Folder");
        AddProjectLabel(2, "Project type");

        Grid.SetRow(txtProjectName, 0);
        Grid.SetColumn(txtProjectName, 1);
        Grid.SetColumnSpan(txtProjectName, 2);
        projectTabGrid.Children.Add(txtProjectName);

        Grid.SetRow(txtProjectFolder, 1);
        Grid.SetColumn(txtProjectFolder, 1);
        projectTabGrid.Children.Add(txtProjectFolder);
        Grid.SetRow(btnBrowseFolder, 1);
        Grid.SetColumn(btnBrowseFolder, 2);
        projectTabGrid.Children.Add(btnBrowseFolder);

        Grid.SetRow(cmbProjectType, 2);
        Grid.SetColumn(cmbProjectType, 1);
        Grid.SetColumnSpan(cmbProjectType, 2);
        projectTabGrid.Children.Add(cmbProjectType);

        Grid.SetRow(btnCountSelectedProject, 3);
        Grid.SetColumn(btnCountSelectedProject, 1);
        projectTabGrid.Children.Add(btnCountSelectedProject);

        Grid.SetRow(lblSelectedTypeInfo, 4);
        Grid.SetColumn(lblSelectedTypeInfo, 1);
        Grid.SetColumnSpan(lblSelectedTypeInfo, 2);
        projectTabGrid.Children.Add(lblSelectedTypeInfo);

        rightTabs.Items.Add(new TabItem
        {
            Header = "Project",
            Content = new ScrollViewer
            {
                Content = projectTabGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        });

        var resultsRoot = new DockPanel { Margin = new Thickness(10) };
        var txtSummary = new TextBlock
        {
            Text = "Select a project and click 'Count Selected Project'.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(txtSummary, Dock.Top);
        resultsRoot.Children.Add(txtSummary);

        var resultsList = new ListView();
        var gridView = new GridView();
        gridView.Columns.Add(new GridViewColumn { Header = "File type", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding("FileType") });
        gridView.Columns.Add(new GridViewColumn { Header = "Files", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding("FileCount") });
        gridView.Columns.Add(new GridViewColumn { Header = "Lines", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("LineCount") });
        gridView.Columns.Add(new GridViewColumn { Header = "Source", Width = 140, DisplayMemberBinding = new System.Windows.Data.Binding("Source") });
        resultsList.View = gridView;
        resultsRoot.Children.Add(resultsList);

        var unknownPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        unknownPanel.Children.Add(new TextBlock
        {
            Text = "Files not counted:",
            FontWeight = FontWeights.SemiBold
        });
        var unknownList = new ListBox { Height = 160, Margin = new Thickness(0, 4, 0, 0) };
        unknownPanel.Children.Add(unknownList);
        DockPanel.SetDock(unknownPanel, Dock.Bottom);
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
                txtProjectName.Text = string.Empty;
                txtProjectFolder.Text = string.Empty;
                cmbProjectType.SelectedIndex = -1;
                lblSelectedTypeInfo.Text = "Select a project.";
                return;
            }

            var project = workingProjects[selectedProjectIndex];
            txtProjectName.IsEnabled = true;
            txtProjectFolder.IsEnabled = true;
            btnBrowseFolder.IsEnabled = true;
            btnSettings.IsEnabled = true;
            cmbProjectType.IsEnabled = workingTypes.Count > 0;
            btnCountSelectedProject.IsEnabled = true;
            btnRemoveProject.IsEnabled = true;
            txtProjectName.Text = project.Name;
            txtProjectFolder.Text = project.FolderPath;
            cmbProjectType.SelectedItem = project.TypeName;

            var selectedType = workingTypes.FirstOrDefault(type => string.Equals(type.Name, project.TypeName, StringComparison.OrdinalIgnoreCase));
            var selectedTypeFileTypes = selectedType?.FileTypes ?? [];
            lblSelectedTypeInfo.Text = selectedTypeFileTypes.Count == 0
                ? "Selected type currently has no file type filters."
                : $"Selected type includes: {string.Join(", ", selectedTypeFileTypes)}";
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
            return $"{normalizedFolderPath}|{normalizedTypeName}";
        }

        void RenderResultForSelectedProject()
        {
            resultsList.Items.Clear();
            unknownList.Items.Clear();

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
                $"Project: {selectedProject.Name} | Counted files: {result.CountedFiles} | Counted lines: {result.CountedLines} | Not counted files: {result.NotCountedFileCount}";
        }

        void ApplyCountForSelectedProject()
        {
            if (selectedProjectIndex < 0 || selectedProjectIndex >= workingProjects.Count)
                return;

            var selectedProject = workingProjects[selectedProjectIndex];
            if (selectedProject.FolderPath.Length == 0)
            {
                MessageBox.Show(dlg, "Choose a folder for the project first.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string fullFolderPath;
            try
            {
                fullFolderPath = Path.GetFullPath(selectedProject.FolderPath);
            }
            catch
            {
                MessageBox.Show(dlg, "The selected folder path is not valid.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(fullFolderPath))
            {
                MessageBox.Show(dlg, "The selected folder does not exist.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            selectedProject.FolderPath = fullFolderPath;
            RefreshProjectsList();
            projectList.SelectedIndex = selectedProjectIndex;

            var selectedType = workingTypes.FirstOrDefault(type => string.Equals(type.Name, selectedProject.TypeName, StringComparison.OrdinalIgnoreCase));
            var typeFileTypes = selectedType?.FileTypes ?? [];
            var result = CountProjectLines(fullFolderPath, typeFileTypes, workingAutoDetectedFileTypes, workingIgnoredFileTypes, workingIgnoredFolders);
            var resultKey = BuildProjectResultKey(selectedProject);
            projectResultsByKey[resultKey] = result;
            RenderResultForSelectedProject();
            rightTabs.SelectedIndex = 1;
        }

        void ShowProjectLineCounterSettingsDialog()
        {
            var settingsTypes = NormalizeProjectLineCounterTypes(workingTypes);
            var settingsAutoDetected = NormalizeProjectLineCounterAutoDetectedFileTypes(workingAutoDetectedFileTypes);
            var settingsIgnored = NormalizeProjectLineCounterIgnoredFileTypes(workingIgnoredFileTypes);
            var settingsIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(workingIgnoredFolders);
            var settingsSelectedTypeIndex = -1;
            var settingsSelectedTypeFileTypeIndex = -1;
            var settingsSelectedAutoFileTypeIndex = -1;
            var settingsSelectedIgnoredFileTypeIndex = -1;
            var settingsSelectedIgnoredFolderIndex = -1;
            var isTypeSelectionUpdating = false;

            var settingsDlg = new Window
            {
                Title = "Project Line Counter Settings",
                Width = 760,
                Height = 620,
                MinWidth = 680,
                MinHeight = 560,
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

            var typesRoot = new DockPanel { Margin = new Thickness(10) };
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
            DockPanel.SetDock(typeHeaderRow, Dock.Top);
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
            DockPanel.SetDock(typeNameRow, Dock.Top);
            typesRoot.Children.Add(typeNameRow);

            var typeFileTypeButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            typeFileTypeButtons.Children.Add(new TextBlock
            {
                Text = "File types for selected type",
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
            typesRoot.Children.Add(typeFileTypeButtons);

            var listTypeFileTypes = new ListBox();
            typesRoot.Children.Add(listTypeFileTypes);
            settingsTabs.Items.Add(new TabItem { Header = "Types", Content = typesRoot });

            var autoRoot = new DockPanel { Margin = new Thickness(10) };
            autoRoot.Children.Add(new TextBlock
            {
                Text = "Auto-detected file types",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            DockPanel.SetDock(autoRoot.Children[^1], Dock.Top);
            var autoButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var txtAddAutoFileType = new TextBox { Width = 120, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use format .json or json" };
            var btnAddAutoFileType = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveAutoFileType = new Button { Content = "Remove", Width = 80 };
            autoButtons.Children.Add(txtAddAutoFileType);
            autoButtons.Children.Add(btnAddAutoFileType);
            autoButtons.Children.Add(btnRemoveAutoFileType);
            DockPanel.SetDock(autoButtons, Dock.Top);
            autoRoot.Children.Add(autoButtons);
            var listAutoFileTypes = new ListBox();
            autoRoot.Children.Add(listAutoFileTypes);
            settingsTabs.Items.Add(new TabItem { Header = "Auto Detect", Content = autoRoot });

            var ignoreFilesRoot = new DockPanel { Margin = new Thickness(10) };
            ignoreFilesRoot.Children.Add(new TextBlock
            {
                Text = "Always ignored file types",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            DockPanel.SetDock(ignoreFilesRoot.Children[^1], Dock.Top);
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

            var ignoreFoldersRoot = new DockPanel { Margin = new Thickness(10) };
            ignoreFoldersRoot.Children.Add(new TextBlock
            {
                Text = "Ignored folders",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            DockPanel.SetDock(ignoreFoldersRoot.Children[^1], Dock.Top);
            var ignoredFolderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var txtAddIgnoredFolder = new TextBox { Width = 180, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Use folder name or relative path, e.g. bin or build/output" };
            var btnAddIgnoredFolder = new Button { Content = "Add", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var btnRemoveIgnoredFolder = new Button { Content = "Remove", Width = 80 };
            ignoredFolderButtons.Children.Add(txtAddIgnoredFolder);
            ignoredFolderButtons.Children.Add(btnAddIgnoredFolder);
            ignoredFolderButtons.Children.Add(btnRemoveIgnoredFolder);
            DockPanel.SetDock(ignoredFolderButtons, Dock.Top);
            ignoreFoldersRoot.Children.Add(ignoredFolderButtons);
            var listIgnoredFolders = new ListBox();
            ignoreFoldersRoot.Children.Add(listIgnoredFolders);
            settingsTabs.Items.Add(new TabItem { Header = "Ignore Folders", Content = ignoreFoldersRoot });

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

            void RefreshSettingsTypeFileTypes()
            {
                listTypeFileTypes.Items.Clear();
                if (settingsSelectedTypeIndex < 0 || settingsSelectedTypeIndex >= settingsTypes.Count)
                {
                    txtTypeName.IsEnabled = false;
                    btnRemoveType.IsEnabled = false;
                    btnAddTypeFileType.IsEnabled = false;
                    btnRemoveTypeFileType.IsEnabled = false;
                    txtTypeName.Text = string.Empty;
                    return;
                }

                txtTypeName.IsEnabled = true;
                btnRemoveType.IsEnabled = settingsTypes.Count > 1;
                btnAddTypeFileType.IsEnabled = true;
                btnRemoveTypeFileType.IsEnabled = true;
                txtTypeName.Text = settingsTypes[settingsSelectedTypeIndex].Name;
                foreach (var fileType in settingsTypes[settingsSelectedTypeIndex].FileTypes ?? [])
                    listTypeFileTypes.Items.Add(fileType);
            }

            void RefreshSettingsAutoDetected()
            {
                listAutoFileTypes.Items.Clear();
                foreach (var fileType in settingsAutoDetected)
                    listAutoFileTypes.Items.Add(fileType);
            }

            void RefreshSettingsIgnored()
            {
                listIgnoredFileTypes.Items.Clear();
                foreach (var fileType in settingsIgnored)
                    listIgnoredFileTypes.Items.Add(fileType);
            }

            void RefreshSettingsIgnoredFolders()
            {
                listIgnoredFolders.Items.Clear();
                foreach (var folderRule in settingsIgnoredFolders)
                    listIgnoredFolders.Items.Add(folderRule);
            }

            cmbTypes.SelectionChanged += (_, _) =>
            {
                if (isTypeSelectionUpdating)
                    return;
                settingsSelectedTypeIndex = cmbTypes.SelectedIndex;
                settingsSelectedTypeFileTypeIndex = -1;
                RefreshSettingsTypeFileTypes();
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
                settingsTypes.Add(new ProjectLineCounterType { Name = "New Type", FileTypes = [".txt"] });
                settingsSelectedTypeIndex = settingsTypes.Count - 1;
                RefreshSettingsTypes();
                cmbTypes.SelectedIndex = settingsSelectedTypeIndex;
                RefreshSettingsTypeFileTypes();
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
                RefreshSettingsTypeFileTypes();
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
                RefreshSettingsTypeFileTypes();
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
                RefreshSettingsTypeFileTypes();
            };

            listAutoFileTypes.SelectionChanged += (_, _) => settingsSelectedAutoFileTypeIndex = listAutoFileTypes.SelectedIndex;
            btnAddAutoFileType.Click += (_, _) =>
            {
                var normalized = NormalizeProjectLineCounterFileType(txtAddAutoFileType.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid file type, for example .json.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!settingsAutoDetected.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsAutoDetected.Add(normalized);
                settingsAutoDetected = settingsAutoDetected
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddAutoFileType.Text = string.Empty;
                RefreshSettingsAutoDetected();
            };

            btnRemoveAutoFileType.Click += (_, _) =>
            {
                if (settingsSelectedAutoFileTypeIndex < 0 || settingsSelectedAutoFileTypeIndex >= listAutoFileTypes.Items.Count)
                    return;
                var selected = listAutoFileTypes.Items[settingsSelectedAutoFileTypeIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsAutoDetected.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsAutoDetected();
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

            listIgnoredFolders.SelectionChanged += (_, _) => settingsSelectedIgnoredFolderIndex = listIgnoredFolders.SelectedIndex;
            btnAddIgnoredFolder.Click += (_, _) =>
            {
                var normalized = NormalizeProjectLineCounterFolderRule(txtAddIgnoredFolder.Text);
                if (normalized == null)
                {
                    MessageBox.Show(settingsDlg, "Enter a valid folder rule, for example bin or src/generated.", "Project Line Counter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!settingsIgnoredFolders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    settingsIgnoredFolders.Add(normalized);
                settingsIgnoredFolders = settingsIgnoredFolders
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                txtAddIgnoredFolder.Text = string.Empty;
                RefreshSettingsIgnoredFolders();
            };

            btnRemoveIgnoredFolder.Click += (_, _) =>
            {
                if (settingsSelectedIgnoredFolderIndex < 0 || settingsSelectedIgnoredFolderIndex >= listIgnoredFolders.Items.Count)
                    return;
                var selected = listIgnoredFolders.Items[settingsSelectedIgnoredFolderIndex]?.ToString();
                if (string.IsNullOrWhiteSpace(selected))
                    return;
                settingsIgnoredFolders.RemoveAll(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase));
                RefreshSettingsIgnoredFolders();
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
                workingAutoDetectedFileTypes = NormalizeProjectLineCounterAutoDetectedFileTypes(settingsAutoDetected);
                workingIgnoredFileTypes = NormalizeProjectLineCounterIgnoredFileTypes(settingsIgnored);
                workingIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(settingsIgnoredFolders);
                NormalizeProjectTypeAssignments();
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
                RefreshSettingsTypeFileTypes();
                RefreshSettingsAutoDetected();
                RefreshSettingsIgnored();
                RefreshSettingsIgnoredFolders();
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
                TypeName = typeName
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

        btnCountSelectedProject.Click += (_, _) => ApplyCountForSelectedProject();
        btnSettings.Click += (_, _) => ShowProjectLineCounterSettingsDialog();

        btnOk.Click += (_, _) =>
        {
            var normalizedTypes = NormalizeProjectLineCounterTypes(workingTypes);
            var normalizedProjects = NormalizeProjectLineCounterProjects(workingProjects, normalizedTypes);
            var normalizedAuto = NormalizeProjectLineCounterAutoDetectedFileTypes(workingAutoDetectedFileTypes);
            var normalizedIgnored = NormalizeProjectLineCounterIgnoredFileTypes(workingIgnoredFileTypes);
            var normalizedIgnoredFolders = NormalizeProjectLineCounterIgnoredFolders(workingIgnoredFolders);

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
            _projectLineCounterAutoDetectedFileTypes = normalizedAuto;
            _projectLineCounterIgnoredFileTypes = normalizedIgnored;
            _projectLineCounterIgnoredFolders = normalizedIgnoredFolders;
            SaveWindowSettings();
            dlg.DialogResult = true;
        };

        dlg.Loaded += (_, _) =>
        {
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
