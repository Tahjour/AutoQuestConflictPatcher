using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestSourceContext
{
    public QuestSourceContext(
        ModKey modKey,
        IQuestGetter quest,
        IReadOnlyCollection<ModKey> masters,
        int loadOrderIndex)
    {
        ModKey = modKey;
        Quest = quest;
        Masters = masters;
        LoadOrderIndex = loadOrderIndex;
    }

    public ModKey ModKey { get; }

    public IQuestGetter Quest { get; }

    public IReadOnlyCollection<ModKey> Masters { get; }

    public int LoadOrderIndex { get; }
}
