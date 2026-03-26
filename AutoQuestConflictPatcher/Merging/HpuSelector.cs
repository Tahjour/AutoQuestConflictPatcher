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

        var history = new Dictionary<string, int>(StringComparer.Ordinal);
        HpuSelection? selection = null;
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (!source.Exists)
            {
                continue;
            }

            var key = fingerprint(source.Value);
            if (!history.TryGetValue(key, out var historyIndex))
            {
                historyIndex = history.Count;
                history[key] = historyIndex;
                selection = new HpuSelection(source.Value, source.Context.ModKey, historyIndex, key);
            }
        }

        return selection;
    }
}
