using PixelFlow.Core.Trust;

namespace PixelFlow.Core.Tests.Trust;

public sealed class ProjectTrustStoreTests
{
    [Fact]
    public void UntrustedPath_IsNotTrusted_UntilAccepted()
    {
        using var temp = new TempFile();
        var store = new ProjectTrustStore(temp.Path);

        // Downloads-like unfamiliar path
        var downloadsLike = Path.Combine(
            Path.GetTempPath(),
            "PixelFlow.TrustTests",
            "Downloads",
            "untrusted.pflow");

        Assert.False(store.IsTrusted(downloadsLike));

        store.Trust(downloadsLike);
        Assert.True(store.IsTrusted(downloadsLike));

        // Persistence across reload
        var reloaded = new ProjectTrustStore(temp.Path);
        Assert.True(reloaded.IsTrusted(downloadsLike));
    }

    [Fact]
    public void Decline_LeavesUntrusted_RunGateWouldBlock()
    {
        using var temp = new TempFile();
        var store = new ProjectTrustStore(temp.Path);
        var folder = Path.Combine(Path.GetTempPath(), "PixelFlow.TrustTests", "declined.pflow");

        // User opened but declined → never call Trust
        Assert.False(store.IsTrusted(folder));
        Assert.False(CanRun(store, folder));
    }

    [Fact]
    public void Accept_AllowsRun_AndRemembersOnSubsequentOpen()
    {
        using var temp = new TempFile();
        var store = new ProjectTrustStore(temp.Path);
        var folder = Path.Combine(Path.GetTempPath(), "PixelFlow.TrustTests", "accepted.pflow");

        store.Trust(folder);
        Assert.True(CanRun(store, folder));

        var subsequentOpen = new ProjectTrustStore(temp.Path);
        Assert.True(subsequentOpen.IsTrusted(folder));
        Assert.True(CanRun(subsequentOpen, folder));
    }

    [Fact]
    public void Normalize_IgnoresTrailingSeparators_AndIsCaseInsensitive()
    {
        using var temp = new TempFile();
        var store = new ProjectTrustStore(temp.Path);
        var folder = Path.Combine(Path.GetTempPath(), "PixelFlow.TrustTests", "Case.pflow");

        store.Trust(folder + Path.DirectorySeparatorChar);
        Assert.True(store.IsTrusted(folder));
        Assert.True(store.IsTrusted(folder.ToUpperInvariant()));
    }

    [Fact]
    public void Revoke_RemovesTrust()
    {
        using var temp = new TempFile();
        var store = new ProjectTrustStore(temp.Path);
        var folder = Path.Combine(Path.GetTempPath(), "PixelFlow.TrustTests", "revoke.pflow");

        store.Trust(folder);
        store.Revoke(folder);
        Assert.False(store.IsTrusted(folder));
    }

    [Fact]
    public void CorruptStoreFile_FailsClosed()
    {
        using var temp = new TempFile();
        File.WriteAllText(temp.Path, "{ not valid json");
        var store = new ProjectTrustStore(temp.Path);
        Assert.False(store.IsTrusted(@"C:\somewhere\evil.pflow"));
        Assert.Empty(store.ListTrusted());
    }

    /// <summary>Studio run gate: only trusted folders may start the Runner.</summary>
    private static bool CanRun(ProjectTrustStore store, string projectFolder) =>
        store.IsTrusted(projectFolder);

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile()
        {
            var dir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PixelFlow.TrustTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, "trusted-projects.json");
        }

        public void Dispose()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (dir is not null && Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }
}
