using System.Text.Json;

namespace PixelFlow.Core.Trust;

/// <summary>
/// Per-user store of trusted project folder paths (P29). Paths are normalized with
/// <see cref="Normalize"/> and compared case-insensitively on Windows.
/// Default file: <c>%LocalAppData%\PixelFlow\trusted-projects.json</c>.
/// </summary>
public sealed class ProjectTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _storePath;
    private readonly object _gate = new();
    private HashSet<string> _trusted;

    public ProjectTrustStore(string? storePath = null)
    {
        _storePath = storePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PixelFlow",
                "trusted-projects.json");
        _trusted = LoadUnlocked();
    }

    public string StorePath => _storePath;

    /// <summary>Full path without trailing separators; used as the trust key.</summary>
    public static string Normalize(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        return Path.GetFullPath(projectFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public bool IsTrusted(string projectFolder)
    {
        var key = Normalize(projectFolder);
        lock (_gate)
        {
            return _trusted.Contains(key);
        }
    }

    public void Trust(string projectFolder)
    {
        var key = Normalize(projectFolder);
        lock (_gate)
        {
            if (_trusted.Add(key))
            {
                SaveUnlocked();
            }
        }
    }

    public void Revoke(string projectFolder)
    {
        var key = Normalize(projectFolder);
        lock (_gate)
        {
            if (_trusted.Remove(key))
            {
                SaveUnlocked();
            }
        }
    }

    public IReadOnlyCollection<string> ListTrusted()
    {
        lock (_gate)
        {
            return _trusted.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    private HashSet<string> LoadUnlocked()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_storePath))
        {
            return set;
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            var dto = JsonSerializer.Deserialize<TrustStoreDto>(json, JsonOptions);
            if (dto?.TrustedProjects is null)
            {
                return set;
            }

            foreach (var path in dto.TrustedProjects)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    set.Add(Normalize(path));
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt store → treat as empty (fail closed: nothing trusted).
        }
        catch (IOException)
        {
            // Unreadable → fail closed.
        }

        return set;
    }

    private void SaveUnlocked()
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = new TrustStoreDto
        {
            TrustedProjects = _trusted.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var temp = _storePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _storePath, overwrite: true);
    }

    private sealed class TrustStoreDto
    {
        public List<string> TrustedProjects { get; set; } = [];
    }
}
