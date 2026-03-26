namespace AutoQuestConflictPatcher.Configuration;

public sealed class QuestSectionOverride
{
    public string Quest { get; set; } = string.Empty;

    public QuestMergeSection Sections { get; set; } = QuestMergeSection.None;
}
