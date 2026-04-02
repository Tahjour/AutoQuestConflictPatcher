namespace AutoQuestConflictPatcher.Merging;

public readonly record struct ComponentKey(string Kind, string Key)
{
    public override string ToString()
    {
        return $"{Kind}:{Key}";
    }
}
