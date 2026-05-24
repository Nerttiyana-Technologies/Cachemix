using System.Text.Json;

namespace Cachemix.Tool;

/// <summary>
/// The <c>diff</c> command: fetches the snapshot from two or more running
/// Cachemix dashboards and reports cache keys that are missing on some
/// instances or whose type/size diverges across them.
/// </summary>
internal static class SnapshotDiff
{
    private const int MaxRows = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs the diff command.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> RunAsync(CliArgs cli)
    {
        IReadOnlyList<string> urls = cli.Positionals;
        if (urls.Count < 2)
        {
            throw new CliUsageException("diff needs at least two dashboard URLs to compare.");
        }

        InstanceSnapshot[] snapshots = await Task.WhenAll(urls.Select(FetchAsync)).ConfigureAwait(false);
        DedupeLabels(snapshots);

        Output.Heading("Instances");
        foreach (InstanceSnapshot snapshot in snapshots)
        {
            Output.Info(
                snapshot.Label,
                snapshot.Reachable
                    ? $"{snapshot.Entries.Count} keys  ({snapshot.Url})"
                    : $"unreachable - {snapshot.Error}  ({snapshot.Url})");
        }

        Output.Plain(string.Empty);

        InstanceSnapshot[] reachable = snapshots.Where(s => s.Reachable).ToArray();
        if (reachable.Length < 2)
        {
            Output.Error("Need at least two reachable dashboards to compare.");
            return 1;
        }

        List<DiffRow> rows = Compute(reachable);
        if (rows.Count == 0)
        {
            Output.Plain("All reachable instances hold identical cache state.");
            return 0;
        }

        var headers = new List<string> { "KEY", "CACHE", "KIND" };
        headers.AddRange(reachable.Select(r => r.Label));

        var tableRows = new List<string[]>(rows.Count);
        foreach (DiffRow row in rows)
        {
            var cells = new List<string>(headers.Count)
            {
                Output.Truncate(row.Key, 48),
                row.CacheName,
                row.Kind,
            };
            cells.AddRange(row.Cells);
            tableRows.Add(cells.ToArray());
        }

        Output.Table(headers, tableRows);
        Output.Plain(string.Empty);
        Output.Plain($"{rows.Count} diverging key(s) across {reachable.Length} reachable instance(s).");
        return 0;
    }

    private static async Task<InstanceSnapshot> FetchAsync(string url)
    {
        string snapshotUrl = url.TrimEnd('/') + "/api/snapshot";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        try
        {
            string json = await http.GetStringAsync(snapshotUrl).ConfigureAwait(false);
            SnapshotDto? dto = JsonSerializer.Deserialize<SnapshotDto>(json, JsonOptions);
            return dto is null
                ? InstanceSnapshot.Unreachable(url, "the dashboard returned an empty snapshot")
                : InstanceSnapshot.From(url, dto);
        }
        catch (HttpRequestException ex)
        {
            return InstanceSnapshot.Unreachable(url, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return InstanceSnapshot.Unreachable(url, "the request timed out");
        }
        catch (JsonException ex)
        {
            return InstanceSnapshot.Unreachable(url, "unreadable snapshot: " + ex.Message);
        }
    }

    private static List<DiffRow> Compute(IReadOnlyList<InstanceSnapshot> reachable)
    {
        var allKeys = new HashSet<(string Cache, string Key)>();
        foreach (InstanceSnapshot snapshot in reachable)
        {
            foreach ((string Cache, string Key) key in snapshot.Entries.Keys)
            {
                allKeys.Add(key);
            }
        }

        var rows = new List<DiffRow>();
        foreach ((string cache, string key) in allKeys
            .OrderBy(pair => pair.Cache, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var cells = new List<string>(reachable.Count);
            int present = 0;
            string? baseType = null;
            long? baseSize = null;
            bool sawBase = false;
            bool diverge = false;

            foreach (InstanceSnapshot snapshot in reachable)
            {
                if (snapshot.Entries.TryGetValue((cache, key), out EntryDto? entry))
                {
                    present++;
                    cells.Add(ShortType(entry.ValueTypeName) + " " + Output.FormatBytes(entry.EstimatedSizeBytes));

                    if (!sawBase)
                    {
                        baseType = entry.ValueTypeName;
                        baseSize = entry.EstimatedSizeBytes;
                        sawBase = true;
                    }
                    else if (!string.Equals(baseType, entry.ValueTypeName, StringComparison.Ordinal)
                        || baseSize != entry.EstimatedSizeBytes)
                    {
                        diverge = true;
                    }
                }
                else
                {
                    cells.Add("absent");
                }
            }

            bool missing = present > 0 && present < reachable.Count;
            if (!missing && !diverge)
            {
                continue;
            }

            rows.Add(new DiffRow(key, cache, missing ? "missing" : "divergent", cells));
            if (rows.Count >= MaxRows)
            {
                break;
            }
        }

        return rows;
    }

    private static void DedupeLabels(IReadOnlyList<InstanceSnapshot> snapshots)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (InstanceSnapshot snapshot in snapshots)
        {
            if (seen.TryGetValue(snapshot.Label, out int count))
            {
                seen[snapshot.Label] = count + 1;
                snapshot.Label = $"{snapshot.Label}#{count + 1}";
            }
            else
            {
                seen[snapshot.Label] = 1;
            }
        }
    }

    private static string ShortType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return "-";
        }

        string text = typeName;
        foreach (char terminator in new[] { ',', '`', '[' })
        {
            int index = text.IndexOf(terminator, StringComparison.Ordinal);
            if (index >= 0)
            {
                text = text[..index];
            }
        }

        int dot = text.LastIndexOf('.');
        if (dot >= 0)
        {
            text = text[(dot + 1)..];
        }

        return text.Length == 0 ? "-" : text;
    }

    private sealed record DiffRow(string Key, string CacheName, string Kind, IReadOnlyList<string> Cells);

    private sealed record SnapshotDto(string? InstanceName, IReadOnlyList<EntryDto>? Entries);

    private sealed record EntryDto(
        string? Key,
        string? CacheName,
        string? ValueTypeName,
        long? EstimatedSizeBytes);

    private sealed class InstanceSnapshot
    {
        public required string Url { get; init; }

        public required string Label { get; set; }

        public required bool Reachable { get; init; }

        public string? Error { get; init; }

        public required IReadOnlyDictionary<(string Cache, string Key), EntryDto> Entries { get; init; }

        public static InstanceSnapshot From(string url, SnapshotDto dto)
        {
            var entries = new Dictionary<(string, string), EntryDto>();
            foreach (EntryDto entry in dto.Entries ?? [])
            {
                if (!string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.CacheName))
                {
                    entries[(entry.CacheName, entry.Key)] = entry;
                }
            }

            return new InstanceSnapshot
            {
                Url = url,
                Label = string.IsNullOrWhiteSpace(dto.InstanceName) ? HostOf(url) : dto.InstanceName,
                Reachable = true,
                Entries = entries,
            };
        }

        public static InstanceSnapshot Unreachable(string url, string error) => new()
        {
            Url = url,
            Label = HostOf(url),
            Reachable = false,
            Error = error,
            Entries = new Dictionary<(string, string), EntryDto>(),
        };

        private static string HostOf(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ? parsed.Authority : url;
    }
}
