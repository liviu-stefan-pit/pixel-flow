namespace PixelFlow.Integration.Tests.Infrastructure;

/// <summary>
/// Shares one <see cref="TestBenchFixture"/> (one Test Bench launch/lifetime) across all Live tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TestBenchCollection : ICollectionFixture<TestBenchFixture>
{
    public const string Name = "TestBench collection (Live)";
}
