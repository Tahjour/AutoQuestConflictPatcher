using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Configuration;

public sealed class AutoQuestConflictPatcherSettings
{
    public MergePosture MergePosture { get; set; } = MergePosture.Aggressive;

    public bool EmitReport { get; set; } = true;

    public HashSet<string> IgnoredQuests { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<QuestSectionOverride> WinnerOnlySections { get; set; } = [];

    public bool IsIgnored(FormKey formKey, string? editorId)
    {
        return MatchesAny(IgnoredQuests, formKey, editorId);
    }

    public QuestMergeSection GetWinnerOnlySections(FormKey formKey, string? editorId)
    {
        QuestMergeSection sections = QuestMergeSection.None;
        foreach (var rule in WinnerOnlySections)
        {
            if (Matches(rule.Quest, formKey, editorId))
            {
                sections |= rule.Sections;
            }
        }

        return sections;
    }

    private static bool MatchesAny(IEnumerable<string> candidates, FormKey formKey, string? editorId)
    {
        foreach (var candidate in candidates)
        {
            if (Matches(candidate, formKey, editorId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string candidate, FormKey formKey, string? editorId)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return candidate.Equals(formKey.ToString(), StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(editorId)
                && candidate.Equals(editorId, StringComparison.OrdinalIgnoreCase));
    }
}
