namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestValidationResult
{
    public static QuestValidationResult Success { get; } = new([], []);

    public QuestValidationResult(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool IsValid => Errors.Count == 0;
}
