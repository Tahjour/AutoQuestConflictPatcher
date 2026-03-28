using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed record HpmoSelection(
    object? Value,
    ModKey SelectedFrom,
    int SelectedSourceIndex,
    string Fingerprint,
    int TotalOccurrences,
    int MeaningfulOccurrences,
    ModKey? HighestPriorityMeaningfulSource,
    ModKey HighestPrioritySource);

public sealed record HpmoGroup(
    string Fingerprint,
    int TotalOccurrences,
    int MeaningfulOccurrences,
    HpmoSelection? HighestMeaningfulSelection,
    HpmoSelection HighestSelection,
    int FirstSourceIndex);

public static class HpmoSelector
{
    public static IReadOnlyList<HpmoGroup> Analyze(
        IReadOnlyList<MergeSource> sources,
        OfficialMasterClassifier officialMasters,
        Func<object?, string>? fingerprint = null)
    {
        fingerprint ??= QuestFingerprint.Exact;

        var occurrences = new List<Occurrence>();
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (!source.Exists)
            {
                continue;
            }

            var key = fingerprint(source.Value);
            var parentIndex = GetNearestParentSourceIndex(sources, sourceIndex);
            var parentFingerprint = parentIndex >= 0
                ? fingerprint(sources[parentIndex].Exists ? sources[parentIndex].Value : null)
                : null;
            var previousFingerprint = occurrences.Count > 0
                ? occurrences[^1].Fingerprint
                : null;
            var priorMatches = occurrences
                .Where(occurrence => StringComparer.Ordinal.Equals(occurrence.Fingerprint, key))
                .ToArray();

            occurrences.Add(new Occurrence(
                sourceIndex,
                source,
                key,
                IsMeaningfulOccurrence(
                    source,
                    key,
                    parentFingerprint,
                    previousFingerprint,
                    priorMatches,
                    officialMasters)));
        }

        if (occurrences.Count == 0)
        {
            return [];
        }

        return occurrences
            .GroupBy(static occurrence => occurrence.Fingerprint, StringComparer.Ordinal)
            .Select(group => BuildGroup(group.Key, group))
            .ToArray();
    }

    public static HpmoSelection? Select(
        IReadOnlyList<MergeSource> sources,
        OfficialMasterClassifier officialMasters,
        Func<object?, string>? fingerprint = null)
    {
        var bestGroup = Analyze(sources, officialMasters, fingerprint)
            .OrderByDescending(static group => group.MeaningfulOccurrences)
            .ThenByDescending(static group => group.TotalOccurrences)
            .ThenByDescending(static group => group.HighestMeaningfulSelection?.SelectedSourceIndex ?? int.MinValue)
            .ThenByDescending(static group => group.HighestSelection.SelectedSourceIndex)
            .ThenByDescending(static group => group.FirstSourceIndex)
            .ThenBy(static group => group.Fingerprint, StringComparer.Ordinal)
            .FirstOrDefault();

        return bestGroup?.HighestMeaningfulSelection ?? bestGroup?.HighestSelection;
    }

    private static bool IsMeaningfulOccurrence(
        MergeSource source,
        string fingerprint,
        string? parentFingerprint,
        string? previousFingerprint,
        IReadOnlyList<Occurrence> priorMatches,
        OfficialMasterClassifier officialMasters)
    {
        if (parentFingerprint is null)
        {
            return false;
        }

        var differsFromParent = !StringComparer.Ordinal.Equals(fingerprint, parentFingerprint);
        var differsFromPrevious = previousFingerprint is not null
            && !StringComparer.Ordinal.Equals(fingerprint, previousFingerprint);
        var hasPriorMatch = priorMatches.Count > 0;
        var hasPriorMeaningfulMatch = priorMatches.Any(static match => match.IsMeaningful);
        var hasPriorMeaningfulMasterMatch = priorMatches.Any(match =>
            match.IsMeaningful
            && source.Context.Masters.Contains(match.Source.Context.ModKey));

        if (differsFromParent)
        {
            if (StringComparer.Ordinal.Equals(parentFingerprint, "<missing>"))
            {
                return true;
            }

            if (!hasPriorMatch)
            {
                return true;
            }

            return hasPriorMeaningfulMatch;
        }

        return differsFromPrevious && hasPriorMeaningfulMasterMatch;
    }

    private static HpmoGroup BuildGroup(string fingerprint, IEnumerable<Occurrence> occurrences)
    {
        var ordered = occurrences
            .OrderBy(static occurrence => occurrence.SourceIndex)
            .ToArray();
        var meaningful = ordered
            .Where(static occurrence => occurrence.IsMeaningful)
            .ToArray();

        var highestMeaningful = meaningful.Length > 0
            ? ToSelection(fingerprint, ordered.Length, meaningful.Length, meaningful[^1], ordered[^1])
            : null;
        var highest = ToSelection(fingerprint, ordered.Length, meaningful.Length, ordered[^1], ordered[^1]);

        return new HpmoGroup(
            fingerprint,
            ordered.Length,
            meaningful.Length,
            highestMeaningful,
            highest,
            ordered[0].SourceIndex);
    }

    private static HpmoSelection ToSelection(
        string fingerprint,
        int totalOccurrences,
        int meaningfulOccurrences,
        Occurrence selectedOccurrence,
        Occurrence highestOccurrence)
    {
        return new HpmoSelection(
            selectedOccurrence.Source.Value,
            selectedOccurrence.Source.Context.ModKey,
            selectedOccurrence.SourceIndex,
            fingerprint,
            totalOccurrences,
            meaningfulOccurrences,
            meaningfulOccurrences > 0 ? selectedOccurrence.Source.Context.ModKey : default(ModKey?),
            highestOccurrence.Source.Context.ModKey);
    }

    private static int GetNearestParentSourceIndex(IReadOnlyList<MergeSource> sources, int sourceIndex)
    {
        var source = sources[sourceIndex];
        for (var index = sourceIndex - 1; index >= 0; index--)
        {
            if (source.Context.Masters.Contains(sources[index].Context.ModKey))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record Occurrence(
        int SourceIndex,
        MergeSource Source,
        string Fingerprint,
        bool IsMeaningful);

}
