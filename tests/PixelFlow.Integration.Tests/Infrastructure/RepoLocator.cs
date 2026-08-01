using System.IO;

namespace PixelFlow.Integration.Tests.Infrastructure;

/// <summary>
/// Locates the checked-out repo root from the test assembly's output directory.
/// </summary>
internal static class RepoLocator
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    public static string FixtureProjectPath(string fixtureName) =>
        Path.Combine(Root, "fixtures", "projects", fixtureName + ".pflow");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fixtures = Path.Combine(dir.FullName, "fixtures", "projects");
            var docs = Path.Combine(dir.FullName, "docs", "phases.md");
            if (Directory.Exists(fixtures) && File.Exists(docs))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root from {AppContext.BaseDirectory}.");
    }
}
