namespace AutoQuestConflictPatcher.Configuration;

[Flags]
public enum QuestMergeSection
{
    None = 0,
    TopLevel = 1 << 0,
    Aliases = 1 << 1,
    Stages = 1 << 2,
    Objectives = 1 << 3,
    Vmad = 1 << 4,
    Conditions = 1 << 5,
    All = TopLevel | Aliases | Stages | Objectives | Vmad | Conditions,
}
