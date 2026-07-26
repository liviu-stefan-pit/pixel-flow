using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Tests.Projects;

public sealed class ProjectMigrationTests
{
    [Fact]
    public void LegacyV0Fixture_MigratesToGoldenV1()
    {
        var legacyPath = FixturePath("legacy-v0.pflow", "project.json");
        var expectedPath = FixturePath("legacy-v0.pflow", "expected-v1.json");

        var migrated = ProjectMigrationPipeline.Default.MigrateToCurrent(File.ReadAllText(legacyPath));
        var expected = ProjectJson.Deserialize(File.ReadAllText(expectedPath));

        Assert.Equal(ProjectSchema.CurrentVersion, migrated.SchemaVersion);
        Assert.Equal(expected.Name, migrated.Name);
        Assert.Equal(expected.Variables, migrated.Variables);
        Assert.Equal(expected.Defaults.TimeoutMs, migrated.Defaults.TimeoutMs);
        Assert.Equal(expected.Defaults.Retry.MaxAttempts, migrated.Defaults.Retry.MaxAttempts);
        Assert.Equal(expected.Steps.Count, migrated.Steps.Count);
        Assert.Equal("Wait", migrated.Steps[0].Type);
        Assert.Equal("Click", migrated.Steps[1].Type);
        Assert.Equal(expected.Steps[1].Locator!.Layers[0].AutomationId, migrated.Steps[1].Locator!.Layers[0].AutomationId);

        // Golden JSON equality after normalize through our serializer.
        Assert.Equal(ProjectJson.Serialize(expected), ProjectJson.Serialize(migrated));
    }

    [Fact]
    public void LoadViaStore_MigratesLegacyAndSavePersistsV1()
    {
        using var temp = new TempDirectory();
        var source = FixturePath("legacy-v0.pflow");
        var projectFolder = Path.Combine(temp.Path, "legacy-v0.pflow");
        CopyDirectory(source, projectFolder);

        // Do not copy the golden expected file into the project bundle content under test.
        var expectedCopy = Path.Combine(projectFolder, "expected-v1.json");
        if (File.Exists(expectedCopy))
        {
            File.Delete(expectedCopy);
        }

        var store = new ProjectStore();
        var loaded = store.Load(projectFolder);
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.Equal("Wait", loaded.Steps[0].Type);

        store.Save(projectFolder, loaded);
        var onDisk = ProjectJson.Deserialize(File.ReadAllText(ProjectPaths.ProjectFile(projectFolder)));
        Assert.Equal(1, onDisk.SchemaVersion);
        Assert.DoesNotContain("\"actions\"", File.ReadAllText(ProjectPaths.ProjectFile(projectFolder)));
    }

    [Fact]
    public void CurrentSchema_IdentityLoad()
    {
        var json = File.ReadAllText(FixturePath("minimal.pflow", "project.json"));
        var project = ProjectMigrationPipeline.Default.MigrateToCurrent(json);
        Assert.Equal(1, project.SchemaVersion);
        Assert.Equal("minimal", project.Name);
    }

    [Fact]
    public void UnknownFutureSchema_FailsLoudly()
    {
        const string future = """
            {
              "schemaVersion": 99,
              "name": "from-the-future",
              "steps": []
            }
            """;

        var ex = Assert.Throws<NotSupportedException>(
            () => ProjectMigrationPipeline.Default.MigrateToCurrent(future));
        Assert.Contains("99", ex.Message);
        Assert.Contains(ProjectSchema.CurrentVersion.ToString(), ex.Message);
    }

    [Fact]
    public void MissingSchemaVersion_FailsLoudly()
    {
        const string missing = """{ "name": "no-version", "steps": [] }""";
        Assert.Throws<InvalidOperationException>(
            () => ProjectMigrationPipeline.Default.MigrateToCurrent(missing));
    }

    private static string FixturePath(params string[] parts)
    {
        var root = TestPaths.FindRepoRoot();
        return Path.Combine(new[] { root, "fixtures", "projects" }.Concat(parts).ToArray());
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

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
                // Best-effort cleanup.
            }
        }
    }
}
