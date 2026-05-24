using System.Text.Json;
using System.Text.Json.Serialization;
using Cachemix.Abstractions;

namespace Cachemix.AspNetCore;

/// <summary>Why a cache key is reported as a divergence across instances.</summary>
public enum DiffKind
{
    /// <summary>The key is present on some instances but absent on others.</summary>
    Missing,

    /// <summary>The key is present everywhere but its type or size differs.</summary>
    Divergent,
}

/// <summary>One instance's participation in a Multi-Instance Diff.</summary>
public sealed record DiffInstance
{
    /// <summary>The instance's label.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the instance's snapshot was successfully retrieved.</summary>
    public required bool Reachable { get; init; }

    /// <summary>The reason the instance could not be reached, when unreachable.</summary>
    public string? Error { get; init; }

    /// <summary>The number of tracked entries the instance reported.</summary>
    public int EntryCount { get; init; }
}

/// <summary>One instance's view of a single diverging cache key.</summary>
public sealed record DiffCell
{
    /// <summary>The instance this cell belongs to.</summary>
    public required string Instance { get; init; }

    /// <summary>Whether the key is present on this instance.</summary>
    public required bool Present { get; init; }

    /// <summary>The CLR type name observed for the key on this instance, when present.</summary>
    public string? ValueTypeName { get; init; }

    /// <summary>The estimated size observed for the key on this instance, when present.</summary>
    public long? SizeBytes { get; init; }
}

/// <summary>A single cache key that differs across instances.</summary>
public sealed record DiffEntry
{
    /// <summary>The cache key.</summary>
    public required string Key { get; init; }

    /// <summary>The logical name of the cache holding the key.</summary>
    public required string CacheName { get; init; }

    /// <summary>Why the key is a divergence.</summary>
    public required DiffKind Kind { get; init; }

    /// <summary>One cell per reachable instance, in instance order.</summary>
    public required IReadOnlyList<DiffCell> Cells { get; init; }
}

/// <summary>The result of comparing cache state across this instance and its peers.</summary>
public sealed record InstanceDiffReport
{
    /// <summary>Every instance considered, reachable or not.</summary>
    public required IReadOnlyList<DiffInstance> Instances { get; init; }

    /// <summary>The cache keys that differ across the reachable instances.</summary>
    public required IReadOnlyList<DiffEntry> Divergences { get; init; }

    /// <summary>When the diff was produced, in UTC.</summary>
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

/// <summary>
/// Builds a <see cref="InstanceDiffReport"/> by comparing this instance's cache
/// state against every configured peer dashboard.
/// </summary>
internal sealed class InstanceDiffService
{
    private const int MaxDivergences = 500;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions PeerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DashboardSnapshotProvider _snapshots;
    private readonly IReadOnlyList<DashboardPeer> _peers;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Creates the diff service.</summary>
    /// <param name="snapshots">The local snapshot provider.</param>
    /// <param name="peers">The configured peer dashboards.</param>
    /// <param name="httpClientFactory">The factory used to fetch peer snapshots.</param>
    public InstanceDiffService(
        DashboardSnapshotProvider snapshots,
        IEnumerable<DashboardPeer> peers,
        IHttpClientFactory httpClientFactory)
    {
        _snapshots = snapshots;
        _peers = [.. peers];
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Compares this instance against its peers.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The diff report.</returns>
    public async Task<InstanceDiffReport> BuildAsync(CancellationToken cancellationToken)
    {
        DashboardSnapshot local = _snapshots.GetSnapshot();
        string localName = string.IsNullOrWhiteSpace(local.InstanceName) ? "this instance" : local.InstanceName;

        PeerResult[] peerResults = await Task.WhenAll(
            _peers.Select(peer => FetchPeerAsync(peer, cancellationToken))).ConfigureAwait(false);

        // Ordered sources: the local instance first, then each peer.
        var sources = new List<(string Name, DashboardSnapshot? Snapshot, string? Error)>
        {
            (localName, local, null),
        };
        sources.AddRange(peerResults.Select(p => (p.Name, p.Snapshot, p.Error)));

        var instances = sources
            .Select(source => new DiffInstance
            {
                Name = source.Name,
                Reachable = source.Snapshot is not null,
                Error = source.Error,
                EntryCount = source.Snapshot?.Entries.Count ?? 0,
            })
            .ToList();

        var reachable = sources
            .Where(source => source.Snapshot is not null)
            .Select(source => (source.Name, Snapshot: source.Snapshot!))
            .ToList();

        IReadOnlyList<DiffEntry> divergences = reachable.Count >= 2
            ? ComputeDivergences(reachable)
            : [];

        return new InstanceDiffReport
        {
            Instances = instances,
            Divergences = divergences,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static List<DiffEntry> ComputeDivergences(
        IReadOnlyList<(string Name, DashboardSnapshot Snapshot)> reachable)
    {
        // Index each instance's entries by (cache name, key).
        var index = reachable
            .Select(instance => (
                instance.Name,
                Map: instance.Snapshot.Entries
                    .GroupBy(entry => (entry.CacheName, entry.Key))
                    .ToDictionary(group => group.Key, group => group.First())))
            .ToList();

        var allKeys = new HashSet<(string CacheName, string Key)>();
        foreach ((_, Dictionary<(string, string), CacheEntrySnapshot> map) in index)
        {
            foreach ((string, string) key in map.Keys)
            {
                allKeys.Add(key);
            }
        }

        var result = new List<DiffEntry>();
        foreach ((string cacheName, string key) in allKeys
            .OrderBy(pair => pair.CacheName, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var cells = new List<DiffCell>(index.Count);
            int presentCount = 0;
            string? firstType = null;
            long? firstSize = null;
            bool sawFirst = false;
            bool attributesDiverge = false;

            foreach ((string name, Dictionary<(string, string), CacheEntrySnapshot> map) in index)
            {
                if (map.TryGetValue((cacheName, key), out CacheEntrySnapshot? entry))
                {
                    presentCount++;
                    cells.Add(new DiffCell
                    {
                        Instance = name,
                        Present = true,
                        ValueTypeName = entry.ValueTypeName,
                        SizeBytes = entry.EstimatedSizeBytes,
                    });

                    if (!sawFirst)
                    {
                        firstType = entry.ValueTypeName;
                        firstSize = entry.EstimatedSizeBytes;
                        sawFirst = true;
                    }
                    else if (!string.Equals(firstType, entry.ValueTypeName, StringComparison.Ordinal)
                        || firstSize != entry.EstimatedSizeBytes)
                    {
                        attributesDiverge = true;
                    }
                }
                else
                {
                    cells.Add(new DiffCell { Instance = name, Present = false });
                }
            }

            bool missing = presentCount > 0 && presentCount < index.Count;
            if (!missing && !attributesDiverge)
            {
                // Identical everywhere — not a divergence.
                continue;
            }

            result.Add(new DiffEntry
            {
                Key = key,
                CacheName = cacheName,
                Kind = missing ? DiffKind.Missing : DiffKind.Divergent,
                Cells = cells,
            });

            if (result.Count >= MaxDivergences)
            {
                break;
            }
        }

        return result;
    }

    private async Task<PeerResult> FetchPeerAsync(DashboardPeer peer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PeerTimeout);

        try
        {
            HttpClient client = _httpClientFactory.CreateClient();
            string url = peer.Url.ToString().TrimEnd('/') + "/api/snapshot";
            string json = await client.GetStringAsync(url, timeout.Token).ConfigureAwait(false);

            DashboardSnapshot? snapshot = JsonSerializer.Deserialize<DashboardSnapshot>(json, PeerJsonOptions);
            return snapshot is null
                ? new PeerResult(peer.Name, null, "The peer returned an empty snapshot.")
                : new PeerResult(peer.Name, snapshot, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PeerResult(peer.Name, null, "The peer did not respond within 5 seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new PeerResult(peer.Name, null, ex.Message);
        }
        catch (JsonException ex)
        {
            return new PeerResult(peer.Name, null, "The peer returned an unreadable snapshot: " + ex.Message);
        }
    }

    private sealed record PeerResult(string Name, DashboardSnapshot? Snapshot, string? Error);
}
