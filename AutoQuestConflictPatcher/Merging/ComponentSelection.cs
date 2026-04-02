using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed record ComponentSelection<T>(
    T? Value,
    bool Exists,
    ModKey SelectedFrom,
    MergeConfidence Confidence,
    int Score,
    string Reason) where T : class;
