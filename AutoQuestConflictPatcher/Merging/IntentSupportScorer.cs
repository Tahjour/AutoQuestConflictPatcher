using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed class IntentSupportScorer
{
    private sealed class RemovedState
    {
        public static readonly RemovedState Value = new();
    }

    private readonly OfficialMasterClassifier _officialMasters;

    public IntentSupportScorer(OfficialMasterClassifier officialMasters)
    {
        _officialMasters = officialMasters;
    }

    public ComponentSelection<T> Select<T>(
        ComponentKey key,
        IReadOnlyList<ComponentTimelineEntry<T>> timeline,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencyGraph,
        Func<T?, string>? fingerprint = null,
        Func<T?, int>? penaltySelector = null)
        where T : class
    {
        fingerprint ??= static value => value is null ? "<missing>" : QuestFingerprint.Exact(value);
        penaltySelector ??= static _ => 0;

        var candidates = new Dictionary<string, Candidate<T>>(StringComparer.Ordinal);
        var hpmoSources = new List<MergeSource>(timeline.Count);
        var sharedFingerprint = default(string);
        var sharedSet = false;
        var sharedMatch = true;
        var officialOriginFingerprint = timeline
            .Where(entry => entry.Exists && _officialMasters.IsOfficial(entry.Context.ModKey) && entry.Value is not null)
            .Select(entry => fingerprint(entry.Value))
            .FirstOrDefault();

        for (var index = 0; index < timeline.Count; index++)
        {
            var entry = timeline[index];
            var delta = GetDelta(deltas, key, index);
            var deltaKind = delta?.Kind ?? QuestDeltaKind.Unchanged;
            if (deltaKind == QuestDeltaKind.Removed && !HasMasterAncestorValue(timeline, index))
            {
                deltaKind = QuestDeltaKind.Unchanged;
            }

            var entryFingerprint = entry.Exists && entry.Value is not null
                ? fingerprint(entry.Value)
                : "<missing>";

            if (entry.Exists && entry.Value is not null)
            {
                hpmoSources.Add(new MergeSource(entry.Context, entry.Value, Exists: true, ParentExists: true));
                if (!sharedSet)
                {
                    sharedFingerprint = entryFingerprint;
                    sharedSet = true;
                }
                else if (!StringComparer.Ordinal.Equals(sharedFingerprint, entryFingerprint))
                {
                    sharedMatch = false;
                }
            }
            else if (deltaKind == QuestDeltaKind.Removed)
            {
                hpmoSources.Add(new MergeSource(entry.Context, RemovedState.Value, Exists: true, ParentExists: true));
                sharedMatch = false;
            }
            else
            {
                hpmoSources.Add(new MergeSource(entry.Context, null, Exists: false, ParentExists: false));
                sharedMatch = false;
            }

            var candidateFingerprint = deltaKind == QuestDeltaKind.Removed
                ? "<removed>"
                : entryFingerprint;
            if (!candidates.TryGetValue(candidateFingerprint, out var candidate))
            {
                candidate = new Candidate<T>(candidateFingerprint);
                candidates[candidateFingerprint] = candidate;
            }

            candidate.TotalOccurrences++;
            if (entry.Exists)
            {
                candidate.LastValue = entry.Value;
            }

            candidate.LastContext = entry.Context;
            candidate.LastTimelineIndex = index;
            candidate.LastExists = entry.Exists;

            if (deltaKind is QuestDeltaKind.Added or QuestDeltaKind.Modified or QuestDeltaKind.Removed)
            {
                candidate.MeaningfulOccurrences++;
                candidate.Score += 3;
                candidate.Score += index;
                if (candidate.MeaningfulOccurrences > 1)
                {
                    candidate.Score += 1;
                }

                if (HasMasterLinkedPriorIntent(candidate, entry.Context))
                {
                    candidate.Score += 2;
                }

                if (HasDependencySupport(key, dependencyGraph, deltas, index))
                {
                    candidate.Score += 2;
                }

                candidate.HighestMeaningfulSourceIndex = index;
                candidate.LastMeaningfulModKey = entry.Context.ModKey;
            }
        }

        if (officialOriginFingerprint is not null && !(sharedSet && sharedMatch))
        {
            if (candidates.TryGetValue(officialOriginFingerprint, out var officialCandidate) && officialCandidate.MeaningfulOccurrences > 0)
            {
                officialCandidate.Score -= 5 * officialCandidate.MeaningfulOccurrences;
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"No candidates were available for {key}.");
        }

        foreach (var candidate in candidates.Values)
        {
            candidate.Score -= penaltySelector(candidate.LastValue);
        }

        var hpmo = HpmoSelector.Select(
            hpmoSources,
            _officialMasters,
            value => ReferenceEquals(value, RemovedState.Value)
                ? "<removed>"
                : fingerprint(value as T));

        Candidate<T>? best;
        if (sharedSet && sharedMatch && sharedFingerprint is not null)
        {
            best = candidates[sharedFingerprint];
            return new ComponentSelection<T>(
                best.LastValue,
                Exists: true,
                best.LastContext.ModKey,
                MergeConfidence.Medium,
                best.Score,
                "Shared carry-forward state across all sources.");
        }

        best = candidates.Values
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.MeaningfulOccurrences)
            .ThenByDescending(static candidate => candidate.TotalOccurrences)
            .ThenByDescending(static candidate => candidate.HighestMeaningfulSourceIndex)
            .ThenByDescending(static candidate => candidate.LastTimelineIndex)
            .ThenBy(candidate =>
            {
                if (hpmo is null)
                {
                    return 1;
                }

                return StringComparer.Ordinal.Equals(candidate.Fingerprint, hpmo.Fingerprint) ? 0 : 1;
            })
            .ThenBy(static candidate => candidate.Fingerprint, StringComparer.Ordinal)
            .First();

        var runnerUp = candidates.Values
            .Where(candidate => !ReferenceEquals(candidate, best))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.MeaningfulOccurrences)
            .FirstOrDefault();

        var confidence = GetConfidence(best, runnerUp);
        var exists = !StringComparer.Ordinal.Equals(best.Fingerprint, "<removed>");
        return new ComponentSelection<T>(
            exists ? best.LastValue : null,
            exists,
            best.LastContext.ModKey,
            confidence,
            best.Score,
            exists
                ? $"Selected {best.Fingerprint} from {best.LastContext.ModKey}."
                : $"Selected removal from {best.LastContext.ModKey}.");
    }

    private static bool HasDependencySupport(
        ComponentKey key,
        QuestDependencyGraph dependencyGraph,
        IReadOnlyList<QuestDelta> deltas,
        int timelineIndex)
    {
        if (timelineIndex < 0 || timelineIndex >= deltas.Count)
        {
            return false;
        }

        var delta = deltas[timelineIndex];
        foreach (var related in dependencyGraph.GetRelated(key))
        {
            if (delta.TryGet(related, out var relatedDelta) && relatedDelta.Kind is not QuestDeltaKind.Unchanged)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMasterAncestorValue<T>(
        IReadOnlyList<ComponentTimelineEntry<T>> timeline,
        int index)
        where T : class
    {
        if (index <= 0)
        {
            return false;
        }

        var current = timeline[index];
        for (var ancestorIndex = index - 1; ancestorIndex >= 0; ancestorIndex--)
        {
            var ancestor = timeline[ancestorIndex];
            if (!ancestor.Exists || ancestor.Value is null)
            {
                continue;
            }

            if (current.Context.Masters.Contains(ancestor.Context.ModKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMasterLinkedPriorIntent<T>(Candidate<T> candidate, QuestSourceContext context)
        where T : class
    {
        if (!candidate.HasMeaningfulSource)
        {
            return false;
        }

        return context.Masters.Contains(candidate.LastMeaningfulModKey);
    }

    private static ComponentDelta? GetDelta(
        IReadOnlyList<QuestDelta> deltas,
        ComponentKey key,
        int timelineIndex)
    {
        if (timelineIndex < 0 || timelineIndex >= deltas.Count)
        {
            return null;
        }

        return deltas[timelineIndex].TryGet(key, out var delta) ? delta : null;
    }

    private static MergeConfidence GetConfidence<T>(Candidate<T> best, Candidate<T>? runnerUp)
        where T : class
    {
        if (best.Score <= 0)
        {
            return MergeConfidence.Low;
        }

        if (runnerUp is null)
        {
            return MergeConfidence.High;
        }

        var gap = best.Score - runnerUp.Score;
        if (gap >= 3)
        {
            return MergeConfidence.High;
        }

        if (gap >= 1)
        {
            return MergeConfidence.Medium;
        }

        return MergeConfidence.Low;
    }

    public sealed record ComponentTimelineEntry<T>(
        QuestSourceContext Context,
        T? Value,
        bool Exists,
        int? OrderIndex) where T : class;

    private sealed class Candidate<T> where T : class
    {
        public Candidate(string fingerprint)
        {
            Fingerprint = fingerprint;
        }

        public string Fingerprint { get; }

        public int Score { get; set; }

        public int TotalOccurrences { get; set; }

        public int MeaningfulOccurrences { get; set; }

        public T? LastValue { get; set; }

        public QuestSourceContext LastContext { get; set; } = null!;

        public int LastTimelineIndex { get; set; } = -1;

        public bool LastExists { get; set; }

        public int HighestMeaningfulSourceIndex { get; set; } = int.MinValue;

        public bool HasMeaningfulSource => HighestMeaningfulSourceIndex != int.MinValue;

        public ModKey LastMeaningfulModKey { get; set; }
    }
}
