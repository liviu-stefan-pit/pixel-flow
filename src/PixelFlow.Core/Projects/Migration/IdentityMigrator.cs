using System.Text.Json.Nodes;

namespace PixelFlow.Core.Projects.Migration;

/// <summary>
/// No-op migrator for the current schema (already at the target version).
/// </summary>
public sealed class IdentityMigrator : IProjectMigrator
{
    public IdentityMigrator(int version)
    {
        FromVersion = version;
        ToVersion = version;
    }

    public int FromVersion { get; }

    public int ToVersion { get; }

    public JsonObject Migrate(JsonObject document) => document;
}
