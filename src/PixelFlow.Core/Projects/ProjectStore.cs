using System.Text;

namespace PixelFlow.Core.Projects;

/// <summary>
/// Load/save <c>*.pflow</c> project folders with atomic writes and rolling history backups.
/// </summary>
public sealed class ProjectStore
{
    private readonly ProjectMigrationPipeline _migrations;
    private readonly int _historyRetention;

    public ProjectStore(int historyRetention = 10, ProjectMigrationPipeline? migrations = null)
    {
        if (historyRetention < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(historyRetention), "Retention must be at least 1.");
        }

        _historyRetention = historyRetention;
        _migrations = migrations ?? ProjectMigrationPipeline.Default;
    }

    public int HistoryRetention => _historyRetention;

    public ProjectDocument Load(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var path = ProjectPaths.ProjectFile(projectFolder);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Project file not found: {path}", path);
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return _migrations.MigrateToCurrent(json);
    }

    public void Save(string projectFolder, ProjectDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != ProjectSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Refusing to save schemaVersion {document.SchemaVersion}; current is {ProjectSchema.CurrentVersion}. Migrate before save.");
        }

        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(ProjectPaths.AssetsFolder(projectFolder));
        Directory.CreateDirectory(ProjectPaths.HistoryFolder(projectFolder));

        var projectFile = ProjectPaths.ProjectFile(projectFolder);
        if (File.Exists(projectFile))
        {
            WriteHistoryBackup(projectFolder, projectFile);
        }

        var json = ProjectJson.Serialize(document);
        var tempFile = ProjectPaths.TempProjectFile(projectFolder);

        // Atomic save: write temp, flush, then replace target. A crash before replace
        // leaves the previous project.json intact.
        WriteAllTextAtomicPrep(tempFile, json);
        ReplaceFile(tempFile, projectFile);
        RotateHistory(projectFolder);
    }

    /// <summary>
    /// Ensures the assets folder exists. Asset bytes are stored by content hash in later phases.
    /// </summary>
    public string EnsureAssetsFolder(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var assets = ProjectPaths.AssetsFolder(projectFolder);
        Directory.CreateDirectory(assets);
        return assets;
    }

    private void WriteHistoryBackup(string projectFolder, string projectFile)
    {
        var historyDir = ProjectPaths.HistoryFolder(projectFolder);
        Directory.CreateDirectory(historyDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var backupName = $"project-{stamp}.json";
        var backupPath = Path.Combine(historyDir, backupName);
        File.Copy(projectFile, backupPath, overwrite: false);
    }

    private void RotateHistory(string projectFolder)
    {
        var historyDir = ProjectPaths.HistoryFolder(projectFolder);
        if (!Directory.Exists(historyDir))
        {
            return;
        }

        var files = Directory.GetFiles(historyDir, "project-*.json")
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static info => info.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var stale in files.Skip(_historyRetention))
        {
            stale.Delete();
        }
    }

    private static void WriteAllTextAtomicPrep(string tempFile, string contents)
    {
        using var stream = new FileStream(
            tempFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(contents);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFile(string sourceTempFile, string destinationFile)
    {
        if (File.Exists(destinationFile))
        {
            File.Replace(sourceTempFile, destinationFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(sourceTempFile, destinationFile);
        }
    }
}
