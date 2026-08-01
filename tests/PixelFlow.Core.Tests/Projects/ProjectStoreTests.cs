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

    [Fact]
    public void SavePngAsset_WritesHashedFileAndDedupesIdenticalBytes()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore();
        var projectFolder = Path.Combine(temp.Path, "assets-demo.pflow");
        store.Save(projectFolder, MinimalDocument("assets"));

        // Minimal valid PNG (1x1 transparent).
        var png = Convert.FromHexString(
            "89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C489" +
            "0000000A49444154789A63000100000500010D0A2DB40000000049454E44AE426082");

        var hash1 = store.SavePngAsset(projectFolder, png);
        Assert.StartsWith("sha256-", hash1);
        var path = ProjectPaths.AssetPath(projectFolder, hash1);
        Assert.True(File.Exists(path));
        Assert.Equal(png, File.ReadAllBytes(path));

        var hash2 = store.SavePngAsset(projectFolder, png);
        Assert.Equal(hash1, hash2);
        Assert.Single(Directory.GetFiles(ProjectPaths.AssetsFolder(projectFolder), "sha256-*.png"));
    }

    [Fact]
    public void EditorShapedDocument_RoundTripsClickTypeWait()
    {
        using var temp = new TempDirectory();
        var store = new ProjectStore();
        var projectFolder = Path.Combine(temp.Path, "editor.pflow");
        var document = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "editor-roundtrip",
            Steps =
            [
                new ScriptStep { Id = "w1", Type = "Wait", WaitMs = 250 },
                new ScriptStep
                {
                    Id = "c1",
                    Type = "Click",
                    Locator = new LocatorChain
                    {
                        Scope = new ProcessWindowScope
                        {
                            ProcessName = "PixelFlow.TestBench",
                            WindowTitle = "Test Bench",
                        },
                        Layers =
                        [
                            new LocatorLayer
                            {
                                Kind = "UiaStructural",
                                Enabled = true,
                                AutomationId = "TbSubmit",
                                ControlType = "Button",
                                Name = "Submit",
                            },
                        ],
                    },
                },
                new ScriptStep { Id = "t1", Type = "Type", Text = "hello" },
            ],
        };

        // Simulate editor reorder: move Type before Click.
        var type = document.Steps[2];
        document.Steps.RemoveAt(2);
        document.Steps.Insert(1, type);

        store.Save(projectFolder, document);
        var loaded = store.Load(projectFolder);

        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal(["w1", "t1", "c1"], loaded.Steps.Select(s => s.Id).ToArray());
        Assert.Equal("Wait", loaded.Steps[0].Type);
        Assert.Equal(250, loaded.Steps[0].WaitMs);
        Assert.Equal("Type", loaded.Steps[1].Type);
        Assert.Equal("hello", loaded.Steps[1].Text);
        Assert.Equal("Click", loaded.Steps[2].Type);
        Assert.Equal("TbSubmit", loaded.Steps[2].Locator!.Layers[0].AutomationId);
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
