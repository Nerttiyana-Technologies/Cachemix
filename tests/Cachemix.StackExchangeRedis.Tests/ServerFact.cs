using System.Net.Sockets;
using Xunit;

namespace Cachemix.StackExchangeRedis.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips the test when no Redis-compatible
/// server is available — neither one already listening at the configured address
/// nor Docker (for a throwaway container). The integration tests need a real
/// server; without one they are skipped, not failed.
/// </summary>
public sealed class ServerFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, skipping the test when no server can be reached.</summary>
    public ServerFactAttribute()
    {
        if (!TestServer.CanRun)
        {
            Skip = $"No Redis-compatible server is available — start one at {TestServer.Address}, " +
                   "set the CACHEMIX_TEST_REDIS environment variable, or run Docker.";
        }
    }
}

/// <summary>
/// Resolves the Redis-compatible server the integration tests run against. A
/// server already listening at <see cref="Address"/> is preferred (your local
/// Redis or Valkey); otherwise a throwaway Docker container is used.
/// </summary>
internal static class TestServer
{
    /// <summary>
    /// The address probed for an already-running server: the
    /// <c>CACHEMIX_TEST_REDIS</c> environment variable, or <c>localhost:6379</c>.
    /// </summary>
    public static string Address { get; } =
        Environment.GetEnvironmentVariable("CACHEMIX_TEST_REDIS") is { Length: > 0 } configured
            ? configured
            : "localhost:6379";

    /// <summary>Whether a server is already listening at <see cref="Address"/>.</summary>
    public static bool IsLocalServerReachable { get; } = Probe(Address);

    /// <summary>Whether a Docker daemon looks reachable, for a throwaway container.</summary>
    public static bool IsDockerAvailable { get; } = DetectDocker();

    /// <summary>Whether the integration tests have any server to run against.</summary>
    public static bool CanRun => IsLocalServerReachable || IsDockerAvailable;

    private static bool Probe(string address)
    {
        (string host, int port) = SplitAddress(address);
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (string Host, int Port) SplitAddress(string address)
    {
        // Accept "host:port"; fall back to the conventional Redis port.
        int colon = address.LastIndexOf(':');
        return colon > 0 && int.TryParse(address[(colon + 1)..], out int port)
            ? (address[..colon], port)
            : (address, 6379);
    }

    private static bool DetectDocker()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"\\.\pipe\docker_engine");
        }

        // Linux and macOS reach the daemon through a Unix domain socket.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return File.Exists("/var/run/docker.sock")
            || File.Exists(Path.Combine(home, ".docker", "run", "docker.sock"))
            || File.Exists(Path.Combine(home, ".colima", "default", "docker.sock"));
    }
}
