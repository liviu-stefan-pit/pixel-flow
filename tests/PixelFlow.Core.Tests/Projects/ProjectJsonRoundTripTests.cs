using PixelFlow.Core.Projects;

namespace PixelFlow.Core.Tests.Projects;

public sealed class ProjectJsonRoundTripTests
{
    [Fact]
    public void Serialize_Deserialize_Serialize_IsStable()
    {
        var original = SampleProject();
        var firstJson = ProjectJson.Serialize(original);
        var roundTripped = ProjectJson.Deserialize(firstJson);
        var secondJson = ProjectJson.Serialize(roundTripped);

        Assert.Equal(firstJson, secondJson);
        AssertEqual(original, roundTripped);
    }

    [Fact]
    public void MinimalFixture_RoundTripsAndMatchesArchitectureFields()
    {
        var path = FixturePath("minimal.pflow", "project.json");
        var json = File.ReadAllText(path);
        var project = ProjectJson.Deserialize(json);

        Assert.Equal(ProjectSchema.CurrentVersion, project.SchemaVersion);
        Assert.Equal("minimal", project.Name);
        Assert.Equal(5000, project.Defaults.TimeoutMs);
        Assert.Equal(3, project.Defaults.Retry.MaxAttempts);
        Assert.Equal(250, project.Defaults.Retry.BackoffMs);
        Assert.Equal(2, project.Steps.Count);
        Assert.Contains("greeting", project.Variables.Keys);

        var click = Assert.Single(project.Steps, step => step.Type == "Click");
        Assert.NotNull(click.Locator);
        Assert.Equal("TbSubmit", click.Locator!.Layers[0].AutomationId);

        var again = ProjectJson.Serialize(project);
        var third = ProjectJson.Serialize(ProjectJson.Deserialize(again));
        Assert.Equal(again, third);
    }

    [Fact]
    public void VariableKeyOrder_DoesNotAffectSerializedOutput()
    {
        var a = SampleProject();
        a.Variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["z"] = "1",
            ["a"] = "2",
        };

        var b = SampleProject();
        b.Variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a"] = "2",
            ["z"] = "1",
        };

        Assert.Equal(ProjectJson.Serialize(a), ProjectJson.Serialize(b));
    }

    private static ProjectDocument SampleProject() => new()
    {
        SchemaVersion = ProjectSchema.CurrentVersion,
        Name = "sample",
        Variables =
        {
            ["b"] = "2",
            ["a"] = "1",
        },
        Defaults = new ProjectDefaults
        {
            TimeoutMs = 4000,
            Retry = new RetryPolicy { MaxAttempts = 2, BackoffMs = 100 },
        },
        Steps =
        [
            new ScriptStep
            {
                Id = "s1",
                Type = "Wait",
                WaitMs = 10,
            },
            new ScriptStep
            {
                Id = "s2",
                Type = "Click",
                TimeoutMs = 1000,
                Locator = new LocatorChain
                {
                    Scope = new ProcessWindowScope
                    {
                        ProcessName = "App",
                        WindowTitle = "Main",
                    },
                    Layers =
                    [
                        new LocatorLayer
                        {
                            Kind = "UiaStructural",
                            AutomationId = "Btn",
                            ControlType = "Button",
                            Name = "Go",
                            ConfidenceThreshold = 0.9,
                        },
                    ],
                },
            },
        ],
    };

    private static void AssertEqual(ProjectDocument expected, ProjectDocument actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Defaults.TimeoutMs, actual.Defaults.TimeoutMs);
        Assert.Equal(expected.Defaults.Retry.MaxAttempts, actual.Defaults.Retry.MaxAttempts);
        Assert.Equal(expected.Defaults.Retry.BackoffMs, actual.Defaults.Retry.BackoffMs);
        Assert.Equal(expected.Variables, actual.Variables);
        Assert.Equal(expected.Steps.Count, actual.Steps.Count);
        for (var i = 0; i < expected.Steps.Count; i++)
        {
            Assert.Equal(expected.Steps[i].Id, actual.Steps[i].Id);
            Assert.Equal(expected.Steps[i].Type, actual.Steps[i].Type);
            Assert.Equal(expected.Steps[i].WaitMs, actual.Steps[i].WaitMs);
            Assert.Equal(expected.Steps[i].TimeoutMs, actual.Steps[i].TimeoutMs);
            Assert.Equal(
                expected.Steps[i].Locator?.Layers.FirstOrDefault()?.AutomationId,
                actual.Steps[i].Locator?.Layers.FirstOrDefault()?.AutomationId);
        }
    }

    private static string FixturePath(params string[] parts)
    {
        var root = TestPaths.FindRepoRoot();
        return Path.Combine(new[] { root, "fixtures", "projects" }.Concat(parts).ToArray());
    }
}
