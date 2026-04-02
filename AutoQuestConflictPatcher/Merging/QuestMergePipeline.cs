using System.Collections;
using System.Reflection;

using AutoQuestConflictPatcher.Configuration;
using AutoQuestConflictPatcher.Reporting;

using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestMergePipeline
{
    private readonly MergeReport _report;
    private readonly QuestSnapshotBuilder _snapshotBuilder;
    private readonly QuestDeltaExtractor _deltaExtractor;
    private readonly QuestMergeEngine _legacy;
    private readonly IntentSupportScorer _scorer;
    private readonly MergeValidator _validator;

    public QuestMergePipeline(MergeReport report, string? dataFolderPath = null)
    {
        _report = report;
        _snapshotBuilder = new QuestSnapshotBuilder();
        _deltaExtractor = new QuestDeltaExtractor();
        _legacy = new QuestMergeEngine(report, dataFolderPath);
        _scorer = new IntentSupportScorer(new OfficialMasterClassifier(dataFolderPath));
        _validator = new MergeValidator();
    }

    public Quest Merge(QuestConflict conflict)
    {
        var snapshots = conflict.ContextsLowToHigh
            .Select(_snapshotBuilder.Build)
            .ToArray();
        var deltas = _deltaExtractor.Build(snapshots);
        var dependencies = QuestDependencyGraph.Build(snapshots);
        var merged = _legacy.Merge(conflict);

        _report.Log($"Intent-aware merge started for {conflict.DisplayName}.");

        if (!ShouldKeepWinner(conflict, QuestMergeSection.TopLevel))
        {
            ResolveScalars(merged, snapshots, deltas, dependencies);
        }

        if (ShouldKeepWinner(conflict, QuestMergeSection.Aliases))
        {
            ApplyWinnerAliases(merged, conflict);
        }
        else
        {
            ResolveAliases(merged, snapshots, deltas, dependencies);
        }

        if (ShouldKeepWinner(conflict, QuestMergeSection.Stages))
        {
            ReplaceQuestList<QuestStage>(merged, nameof(Quest.Stages), conflict.WinningQuest.Stages);
        }
        else
        {
            ResolveStages(merged, snapshots, deltas, dependencies);
        }

        if (ShouldKeepWinner(conflict, QuestMergeSection.Objectives))
        {
            ReplaceQuestList<QuestObjective>(merged, nameof(Quest.Objectives), conflict.WinningQuest.Objectives);
        }
        else
        {
            ResolveObjectives(merged, snapshots, deltas, dependencies);
        }

        if (ShouldKeepWinner(conflict, QuestMergeSection.Vmad))
        {
            merged.VirtualMachineAdapter = CloneAs<QuestAdapter>(conflict.WinningQuest.VirtualMachineAdapter!);
        }
        else
        {
            ResolveQuestScripts(merged, snapshots, deltas, dependencies);
            ResolveFragments(merged, snapshots, deltas, dependencies);
        }

        var validation = _validator.Validate(merged);
        foreach (var warning in validation.Warnings)
        {
            _report.Log($"Validation warning on {conflict.DisplayName}: {warning}");
        }

        foreach (var error in validation.Errors)
        {
            _report.Log($"Validation error on {conflict.DisplayName}: {error}");
        }

        _report.Log($"Intent-aware merge completed for {conflict.DisplayName} with {(validation.IsValid ? "valid" : "review-required")} result.");
        return merged;
    }

    private void ResolveScalars(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        foreach (var key in snapshots.SelectMany(static snapshot => snapshot.Scalars.Keys).Distinct(StringComparer.Ordinal))
        {
            var property = merged.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            var selection = _scorer.Select(
                new ComponentKey("Scalar", key),
                snapshots.Select(snapshot =>
                    new IntentSupportScorer.ComponentTimelineEntry<ScalarBox>(
                        snapshot.Context,
                        snapshot.Scalars.TryGetValue(key, out var value) ? new ScalarBox(value) : null,
                        snapshot.Scalars.ContainsKey(key),
                        OrderIndex: null))
                    .ToArray(),
                deltas,
                dependencies,
                static box => box is null ? "<missing>" : QuestFingerprint.Exact(box.Value));

            if (!selection.Exists || selection.Value is null)
            {
                continue;
            }

            var cloned = DeepCopyHelper.CloneForAssignment(selection.Value.Value, property.PropertyType);
            if (cloned is null && selection.Value.Value is not null)
            {
                continue;
            }

            property.SetValue(merged, cloned);
        }
    }

    private void ResolveAliases(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        var aliasKeys = snapshots
            .SelectMany(snapshot => snapshot.QuestAliases.Items.Keys.Concat(snapshot.VmadAliases.Items.Keys))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var legacyAliasMap = merged.Aliases?.ToDictionary(static alias => $"Alias:{alias.ID}", StringComparer.Ordinal)
            ?? new Dictionary<string, QuestAlias>(StringComparer.Ordinal);
        var legacyVmadAliasMap = merged.VirtualMachineAdapter?.Aliases?.ToDictionary(BuildVmadAliasKey, StringComparer.Ordinal)
            ?? new Dictionary<string, QuestFragmentAlias>(StringComparer.Ordinal);

        var resolvedAliases = new List<(int Order, bool FromWinner, QuestAlias Alias)>();
        var resolvedVmadAliases = new List<(int Order, bool FromWinner, QuestFragmentAlias Alias)>();
        var confidenceCounts = new Dictionary<MergeConfidence, int>();
        var winningModKey = snapshots[^1].Context.ModKey;

        foreach (var key in aliasKeys)
        {
            var aliasSelection = _scorer.Select(
                new ComponentKey("QuestAlias", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.QuestAliases),
                deltas,
                dependencies);
            var vmadSelection = _scorer.Select(
                new ComponentKey("VmadAlias", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.VmadAliases),
                deltas,
                dependencies,
                penaltySelector: alias => alias is null || alias.Property is null ? 4 : 0);

            CountConfidence(confidenceCounts, aliasSelection.Confidence);
            CountConfidence(confidenceCounts, vmadSelection.Confidence);

            if (aliasSelection.Exists && aliasSelection.Value is not null)
            {
                var legacyAlias = legacyAliasMap.GetValueOrDefault(key);
                var chosenAlias = _validator.AreCompatible(legacyAlias, aliasSelection.Value)
                    ? CopyPreferLegacy(legacyAlias, aliasSelection.Value)
                    : DeepCopy(aliasSelection.Value)!;
                resolvedAliases.Add((ResolveOrder(aliasSelection, snapshots, key, static snapshot => snapshot.QuestAliases), aliasSelection.SelectedFrom == winningModKey, chosenAlias));
            }

            if (vmadSelection.Exists && vmadSelection.Value is not null)
            {
                var legacyAlias = legacyVmadAliasMap.GetValueOrDefault(key);
                var chosenAlias = ChooseRicherVmadAlias(legacyAlias, vmadSelection.Value);
                if (chosenAlias.Property is not null && chosenAlias.Property.Alias >= 0 && chosenAlias.Property.Object.FormKey != default)
                {
                    resolvedVmadAliases.Add((ResolveOrder(vmadSelection, snapshots, key, static snapshot => snapshot.VmadAliases), vmadSelection.SelectedFrom == winningModKey, chosenAlias));
                }
                else
                {
                    _report.Log($"Dropped invalid VMAD alias bundle {key} during intent-aware merge.");
                }
            }
        }

        EnsureListPropertyInitialized(merged, nameof(Quest.Aliases));
        ReplaceListContents(GetListProperty<QuestAlias>(merged, nameof(Quest.Aliases)), resolvedAliases
            .OrderBy(static entry => entry.Order)
            .ThenBy(static entry => entry.FromWinner)
            .ThenBy(static entry => entry.Alias.ID)
            .Select(static entry => entry.Alias));

        if (resolvedVmadAliases.Count > 0 || merged.VirtualMachineAdapter is not null)
        {
            merged.VirtualMachineAdapter ??= NewAdapter();
            EnsureListPropertyInitialized(merged.VirtualMachineAdapter, nameof(QuestAdapter.Aliases));
            ReplaceListContents(GetListProperty<QuestFragmentAlias>(merged.VirtualMachineAdapter, nameof(QuestAdapter.Aliases)), resolvedVmadAliases
                .OrderBy(static entry => entry.Order)
                .ThenBy(static entry => entry.FromWinner)
                .ThenBy(entry => entry.Alias.Property?.Alias ?? short.MaxValue)
                .Select(static entry => entry.Alias));
        }

        LogConfidenceSummary("Alias bundle", confidenceCounts);
    }

    private void ResolveStages(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        var legacyStages = merged.Stages?.ToDictionary(static stage => $"Stage:{stage.Index}", StringComparer.Ordinal)
            ?? new Dictionary<string, QuestStage>(StringComparer.Ordinal);
        var resolved = new List<(int Order, QuestStage Stage)>();
        var confidenceCounts = new Dictionary<MergeConfidence, int>();

        foreach (var key in snapshots.SelectMany(static snapshot => snapshot.Stages.Items.Keys).Distinct(StringComparer.Ordinal))
        {
            var selection = _scorer.Select(
                new ComponentKey("Stage", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.Stages),
                deltas,
                dependencies);

            CountConfidence(confidenceCounts, selection.Confidence);
            if (!selection.Exists || selection.Value is null)
            {
                continue;
            }

            var chosen = DeepCopy(selection.Value)!;
            if (legacyStages.TryGetValue(key, out var legacyStage))
            {
                EnsureListPropertyInitialized(chosen, nameof(QuestStage.LogEntries));
                ReplaceListContents(GetListProperty<QuestLogEntry>(chosen, nameof(QuestStage.LogEntries)), legacyStage.LogEntries);
            }

            resolved.Add((ResolveOrder(selection, snapshots, key, static snapshot => snapshot.Stages), chosen));
        }

        EnsureListPropertyInitialized(merged, nameof(Quest.Stages));
        ReplaceListContents(GetListProperty<QuestStage>(merged, nameof(Quest.Stages)), resolved.OrderBy(static entry => entry.Order).ThenBy(static entry => entry.Stage.Index).Select(static entry => entry.Stage));

        LogConfidenceSummary("Stage bundle", confidenceCounts);
    }

    private void ResolveObjectives(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        var resolved = new List<(int Order, QuestObjective Objective)>();
        foreach (var key in snapshots.SelectMany(static snapshot => snapshot.Objectives.Items.Keys).Distinct(StringComparer.Ordinal))
        {
            var selection = _scorer.Select(
                new ComponentKey("Objective", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.Objectives),
                deltas,
                dependencies);

            if (!selection.Exists || selection.Value is null)
            {
                continue;
            }

            resolved.Add((ResolveOrder(selection, snapshots, key, static snapshot => snapshot.Objectives), DeepCopy(selection.Value)!));
        }

        EnsureListPropertyInitialized(merged, nameof(Quest.Objectives));
        ReplaceListContents(GetListProperty<QuestObjective>(merged, nameof(Quest.Objectives)), resolved.OrderBy(static entry => entry.Order).ThenBy(static entry => entry.Objective.Index).Select(static entry => entry.Objective));
    }

    private void ResolveQuestScripts(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        var resolved = new List<(int Order, ScriptEntry Script)>();
        foreach (var key in snapshots.SelectMany(static snapshot => snapshot.VmadScripts.Items.Keys).Distinct(StringComparer.Ordinal))
        {
            var selection = _scorer.Select(
                new ComponentKey("VmadScript", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.VmadScripts),
                deltas,
                dependencies);

            if (!selection.Exists || selection.Value is null)
            {
                continue;
            }

            resolved.Add((ResolveOrder(selection, snapshots, key, static snapshot => snapshot.VmadScripts), DeepCopy(selection.Value)!));
        }

        if (resolved.Count > 0 || merged.VirtualMachineAdapter is not null)
        {
            merged.VirtualMachineAdapter ??= NewAdapter();
            EnsureListPropertyInitialized(merged.VirtualMachineAdapter, nameof(QuestAdapter.Scripts));
            ReplaceListContents(GetListProperty<ScriptEntry>(merged.VirtualMachineAdapter, nameof(QuestAdapter.Scripts)), resolved.OrderBy(static entry => entry.Order).ThenBy(entry => entry.Script.Name, StringComparer.OrdinalIgnoreCase).Select(static entry => entry.Script));
        }
    }

    private void ResolveFragments(
        Quest merged,
        IReadOnlyList<QuestSnapshot> snapshots,
        IReadOnlyList<QuestDelta> deltas,
        QuestDependencyGraph dependencies)
    {
        var resolved = new List<(int Order, QuestScriptFragment Fragment)>();
        foreach (var key in snapshots.SelectMany(static snapshot => snapshot.Fragments.Items.Keys).Distinct(StringComparer.Ordinal))
        {
            var selection = _scorer.Select(
                new ComponentKey("Fragment", key),
                BuildTimeline(snapshots, key, static snapshot => snapshot.Fragments),
                deltas,
                dependencies,
                penaltySelector: fragment => fragment is null ? 0 : HasFragmentStageRisk(fragment, merged.Stages) ? 5 : 0);

            if (!selection.Exists || selection.Value is null)
            {
                continue;
            }

            resolved.Add((ResolveOrder(selection, snapshots, key, static snapshot => snapshot.Fragments), DeepCopy(selection.Value)!));
        }

        if (resolved.Count > 0 || merged.VirtualMachineAdapter is not null)
        {
            merged.VirtualMachineAdapter ??= NewAdapter();
            EnsureListPropertyInitialized(merged.VirtualMachineAdapter, nameof(QuestAdapter.Fragments));
            ReplaceListContents(GetListProperty<QuestScriptFragment>(merged.VirtualMachineAdapter, nameof(QuestAdapter.Fragments)), resolved.OrderBy(static entry => entry.Order).Select(static entry => entry.Fragment));
        }
    }

    private static bool HasFragmentStageRisk(QuestScriptFragment fragment, IEnumerable<QuestStage>? stages)
    {
        if (stages is null)
        {
            return false;
        }

        var stageValues = stages.Select(static stage => stage.Index).ToHashSet();
        var stage = Convert.ToUInt16(GetNumericProperty(fragment, "Stage"));
        var stageIndex = Convert.ToUInt16(GetNumericProperty(fragment, "StageIndex"));
        return stage != 0 && !stageValues.Contains(stage) && stageIndex != 0 && !stageValues.Contains(stageIndex);
    }

    private static int GetNumericProperty(object target, string propertyName)
    {
        var value = target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(target);
        return value is null
            ? 0
            : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static T? DeepCopy<T>(T? value) where T : class
    {
        return value is null ? null : (DeepCopyHelper.DeepCopyObject(value) as T ?? value);
    }

    private static QuestAdapter NewAdapter()
    {
        return new QuestAdapter
        {
            Scripts = [],
            Aliases = [],
            Fragments = [],
        };
    }

    private static void ApplyWinnerAliases(Quest merged, QuestConflict conflict)
    {
        ReplaceQuestList<QuestAlias>(merged, nameof(Quest.Aliases), conflict.WinningQuest.Aliases);
        merged.VirtualMachineAdapter ??= NewAdapter();
        EnsureListPropertyInitialized(merged.VirtualMachineAdapter, nameof(QuestAdapter.Aliases));
        ReplaceListContents(GetListProperty<QuestFragmentAlias>(merged.VirtualMachineAdapter, nameof(QuestAdapter.Aliases)), conflict.WinningQuest.VirtualMachineAdapter?.Aliases);
    }

    private static bool ShouldKeepWinner(QuestConflict conflict, QuestMergeSection section)
    {
        return (conflict.WinnerOnlySections & section) != 0;
    }

    private static void ReplaceQuestList<T>(object target, string propertyName, IEnumerable? source)
    {
        EnsureListPropertyInitialized(target, propertyName);
        ReplaceListContents(GetListProperty<T>(target, propertyName), source);
    }

    private static QuestAlias CopyPreferLegacy(QuestAlias? legacyAlias, QuestAlias selectedAlias)
    {
        if (legacyAlias is null)
        {
            return DeepCopy(selectedAlias)!;
        }

        return DeepCopy(legacyAlias)!;
    }

    private QuestFragmentAlias ChooseRicherVmadAlias(QuestFragmentAlias? legacyAlias, QuestFragmentAlias selectedAlias)
    {
        if (legacyAlias is null)
        {
            return DeepCopy(selectedAlias)!;
        }

        if (!_validator.AreCompatible(legacyAlias, selectedAlias))
        {
            return DeepCopy(selectedAlias)!;
        }

        var legacyPropertyCount = legacyAlias.Scripts?.Sum(static script => script.Properties?.Count ?? 0) ?? 0;
        var selectedPropertyCount = selectedAlias.Scripts?.Sum(static script => script.Properties?.Count ?? 0) ?? 0;
        return legacyPropertyCount >= selectedPropertyCount
            ? DeepCopy(legacyAlias)!
            : DeepCopy(selectedAlias)!;
    }

    private static string BuildVmadAliasKey(QuestFragmentAlias alias)
    {
        if (alias.Property is not null && alias.Property.Alias >= 0)
        {
            return $"Alias:{alias.Property.Alias}";
        }

        return $"VmadAlias:{QuestFingerprint.Exact(alias)}";
    }

    private static IReadOnlyList<IntentSupportScorer.ComponentTimelineEntry<T>> BuildTimeline<T>(
        IReadOnlyList<QuestSnapshot> snapshots,
        string key,
        Func<QuestSnapshot, KeyedSectionSnapshot<T>> selector)
        where T : class
    {
        return snapshots
            .Select(snapshot =>
            {
                var section = selector(snapshot);
                var exists = section.Items.TryGetValue(key, out var value);
                return new IntentSupportScorer.ComponentTimelineEntry<T>(
                    snapshot.Context,
                    value,
                    exists,
                    section.Order.TryGetValue(key, out var order) ? order : null);
            })
            .ToArray();
    }

    private static int ResolveOrder<T>(
        ComponentSelection<T> selection,
        IReadOnlyList<QuestSnapshot> snapshots,
        string key,
        Func<QuestSnapshot, KeyedSectionSnapshot<T>> selector)
        where T : class
    {
        for (var index = snapshots.Count - 1; index >= 0; index--)
        {
            if (snapshots[index].Context.ModKey != selection.SelectedFrom)
            {
                continue;
            }

            var section = selector(snapshots[index]);
            if (section.Order.TryGetValue(key, out var order))
            {
                return order;
            }
        }

        for (var index = snapshots.Count - 1; index >= 0; index--)
        {
            var section = selector(snapshots[index]);
            if (section.Order.TryGetValue(key, out var order))
            {
                return order;
            }
        }

        return int.MaxValue;
    }

    private void LogConfidenceSummary(string sectionName, IReadOnlyDictionary<MergeConfidence, int> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        _report.Log($"{sectionName} confidence summary: High={counts.GetValueOrDefault(MergeConfidence.High)}, Medium={counts.GetValueOrDefault(MergeConfidence.Medium)}, Low={counts.GetValueOrDefault(MergeConfidence.Low)}.");
    }

    private static void CountConfidence(IDictionary<MergeConfidence, int> counts, MergeConfidence confidence)
    {
        counts[confidence] = counts.TryGetValue(confidence, out var current) ? current + 1 : 1;
    }

    private sealed class ScalarBox
    {
        public ScalarBox(object? value)
        {
            Value = value;
        }

        public object? Value { get; }
    }

    private static void EnsureListPropertyInitialized(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Unable to find property {propertyName} on {target.GetType().FullName}.");

        if (property.GetValue(target) is not null)
        {
            return;
        }

        var list = System.Activator.CreateInstance(property.PropertyType)
            ?? throw new InvalidOperationException($"Unable to create list for {target.GetType().FullName}.{propertyName}.");

        property.SetValue(target, list);
    }

    private static IList<T> GetListProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Unable to find property {propertyName} on {target.GetType().FullName}.");
        return (IList<T>)(property.GetValue(target)
            ?? throw new InvalidOperationException($"List property {target.GetType().FullName}.{propertyName} was not initialized."));
    }

    private static void ReplaceListContents<T>(IList<T> target, IEnumerable? source)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is null)
            {
                continue;
            }

            target.Add(CloneAs<T>(item));
        }
    }

    private static T CloneAs<T>(object item)
    {
        var clone = DeepCopyHelper.DeepCopyObject(item);
        if (clone is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Unable to clone {item.GetType().FullName} as {typeof(T).FullName}.");
    }
}
