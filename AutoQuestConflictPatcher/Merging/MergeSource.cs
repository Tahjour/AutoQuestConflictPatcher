namespace AutoQuestConflictPatcher.Merging;

public readonly record struct MergeSource(
    QuestSourceContext Context,
    object? Value,
    bool Exists,
    bool ParentExists = true);
