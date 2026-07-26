using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Tests.Projects;

public sealed class ProjectStoreTests
{
    [Fact]
    public void Save_CreatesProjectJsonAndAssetsFolder()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore(historyRetention: 3);
        var projectFolder = Path.Combine(temp.Path, "demo.pflow");
        var document = MinimalDocument("first");

        store.Save(projectFolder, document);

        Assert.True(File.Exists(ProjectPaths.ProjectFile(projectFolder)));
        Assert.True(Directory.Exists(ProjectPaths.AssetsFolder(projectFolder)));
        Assert.True(Directory.Exists(ProjectPaths.HistoryFolder(projectFolder)));

        var loaded = store.Load(projectFolder);
        Assert.Equal("first", loaded.Name);
        Assert.Equal(ProjectSchema.CurrentVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void SecondSave_WritesHistoryBackup()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore(historyRetention: 5);
        var projectFolder = Path.Combine(temp.Path, "demo.pflow");

        store.Save(projectFolder, MinimalDocument("v1"));
        store.Save(projectFolder, MinimalDocument("v2"));

        var historyFiles = Directory.GetFiles(ProjectPaths.HistoryFolder(projectFolder), "project-*.json");
        Assert.Single(historyFiles);

        var backupJson = File.ReadAllText(historyFiles[0]);
        var backup = ProjectJson.Deserialize(backupJson);
        Assert.Equal("v1", backup.Name);

        var current = store.Load(projectFolder);
        Assert.Equal("v2", current.Name);
    }

    [Fact]
    public void HistoryRotation_KeepsOnlyRetentionCount()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore(historyRetention: 2);
        var projectFolder = Path.Combine(temp.Path, "demo.pflow");

        store.Save(projectFolder, MinimalDocument("n0"));
        store.Save(projectFolder, MinimalDocument("n1"));
        store.Save(projectFolder, MinimalDocument("n2"));
        store.Save(projectFolder, MinimalDocument("n3"));

        var historyFiles = Directory.GetFiles(ProjectPaths.HistoryFolder(projectFolder), "project-*.json");
        Assert.Equal(2, historyFiles.Length);
        Assert.Equal("n3", store.Load(projectFolder).Name);
    }

    [Fact]
    public void InterruptedSave_BeforeReplace_LeavesOriginalIntact()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore();
        var projectFolder = Path.Combine(temp.Path, "demo.pflow");
        store.Save(projectFolder, MinimalDocument("good"));

        var originalJson = File.ReadAllText(ProjectPaths.ProjectFile(projectFolder));
        var tempFile = ProjectPaths.TempProjectFile(projectFolder);
        File.WriteAllText(tempFile, "{ \"schemaVersion\": 1, \"name\": \"corrupt-attempt\", \"steps\": [] }");

        // Simulate crash after temp write, before rename/replace.
        Assert.True(File.Exists(tempFile));
        Assert.Equal(originalJson, File.ReadAllText(ProjectPaths.ProjectFile(projectFolder)));

        var loaded = store.Load(projectFolder);
        Assert.Equal("good", loaded.Name);
    }

    [Fact]
    public void AssetPath_UsesContentHashConvention()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pflow-assets-check");
        var path = ProjectPaths.AssetPath(folder, "deadbeef", ".png");
        Assert.Equal(
            Path.Combine(folder, "assets", "sha256-deadbeef.png"),
            path);
        Assert.Equal("sha256-abc.png", ProjectPaths.AssetFileName("sha256-abc", ".png"));
    }

    private static ProjectDocument MinimalDocument(string name) => new()
    {
        SchemaVersion = ProjectSchema.CurrentVersion,
        Name = name,
        Steps =
        [
            new ScriptStep { Id = "w", Type = "Wait", WaitMs = 1 },
        ],
    };

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pixelflow-tests",
            Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test dirs.
            }
        }
    }
}
