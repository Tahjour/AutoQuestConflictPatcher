using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestSnapshot
{
    public QuestSnapshot(
        QuestSourceContext context,
        IReadOnlyDictionary<string, object?> scalars,
        OrderedSectionSnapshot<object> textDisplayGlobals,
        OrderedSectionSnapshot<object> dialogConditions,
        OrderedSectionSnapshot<object> eventConditions,
        KeyedSectionSnapshot<QuestAlias> questAliases,
        KeyedSectionSnapshot<QuestFragmentAlias> vmadAliases,
        KeyedSectionSnapshot<ScriptEntry> vmadScripts,
        KeyedSectionSnapshot<QuestStage> stages,
        KeyedSectionSnapshot<QuestObjective> objectives,
        OrderedSectionSnapshot<QuestScriptFragment> fragmentSection,
        KeyedSectionSnapshot<QuestScriptFragment> fragments)
    {
        Context = context;
        Scalars = scalars;
        TextDisplayGlobals = textDisplayGlobals;
        DialogConditions = dialogConditions;
        EventConditions = eventConditions;
        QuestAliases = questAliases;
        VmadAliases = vmadAliases;
        VmadScripts = vmadScripts;
        Stages = stages;
        Objectives = objectives;
        FragmentSection = fragmentSection;
        Fragments = fragments;
    }

    public QuestSourceContext Context { get; }

    public IReadOnlyDictionary<string, object?> Scalars { get; }

    public OrderedSectionSnapshot<object> TextDisplayGlobals { get; }

    public OrderedSectionSnapshot<object> DialogConditions { get; }

    public OrderedSectionSnapshot<object> EventConditions { get; }

    public KeyedSectionSnapshot<QuestAlias> QuestAliases { get; }

    public KeyedSectionSnapshot<QuestFragmentAlias> VmadAliases { get; }

    public KeyedSectionSnapshot<ScriptEntry> VmadScripts { get; }

    public KeyedSectionSnapshot<QuestStage> Stages { get; }

    public KeyedSectionSnapshot<QuestObjective> Objectives { get; }

    public OrderedSectionSnapshot<QuestScriptFragment> FragmentSection { get; }

    public KeyedSectionSnapshot<QuestScriptFragment> Fragments { get; }
}

public sealed class KeyedSectionSnapshot<T> where T : class
{
    public KeyedSectionSnapshot(
        bool present,
        IReadOnlyDictionary<string, T> items,
        IReadOnlyDictionary<string, int> order,
        IReadOnlyList<string> orderedKeys)
    {
        Present = present;
        Items = items;
        Order = order;
        OrderedKeys = orderedKeys;
    }

    public bool Present { get; }

    public IReadOnlyDictionary<string, T> Items { get; }

    public IReadOnlyDictionary<string, int> Order { get; }

    public IReadOnlyList<string> OrderedKeys { get; }
}

public sealed class OrderedSectionSnapshot<T>
{
    public OrderedSectionSnapshot(bool present, IReadOnlyList<T> items)
    {
        Present = present;
        Items = items;
    }

    public bool Present { get; }

    public IReadOnlyList<T> Items { get; }
}
