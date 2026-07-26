using System.Text.Json;
using System.Text.Json.Nodes;
using PixelFlow.Core.Projects.Migration;

namespace PixelFlow.Core.Projects;

/// <summary>
/// Applies ordered schema migrators until the document reaches <see cref="ProjectSchema.CurrentVersion"/>.
/// </summary>
public sealed class ProjectMigrationPipeline
{
    private readonly IReadOnlyDictionary<int, IProjectMigrator> _byFromVersion;

    public ProjectMigrationPipeline(IEnumerable<IProjectMigrator> migrators)
    {
        ArgumentNullException.ThrowIfNull(migrators);
        _byFromVersion = migrators.ToDictionary(static m => m.FromVersion);
    }

    public static ProjectMigrationPipeline Default { get; } = new(
    [
        new V0ToV1Migrator(),
        new IdentityMigrator(ProjectSchema.CurrentVersion),
    ]);

    public ProjectDocument MigrateToCurrent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException("Project JSON parsed to null.");
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException("Project JSON root must be an object.");
        }

        var version = ReadSchemaVersion(root);
        if (version > ProjectSchema.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Unsupported project schemaVersion {version}. This build supports up to {ProjectSchema.CurrentVersion}. Upgrade PixelFlow to open this project.");
        }

        while (version < ProjectSchema.CurrentVersion)
        {
            if (!_byFromVersion.TryGetValue(version, out var migrator))
            {
                throw new NotSupportedException(
                    $"No migrator registered from schemaVersion {version} toward {ProjectSchema.CurrentVersion}.");
            }

            if (migrator.ToVersion <= version)
            {
                throw new InvalidOperationException(
                    $"Migrator from {migrator.FromVersion} must increase schema version (to {migrator.ToVersion}).");
            }

            root = migrator.Migrate(root);
            version = ReadSchemaVersion(root);
            if (version != migrator.ToVersion)
            {
                throw new InvalidOperationException(
                    $"Migrator {migrator.GetType().Name} claimed ToVersion {migrator.ToVersion} but document has schemaVersion {version}.");
            }
        }

        var normalizedJson = root.ToJsonString(ProjectJson.Options);
        return ProjectJson.Deserialize(normalizedJson);
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (root["schemaVersion"] is JsonValue value && value.TryGetValue<int>(out var version))
        {
            return version;
        }

        throw new InvalidOperationException("Project JSON is missing a numeric schemaVersion field.");
    }
}
