namespace PixelFlow.Core.Projects;

/// <summary>
/// Conventional paths inside a <c>*.pflow</c> project folder bundle.
/// </summary>
public static class ProjectPaths
{
    public const string ProjectFileName = "project.json";
    public const string AssetsFolderName = "assets";
    public const string HistoryFolderName = "history";
    public const string ReportsFolderName = "reports";
    public const string TempFileName = "project.json.tmp";

    public static string ProjectFile(string projectFolder) =>
        Path.Combine(projectFolder, ProjectFileName);

    public static string TempProjectFile(string projectFolder) =>
        Path.Combine(projectFolder, TempFileName);

    public static string AssetsFolder(string projectFolder) =>
        Path.Combine(projectFolder, AssetsFolderName);

    public static string HistoryFolder(string projectFolder) =>
        Path.Combine(projectFolder, HistoryFolderName);

    public static string ReportsFolder(string projectFolder) =>
        Path.Combine(projectFolder, ReportsFolderName);

    /// <summary>
    /// Content-hash asset file name convention.
    /// Example: <c>sha256-abc....png</c>
    /// </summary>
    public static string AssetFileName(string contentHash, string extension = ".png")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = "." + extension;
        }

        var hash = contentHash.StartsWith("sha256-", StringComparison.OrdinalIgnoreCase)
            ? contentHash
            : "sha256-" + contentHash;
        return hash + extension;
    }

    public static string AssetPath(string projectFolder, string contentHash, string extension = ".png") =>
        Path.Combine(AssetsFolder(projectFolder), AssetFileName(contentHash, extension));
}
