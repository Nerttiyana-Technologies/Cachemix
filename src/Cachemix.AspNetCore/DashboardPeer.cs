namespace Cachemix.AspNetCore;

/// <summary>
/// A peer Cachemix dashboard that this instance's Multi-Instance Diff compares
/// against. Register peers with <c>AddPeer</c> on the Cachemix builder.
/// </summary>
public sealed class DashboardPeer
{
    /// <summary>Creates a peer.</summary>
    /// <param name="name">A short, unique label for the peer instance.</param>
    /// <param name="url">The peer's dashboard base URL (for example, <c>http://node-b:8080/cachemix</c>).</param>
    public DashboardPeer(string name, Uri url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(url);

        Name = name;
        Url = url;
    }

    /// <summary>A short, unique label for the peer instance.</summary>
    public string Name { get; }

    /// <summary>The peer's dashboard base URL.</summary>
    public Uri Url { get; }
}
