namespace PixelFlow.Core.Tests;

internal static class TestPaths
{
    public static string FindRepoRoot()
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
