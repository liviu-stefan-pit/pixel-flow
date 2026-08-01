using PixelFlow.Core.Projects;
using PixelFlow.Core.Runner;

namespace PixelFlow.Core.Tests.Projects;

public sealed class SecretRefModelTests
{
    [Fact]
    public void SecretRef_RoundTrips_AndNeverSerializesSecretValue()
    {
        var document = new ProjectDocument
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            Name = "secrets",
            Steps =
            [
                new ScriptStep
                {
                    Id = "type-secret",
                    Type = "Type",
                    SecretRef = "PixelFlow/TestSecret",
                    // Intentionally no Text — secret lives in Credential Manager only.
                },
            ],
        };

        var json = ProjectJson.Serialize(document);
        Assert.Contains("\"secretRef\": \"PixelFlow/TestSecret\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);

        var loaded = ProjectJson.Deserialize(json);
        Assert.Equal("PixelFlow/TestSecret", loaded.Steps[0].SecretRef);
        Assert.Null(loaded.Steps[0].Text);
    }

    [Fact]
    public void TypeSecretFixture_ContainsNameOnly()
    {
        var path = Path.Combine(
            TestPaths.FindRepoRoot(),
            "fixtures",
            "projects",
            "type-secret.pflow",
            "project.json");
        var json = File.ReadAllText(path);
        var project = ProjectJson.Deserialize(json);

        var step = Assert.Single(project.Steps);
        Assert.Equal("Type", step.Type);
        Assert.Equal("PixelFlow/TestSecret", step.SecretRef);
        Assert.Null(step.Text);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldCaptureFailureScreenshot_ForcedOffForSecretRef()
    {
        var step = new ScriptStep
        {
            Id = "s",
            Type = "Type",
            SecretRef = "PixelFlow/X",
            CaptureFailureScreenshot = true,
        };
        var defaults = new ProjectDefaults { CaptureFailureScreenshots = true };

        Assert.False(RunnerEngine.ShouldCaptureFailureScreenshot(step, defaults));
    }
}
