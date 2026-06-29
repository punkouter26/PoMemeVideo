namespace PoMemeVideo.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection definition for all Integration tests.
/// Every test class tagged <c>[Collection("Integration")]</c> shares a single
/// <see cref="TestcontainersCleanupFixture"/> lifecycle, ensuring any
/// Testcontainers containers created during the suite are removed at
/// collection teardown — even if the process exits abnormally.
///
/// Combine with future Testcontainers-based tests by:
/// <list type="number">
///   <item>Registering the container via <c>IContainer.StartAsync()</c> as usual.</item>
///   <item>Disposing it in <c>DisposeAsync</c> for the happy path.</item>
///   <item>Tagging the test class with <c>[Collection("Integration")]</c>.</item>
/// </list>
/// The collection-level fixture is a <em>safety net</em>: it does not
/// replace per-test disposal, it covers the cases where that disposal
/// is skipped (crash, kill, debugger detach, etc.).
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<TestcontainersCleanupFixture>
{
}
