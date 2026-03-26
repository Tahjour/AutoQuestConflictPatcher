using AutoQuestConflictPatcher.Configuration;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestConflict
{
    public QuestConflict(
        IReadOnlyList<QuestSourceContext> contextsLowToHigh,
        IReadOnlySet<ModKey> leafMods,
        QuestMergeSection winnerOnlySections)
    {
        ContextsLowToHigh = contextsLowToHigh;
        LeafMods = leafMods;
        WinnerOnlySections = winnerOnlySections;
    }

    public IReadOnlyList<QuestSourceContext> ContextsLowToHigh { get; }

    public IReadOnlySet<ModKey> LeafMods { get; }

    public QuestMergeSection WinnerOnlySections { get; }

    public QuestSourceContext OriginContext => ContextsLowToHigh[0];

    public QuestSourceContext WinningContext => ContextsLowToHigh[^1];

    public IQuestGetter OriginQuest => OriginContext.Quest;

    public IQuestGetter WinningQuest => WinningContext.Quest;

    public FormKey FormKey => WinningQuest.FormKey;

    public string DisplayName => string.IsNullOrWhiteSpace(WinningQuest.EditorID)
        ? WinningQuest.FormKey.ToString()
        : $"{WinningQuest.EditorID} [{WinningQuest.FormKey}]";
}
