using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed record HpuSelection(object? Value, ModKey SelectedFrom, int HistoryIndex, string Fingerprint);

public static class HpuSelector
{
    public static HpuSelection? Select(
        IReadOnlyList<MergeSource> sources,
        IReadOnlySet<ModKey> leafMods,
        Func<object?, string>? fingerprint = null)
    {
        fingerprint ??= QuestFingerprint.Exact;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (!source.Exists)
            {
                continue;
            }

            var key = fingerprint(source.Value);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        HpuSelection? fallback = null;
        var nextHistoryIndex = 0;
        var history = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = sources.Count - 1; index >= 0; index--)
        {
            var source = sources[index];
            if (!source.Exists)
            {
                continue;
            }

            var key = fingerprint(source.Value);
            if (!history.TryGetValue(key, out var historyIndex))
            {
                historyIndex = nextHistoryIndex++;
                history[key] = historyIndex;
            }

            if (!leafMods.Contains(source.Context.ModKey))
            {
                continue;
            }

            fallback ??= new HpuSelection(source.Value, source.Context.ModKey, historyIndex, key);
            if (counts[key] == 1)
            {
                return new HpuSelection(source.Value, source.Context.ModKey, historyIndex, key);
            }
        }

        if (fallback is not null)
        {
            return fallback;
        }

        for (var index = sources.Count - 1; index >= 0; index--)
        {
            var source = sources[index];
            if (!source.Exists)
            {
                continue;
            }

            var key = fingerprint(source.Value);
            if (!history.TryGetValue(key, out var historyIndex))
            {
                historyIndex = nextHistoryIndex++;
                history[key] = historyIndex;
            }

            return new HpuSelection(source.Value, source.Context.ModKey, historyIndex, key);
        }

        return null;
    }
}
