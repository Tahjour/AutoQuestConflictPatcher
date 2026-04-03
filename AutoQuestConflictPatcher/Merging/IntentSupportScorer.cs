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

        string? previousExistingFingerprint = null;
        var volatility = 0;

        for (var index = 0; index < timeline.Count; index++)
        {
            var entry = timeline[index];
            var delta = GetDelta(deltas, key, index);
            var deltaKind = delta?.Kind ?? QuestDeltaKind.Unchanged;
            if (deltaKind == QuestDeltaKind.Removed && !entry.ParentExists)
            {
                deltaKind = QuestDeltaKind.Unchanged;
            }

            if (deltaKind == QuestDeltaKind.Removed && !HasMasterAncestorValue(timeline, index))
            {
                deltaKind = QuestDeltaKind.Unchanged;
            }

            var entryFingerprint = entry.Exists && entry.Value is not null
                ? fingerprint(entry.Value)
                : "<missing>";

            var candidateFingerprint = deltaKind == QuestDeltaKind.Removed && entry.ParentExists
                ? "<removed>"
                : entryFingerprint;
            var dependencySupport = HasDependencySupport(key, dependencyGraph, deltas, index);
            var priorFingerprint = previousExistingFingerprint;
            var dependentAncestorConflict = HasDependentAncestorConflict(
                timeline,
                index,
                deltaKind,
                candidateFingerprint,
                fingerprint);

            if (entry.Exists && entry.Value is not null)
            {
                hpmoSources.Add(new MergeSource(entry.Context, entry.Value, Exists: true, ParentExists: entry.ParentExists));
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
            else if (deltaKind == QuestDeltaKind.Removed && entry.ParentExists)
            {
                hpmoSources.Add(new MergeSource(entry.Context, RemovedState.Value, Exists: true, ParentExists: true));
                sharedMatch = false;
            }
            else
            {
                hpmoSources.Add(new MergeSource(entry.Context, null, Exists: false, ParentExists: entry.ParentExists));
                sharedMatch = false;
            }

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

            if (priorFingerprint is not null
                && (entry.Exists || deltaKind == QuestDeltaKind.Removed)
                && !StringComparer.Ordinal.Equals(priorFingerprint, candidateFingerprint))
            {
                volatility++;
            }

            if (entry.Exists || deltaKind == QuestDeltaKind.Removed)
            {
                previousExistingFingerprint = candidateFingerprint;
            }

            var evidence = ClassifyEvidence(candidate, deltaKind, entry.ParentExists, dependencySupport, candidateFingerprint, priorFingerprint);
            candidate.LastEvidence = evidence;

            if (!IsMeaningful(evidence))
            {
                continue;
            }

            candidate.MeaningfulOccurrences++;
            candidate.Score += GetEvidenceWeight(evidence);
            candidate.Score += index;
            candidate.MeaningfulSupporters.Add(entry.Context.ModKey);

            if (HasMasterLinkedPriorIntent(candidate, entry.Context))
            {
                candidate.Score += 2;
            }

            if (dependencySupport)
            {
                candidate.BundleSupportCount++;
                candidate.Score += 2;
            }

            if (dependentAncestorConflict)
            {
                candidate.DependentConflictCount++;
                candidate.Score += evidence == HpmoEvidenceKind.ExplicitRemoval ? 6 : 4;
            }

            if (evidence == HpmoEvidenceKind.StructuralReassertion)
            {
                candidate.ReassertionCount++;
                candidate.Score += 1;
            }

            if (evidence == HpmoEvidenceKind.ExplicitRemoval)
            {
                candidate.ExplicitRemovalCount++;
            }

            candidate.HighestMeaningfulSourceIndex = index;
            candidate.LastMeaningfulModKey = entry.Context.ModKey;
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

        var hpmoGroups = HpmoSelector.Analyze(
            hpmoSources,
            _officialMasters,
            value => ReferenceEquals(value, RemovedState.Value)
                ? "<removed>"
                : fingerprint(value as T));
        var hpmoByFingerprint = hpmoGroups.ToDictionary(static group => group.Fingerprint, StringComparer.Ordinal);
        var hpmo = hpmoGroups
            .OrderByDescending(static group => group.MeaningfulOccurrences)
            .ThenByDescending(static group => group.BranchSupportCount)
            .ThenByDescending(static group => group.ReassertionCount)
            .ThenByDescending(static group => group.TotalOccurrences)
            .ThenBy(static group => group.VolatilityPenalty)
            .ThenByDescending(static group => group.HighestMeaningfulSelection?.SelectedSourceIndex ?? int.MinValue)
            .ThenByDescending(static group => group.HighestSelection.SelectedSourceIndex)
            .ThenBy(static group => group.Fingerprint, StringComparer.Ordinal)
            .FirstOrDefault();

        foreach (var candidate in candidates.Values)
        {
            if (hpmoByFingerprint.TryGetValue(candidate.Fingerprint, out var group))
            {
                candidate.Score += group.BranchSupportCount;
                candidate.Score += group.ReassertionCount;
                candidate.Score -= group.VolatilityPenalty;
            }

            candidate.ValidatorPenalty = penaltySelector(candidate.LastValue);
            candidate.Score -= candidate.ValidatorPenalty;
        }

        if (sharedSet && sharedMatch && sharedFingerprint is not null)
        {
            var sharedCandidate = candidates[sharedFingerprint];
            return new ComponentSelection<T>(
                sharedCandidate.LastValue,
                Exists: true,
                sharedCandidate.LastContext.ModKey,
                MergeConfidence.Medium,
                sharedCandidate.Score,
                "Shared carry-forward state across all sources.");
        }

        var best = candidates.Values
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.MeaningfulOccurrences)
            .ThenByDescending(static candidate => candidate.MeaningfulSupporters.Count)
            .ThenByDescending(static candidate => candidate.ReassertionCount)
            .ThenByDescending(static candidate => candidate.BundleSupportCount)
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
            .ThenByDescending(static candidate => candidate.MeaningfulSupporters.Count)
            .FirstOrDefault();

        var confidence = GetConfidence(best, runnerUp, volatility);
        var unsafeAmbiguity = confidence == MergeConfidence.Low && runnerUp is not null;
        var exists = !StringComparer.Ordinal.Equals(best.Fingerprint, "<removed>");
        return new ComponentSelection<T>(
            exists ? best.LastValue : null,
            exists,
            best.LastContext.ModKey,
            confidence,
            best.Score,
            BuildReason(best, hpmoByFingerprint.GetValueOrDefault(best.Fingerprint), unsafeAmbiguity),
            unsafeAmbiguity);
    }

    private static HpmoEvidenceKind ClassifyEvidence<T>(
        Candidate<T> candidate,
        QuestDeltaKind deltaKind,
        bool parentExists,
        bool dependencySupport,
        string candidateFingerprint,
        string? previousExistingFingerprint)
        where T : class
    {
        if (deltaKind == QuestDeltaKind.Removed && parentExists)
        {
            return HpmoEvidenceKind.ExplicitRemoval;
        }

        if (deltaKind == QuestDeltaKind.Added)
        {
            return dependencySupport
                ? HpmoEvidenceKind.CohesiveOverride
                : HpmoEvidenceKind.Addition;
        }

        if (deltaKind == QuestDeltaKind.Modified)
        {
            if (candidate.TotalOccurrences > 0
                && previousExistingFingerprint is not null
                && !StringComparer.Ordinal.Equals(previousExistingFingerprint, candidateFingerprint))
            {
                return HpmoEvidenceKind.StructuralReassertion;
            }

            return dependencySupport
                ? HpmoEvidenceKind.CohesiveOverride
                : HpmoEvidenceKind.DirectModification;
        }

        if (candidate.MeaningfulOccurrences > 0
            && previousExistingFingerprint is not null
            && !StringComparer.Ordinal.Equals(previousExistingFingerprint, candidateFingerprint))
        {
            return HpmoEvidenceKind.StructuralReassertion;
        }

        return HpmoEvidenceKind.PureCarryForward;
    }

    private static int GetEvidenceWeight(HpmoEvidenceKind evidence)
    {
        return evidence switch
        {
            HpmoEvidenceKind.CohesiveOverride => 5,
            HpmoEvidenceKind.StructuralReassertion => 4,
            HpmoEvidenceKind.Addition => 3,
            HpmoEvidenceKind.DirectModification => 3,
            HpmoEvidenceKind.ExplicitRemoval => 3,
            HpmoEvidenceKind.ParentDivergenceOnly => 1,
            _ => 0,
        };
    }

    private static bool IsMeaningful(HpmoEvidenceKind evidence)
    {
        return evidence is not HpmoEvidenceKind.PureCarryForward
            and not HpmoEvidenceKind.ParentDivergenceOnly;
    }

    private static string BuildReason<T>(
        Candidate<T> candidate,
        HpmoGroup? hpmoGroup,
        bool unsafeAmbiguity)
        where T : class
    {
        var reasons = new List<string>();
        if (candidate.ReassertionCount > 0)
        {
            reasons.Add($"{candidate.ReassertionCount} branch reassertion(s)");
        }

        if (candidate.BundleSupportCount > 0)
        {
            reasons.Add($"{candidate.BundleSupportCount} cohesive edit(s)");
        }

        if (candidate.DependentConflictCount > 0)
        {
            reasons.Add($"{candidate.DependentConflictCount} dependent override(s)");
        }

        if (candidate.ValidatorPenalty > 0)
        {
            reasons.Add($"validator penalty {candidate.ValidatorPenalty}");
        }

        if (hpmoGroup is not null && hpmoGroup.BranchSupportCount > 1)
        {
            reasons.Add($"{hpmoGroup.BranchSupportCount} HPMO branch support vote(s)");
        }

        if (unsafeAmbiguity)
        {
            reasons.Add("unsafe ambiguity");
        }

        if (reasons.Count == 0)
        {
            return StringComparer.Ordinal.Equals(candidate.Fingerprint, "<removed>")
                ? $"Selected removal from {candidate.LastContext.ModKey}."
                : $"Selected {candidate.Fingerprint} from {candidate.LastContext.ModKey}.";
        }

        return $"{(StringComparer.Ordinal.Equals(candidate.Fingerprint, "<removed>") ? "Selected removal" : $"Selected {candidate.Fingerprint}")} from {candidate.LastContext.ModKey} because {string.Join(", ", reasons)}.";
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

    private static bool HasMasterLinkedPriorIntent<T>(Candidate<T> candidate, QuestSourceContext context)
        where T : class
    {
        if (!candidate.HasMeaningfulSource)
        {
            return false;
        }

        return context.Masters.Contains(candidate.LastMeaningfulModKey);
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

    private static bool HasDependentAncestorConflict<T>(
        IReadOnlyList<ComponentTimelineEntry<T>> timeline,
        int index,
        QuestDeltaKind deltaKind,
        string candidateFingerprint,
        Func<T?, string> fingerprint)
        where T : class
    {
        if (index <= 0 || deltaKind is QuestDeltaKind.Unchanged)
        {
            return false;
        }

        var current = timeline[index];
        for (var ancestorIndex = index - 1; ancestorIndex >= 0; ancestorIndex--)
        {
            var ancestor = timeline[ancestorIndex];
            if (!current.Context.Masters.Contains(ancestor.Context.ModKey))
            {
                continue;
            }

            if (deltaKind == QuestDeltaKind.Removed)
            {
                if (ancestor.Exists && ancestor.Value is not null)
                {
                    return true;
                }

                continue;
            }

            if (!ancestor.Exists || ancestor.Value is null)
            {
                continue;
            }

            if (!StringComparer.Ordinal.Equals(fingerprint(ancestor.Value), candidateFingerprint))
            {
                return true;
            }
        }

        return false;
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

    private static MergeConfidence GetConfidence<T>(Candidate<T> best, Candidate<T>? runnerUp, int volatility)
        where T : class
    {
        if (best.Score <= 0)
        {
            return MergeConfidence.Low;
        }

        if (runnerUp is null)
        {
            return volatility >= 3 ? MergeConfidence.Medium : MergeConfidence.High;
        }

        var gap = best.Score - runnerUp.Score;
        var confidence = gap >= 4
            ? MergeConfidence.High
            : gap >= 2
                ? MergeConfidence.Medium
                : MergeConfidence.Low;

        if (volatility >= 4 && confidence == MergeConfidence.High)
        {
            return MergeConfidence.Medium;
        }

        if (volatility >= 3 && confidence == MergeConfidence.Medium)
        {
            return MergeConfidence.Low;
        }

        return confidence;
    }

    public sealed record ComponentTimelineEntry<T>(
        QuestSourceContext Context,
        T? Value,
        bool Exists,
        int? OrderIndex,
        bool ParentExists = true) where T : class;

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

        public int ReassertionCount { get; set; }

        public int BundleSupportCount { get; set; }

        public int DependentConflictCount { get; set; }

        public int ExplicitRemovalCount { get; set; }

        public int ValidatorPenalty { get; set; }

        public HpmoEvidenceKind LastEvidence { get; set; }

        public HashSet<ModKey> MeaningfulSupporters { get; } = [];
    }
}
