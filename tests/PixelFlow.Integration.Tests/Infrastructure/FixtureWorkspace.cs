using System.IO;

namespace PixelFlow.Integration.Tests.Infrastructure;

/// <summary>
/// Copies a checked-in <c>fixtures/projects/*.pflow</c> bundle into a scratch temp folder
/// (never copying any pre-existing generated <c>reports/</c>) so Live runs cannot dirty the
/// repo tree. Deletes the scratch copy on dispose, best-effort.
/// </summary>
internal sealed class FixtureWorkspace : IDisposable
{
    public string ProjectFolder { get; }

    private FixtureWorkspace(string projectFolder)
    {
        ProjectFolder = projectFolder;
    }

    public static FixtureWorkspace CreateCopy(string fixtureName)
    {
        var source = RepoLocator.FixtureProjectPath(fixtureName);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Fixture not found: {source}");
        }

        var destination = Path.Combine(
            Path.GetTempPath(),
            "PixelFlow.IntegrationTests",
            Guid.NewGuid().ToString("N"),
            fixtureName + ".pflow");

        CopyDirectory(new DirectoryInfo(source), destination);
        return new FixtureWorkspace(destination);
    }

    private static void CopyDirectory(DirectoryInfo source, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationDir, file.Name), overwrite: true);
        }

        foreach (var sub in source.GetDirectories())
        {
            // Never copy a fixture's own generated reports/ into the scratch workspace.
            if (string.Equals(sub.Name, "reports", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(sub, Path.Combine(destinationDir, sub.Name));
        }
    }

    public void Dispose()
    {
        try
        {
            var scratchRoot = Path.GetDirectoryName(ProjectFolder);
            if (scratchRoot is not null && Directory.Exists(scratchRoot))
            {
                Directory.Delete(scratchRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; leftovers under %TEMP% are not load-bearing.
        }
    }
}
