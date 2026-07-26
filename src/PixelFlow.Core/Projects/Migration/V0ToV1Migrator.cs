using System.Text.Json.Nodes;

namespace PixelFlow.Core.Projects.Migration;

/// <summary>
/// Demo migrator: schema 0 used <c>actions</c>/<c>action</c>; schema 1 uses <c>steps</c>/<c>type</c>.
/// </summary>
public sealed class V0ToV1Migrator : IProjectMigrator
{
    public int FromVersion => 0;

    public int ToVersion => 1;

    public JsonObject Migrate(JsonObject document)
    {
        if (document["actions"] is JsonArray actions)
        {
            var steps = new JsonArray();
            foreach (var node in actions)
            {
                if (node is not JsonObject action)
                {
                    continue;
                }

                var step = new JsonObject();
                foreach (var property in action)
                {
                    if (property.Key.Equals("action", StringComparison.OrdinalIgnoreCase))
                    {
                        step["type"] = property.Value is JsonValue value && value.TryGetValue<string>(out var actionName)
                            ? MapActionToType(actionName)
                            : property.Value?.DeepClone();
                    }
                    else
                    {
                        step[property.Key] = property.Value?.DeepClone();
                    }
                }

                steps.Add(step);
            }

            document.Remove("actions");
            document["steps"] = steps;
        }

        document["schemaVersion"] = ProjectSchema.CurrentVersion;

        if (document["defaults"] is null)
        {
            document["defaults"] = new JsonObject
            {
                ["timeoutMs"] = 5000,
                ["retry"] = new JsonObject
                {
                    ["maxAttempts"] = 3,
                    ["backoffMs"] = 250,
                },
            };
        }

        if (document["variables"] is null)
        {
            document["variables"] = new JsonObject();
        }

        return document;
    }

    private static string MapActionToType(string? action) =>
        action?.Trim().ToLowerInvariant() switch
        {
            "click" => "Click",
            "type" => "Type",
            "wait" => "Wait",
            _ => string.IsNullOrWhiteSpace(action) ? "Click" : char.ToUpperInvariant(action[0]) + action[1..],
        };
}
