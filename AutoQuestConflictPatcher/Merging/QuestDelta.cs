using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public enum QuestDeltaKind
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
}

public sealed record ComponentDelta(
    ComponentKey Component,
    QuestDeltaKind Kind,
    string PreviousFingerprint,
    string CurrentFingerprint,
    ModKey ModKey,
    int LoadOrderIndex);

public sealed class QuestDelta
{
    public QuestDelta(
        QuestSourceContext context,
        IReadOnlyDictionary<ComponentKey, ComponentDelta> entries)
    {
        Context = context;
        Entries = entries;
    }

    public QuestSourceContext Context { get; }

    public IReadOnlyDictionary<ComponentKey, ComponentDelta> Entries { get; }

    public bool TryGet(ComponentKey key, out ComponentDelta delta)
    {
        return Entries.TryGetValue(key, out delta!);
    }
}
