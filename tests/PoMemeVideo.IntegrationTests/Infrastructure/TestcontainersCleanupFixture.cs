using System.Diagnostics;
using System.Text.RegularExpressions;

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
///      Testcontainers convention, so the dev compose service
///      <c>pomemevideo-azurite</c> is never at risk.
///   3. <strong>Zero deps.</strong> The fixture shells out to <c>docker</c>
///      — no Testcontainers reference needed in the consuming test classes.
///
/// This used to shell out to a PowerShell script, resolved by walking up for
/// <c>SCRIPTS/cleanup-testcontainers.ps1</c>. That lookup was case-sensitive against a
/// lowercase <c>scripts/</c>, so on Linux it found nothing and the fixture silently reaped
/// nothing at all. The logic is inlined here instead: one less script, and it runs everywhere.
///
/// The fixture is wired via <see cref="IntegrationCollection"/>.
/// </summary>
public sealed class TestcontainersCleanupFixture : IAsyncLifetime
{
    // Testcontainers default name pattern (verified against 4.5.0):
    //   {WithName-or-assembly-name}-test-{image-name}-{16-32-char-hex-checksum}
    private static readonly Regex TestContainerName =
        new(@"^.+-test-[^-]+-[0-9a-f]{16,32}$", RegexOptions.Compiled);

    // Managed elsewhere (docker-compose.yml dev stack) — never touched.
    private static readonly string[] ProtectedNames = ["pomemevideo-azurite"];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // xUnit awaits this before disposing the test host; safe to block.
        await Task.Run(() =>
        {
            try
            {
                foreach (var name in ListContainerNames())
                {
                    if (!TestContainerName.IsMatch(name) || ProtectedNames.Contains(name))
                        continue;

                    RunDocker($"container rm -f {name}");
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup — never fail the test run because of it.
                // A missing docker CLI lands here too, which is fine: nothing to reap.
            }
        });
    }

    private static IEnumerable<string> ListContainerNames()
    {
        var output = RunDocker("ps -a --format {{.Names}}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string RunDocker(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        // The previous code passed TimeSpan.FromSeconds(30).Milliseconds — which is 0, not 30000,
        // so it never actually waited. Pass the TimeSpan itself.
        p.WaitForExit(TimeSpan.FromSeconds(30));
        return stdout;
    }
}
