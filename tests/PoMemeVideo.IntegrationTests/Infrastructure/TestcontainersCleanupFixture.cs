using System.Diagnostics;

namespace PoMemeVideo.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection-scoped fixture that removes any Docker containers
/// created by <c>Testcontainers</c> after the last test in the
/// <see cref="IntegrationCollection"/> runs.
///
/// Why a fixture instead of per-test <c>DisposeAsync</c>?
///   1. <strong>Crash safety.</strong> If a test hard-crashes (or
///      <c>WithCleanUp</c> is misconfigured), the container is still
///      reaped at collection teardown — never again turning Docker into
///      a graveyard of <c>pomemevideo-test-azurite-…</c> hulks.
///   2. <strong>Multi-project safety.</strong> Docker is shared with
///      other Po* worktrees. We only delete names matching the
///      Testcontainers convention (see <c>scripts/cleanup-testcontainers.ps1</c>
///      for the exact pattern), so the dev compose service
///      <c>pomemevideo-azurite</c> is never at risk.
///   3. <strong>Zero deps.</strong> The fixture shells out to <c>docker rm</c>
///      — no Testcontainers reference needed in the consuming test classes.
///
/// The fixture is wired via <see cref="IntegrationCollection"/>.
/// </summary>
public sealed class TestcontainersCleanupFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // xUnit awaits this before disposing the test host; safe to block.
        await Task.Run(() =>
        {
            try
            {
                var script = ResolveCleanupScript();
                if (script is null)
                {
                    // docker CLI missing — nothing to do (other tests may also
                    // depend on Docker being present, so this is informational).
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var p = Process.Start(psi)!;
                p.WaitForExit(TimeSpan.FromSeconds(30).Milliseconds);
                // stdout/stderr are intentionally swallowed: the script already logs to console
                // during interactive runs; during CI the runner shows its own line.
            }
            catch (Exception)
            {
                // Best-effort cleanup — never fail the test run because of it.
            }
        });
    }

    /// <summary>
    /// Walks up from the test assembly's location to find <c>SCRIPTS/cleanup-testcontainers.ps1</c>.
    /// Tests run from <c>{repo}/tests/PoMemeVideo.IntegrationTests/bin/...</c>, so two levels up is the repo.
    /// </summary>
    private static string? ResolveCleanupScript()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "SCRIPTS", "cleanup-testcontainers.ps1");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
