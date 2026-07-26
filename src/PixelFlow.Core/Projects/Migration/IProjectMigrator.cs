using System.Text.Json.Nodes;

namespace PixelFlow.Core.Projects.Migration;

/// <summary>
/// Migrates a project JSON document from one schema version to the next.
/// </summary>
public interface IProjectMigrator
{
    int FromVersion { get; }

    int ToVersion { get; }

    JsonObject Migrate(JsonObject document);
}
