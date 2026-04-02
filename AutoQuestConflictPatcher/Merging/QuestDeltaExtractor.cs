using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestDeltaExtractor
{
    public IReadOnlyList<QuestDelta> Build(IReadOnlyList<QuestSnapshot> snapshots)
    {
        var deltas = new List<QuestDelta>(snapshots.Count);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var current = snapshots[index];
            var previous = index > 0 ? snapshots[index - 1] : null;
            deltas.Add(new QuestDelta(current.Context, BuildEntries(previous, current)));
        }

        return deltas;
    }

    private static IReadOnlyDictionary<ComponentKey, ComponentDelta> BuildEntries(
        QuestSnapshot? previous,
        QuestSnapshot current)
    {
        var entries = new Dictionary<ComponentKey, ComponentDelta>();
        BuildScalarEntries(previous, current, entries);
        BuildKeyedEntries("QuestAlias", previous?.QuestAliases, current.QuestAliases, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("VmadAlias", previous?.VmadAliases, current.VmadAliases, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("VmadScript", previous?.VmadScripts, current.VmadScripts, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Stage", previous?.Stages, current.Stages, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Objective", previous?.Objectives, current.Objectives, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Fragment", previous?.Fragments, current.Fragments, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("DialogConditions", previous?.DialogConditions, current.DialogConditions, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("EventConditions", previous?.EventConditions, current.EventConditions, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("TextDisplayGlobals", previous?.TextDisplayGlobals, current.TextDisplayGlobals, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        return entries;
    }

    private static void BuildScalarEntries(
        QuestSnapshot? previous,
        QuestSnapshot current,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        foreach (var key in current.Scalars.Keys.Union(previous?.Scalars.Keys ?? [], StringComparer.Ordinal))
        {
            var previousValue = previous is not null && previous.Scalars.TryGetValue(key, out var prior)
                ? prior
                : null;
            var currentValue = current.Scalars.TryGetValue(key, out var currentScalar)
                ? currentScalar
                : null;
            var previousFingerprint = QuestFingerprint.Exact(previousValue);
            var currentFingerprint = QuestFingerprint.Exact(currentValue);
            var kind = GetKind(previous is not null && previous.Scalars.ContainsKey(key), current.Scalars.ContainsKey(key), previousFingerprint, currentFingerprint);
            entries[new ComponentKey("Scalar", key)] = new ComponentDelta(
                new ComponentKey("Scalar", key),
                kind,
                previousFingerprint,
                currentFingerprint,
                current.Context.ModKey,
                current.Context.LoadOrderIndex);
        }
    }

    private static void BuildKeyedEntries<T>(
        string kind,
        KeyedSectionSnapshot<T>? previous,
        KeyedSectionSnapshot<T> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
        where T : class
    {
        if (!current.Present)
        {
            return;
        }

        foreach (var key in current.Items.Keys.Union(previous?.Items.Keys ?? [], StringComparer.Ordinal))
        {
            T? previousValue = null;
            var previousExists = previous is not null && previous.Items.TryGetValue(key, out previousValue);
            T? currentValue = null;
            var currentExists = current.Items.TryGetValue(key, out currentValue);
            var previousFingerprint = previousExists ? QuestFingerprint.Exact(previousValue) : "<missing>";
            var currentFingerprint = currentExists ? QuestFingerprint.Exact(currentValue) : "<missing>";
            entries[new ComponentKey(kind, key)] = new ComponentDelta(
                new ComponentKey(kind, key),
                GetKind(previousExists, currentExists, previousFingerprint, currentFingerprint),
                previousFingerprint,
                currentFingerprint,
                modKey,
                loadOrderIndex);
        }
    }

    private static void BuildOrderedEntry(
        string kind,
        OrderedSectionSnapshot<object>? previous,
        OrderedSectionSnapshot<object> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        if (!current.Present)
        {
            return;
        }

        var previousFingerprint = previous is not null && previous.Present
            ? QuestFingerprint.Exact(previous.Items)
            : "<missing>";
        var currentFingerprint = QuestFingerprint.Exact(current.Items);
        entries[new ComponentKey(kind, kind)] = new ComponentDelta(
            new ComponentKey(kind, kind),
            GetKind(previous is not null && previous.Present, current.Present, previousFingerprint, currentFingerprint),
            previousFingerprint,
            currentFingerprint,
            modKey,
            loadOrderIndex);
    }

    private static QuestDeltaKind GetKind(
        bool previousExists,
        bool currentExists,
        string previousFingerprint,
        string currentFingerprint)
    {
        if (!previousExists && currentExists)
        {
            return QuestDeltaKind.Added;
        }

        if (previousExists && !currentExists)
        {
            return QuestDeltaKind.Removed;
        }

        return StringComparer.Ordinal.Equals(previousFingerprint, currentFingerprint)
            ? QuestDeltaKind.Unchanged
            : QuestDeltaKind.Modified;
    }
}
