using System.Collections;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestDeltaExtractor
{
    public IReadOnlyList<QuestDelta> Build(IReadOnlyList<QuestSnapshot> snapshots)
    {
        var deltas = new List<QuestDelta>(snapshots.Count);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var current = snapshots[index];
            var previous = index > 0 ? snapshots[index - 1] : null;
            deltas.Add(new QuestDelta(current.Context, BuildEntries(previous, current)));
        }

        return deltas;
    }

    private static IReadOnlyDictionary<ComponentKey, ComponentDelta> BuildEntries(
        QuestSnapshot? previous,
        QuestSnapshot current)
    {
        var entries = new Dictionary<ComponentKey, ComponentDelta>();
        BuildScalarEntries(previous, current, entries);
        BuildKeyedEntries("QuestAlias", previous?.QuestAliases, current.QuestAliases, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildAliasNestedEntries(previous?.QuestAliases, current.QuestAliases, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("VmadAlias", previous?.VmadAliases, current.VmadAliases, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("VmadScript", previous?.VmadScripts, current.VmadScripts, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildVmadScriptPropertyEntries(previous?.VmadScripts, current.VmadScripts, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Stage", previous?.Stages, current.Stages, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildStageCoreEntries(previous?.Stages, current.Stages, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Objective", previous?.Objectives, current.Objectives, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("FragmentSection", previous?.FragmentSection, current.FragmentSection, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildKeyedEntries("Fragment", previous?.Fragments, current.Fragments, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("DialogConditions", previous?.DialogConditions, current.DialogConditions, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("EventConditions", previous?.EventConditions, current.EventConditions, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        BuildOrderedEntry("TextDisplayGlobals", previous?.TextDisplayGlobals, current.TextDisplayGlobals, current.Context.ModKey, current.Context.LoadOrderIndex, entries);
        return entries;
    }

    private static void BuildScalarEntries(
        QuestSnapshot? previous,
        QuestSnapshot current,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        foreach (var key in current.Scalars.Keys.Union(previous?.Scalars.Keys ?? [], StringComparer.Ordinal))
        {
            var previousValue = previous is not null && previous.Scalars.TryGetValue(key, out var prior)
                ? prior
                : null;
            var currentValue = current.Scalars.TryGetValue(key, out var currentScalar)
                ? currentScalar
                : null;
            var previousFingerprint = QuestFingerprint.Exact(previousValue);
            var currentFingerprint = QuestFingerprint.Exact(currentValue);
            var classification = Classify(previous is not null && previous.Scalars.ContainsKey(key), current.Scalars.ContainsKey(key), previousFingerprint, currentFingerprint);
            entries[new ComponentKey("Scalar", key)] = new ComponentDelta(
                new ComponentKey("Scalar", key),
                classification.Kind,
                classification.Evidence,
                classification.RemovalKind,
                previousFingerprint,
                currentFingerprint,
                current.Context.ModKey,
                current.Context.LoadOrderIndex);
        }
    }

    private static void BuildAliasNestedEntries(
        KeyedSectionSnapshot<QuestAlias>? previous,
        KeyedSectionSnapshot<QuestAlias> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        if (!current.Present)
        {
            return;
        }

        foreach (var aliasKey in current.Items.Keys.Union(previous?.Items.Keys ?? [], StringComparer.Ordinal))
        {
            QuestAlias? previousAlias = null;
            var previousExists = previous is not null && previous.Items.TryGetValue(aliasKey, out previousAlias);
            QuestAlias? currentAlias = null;
            var currentExists = current.Items.TryGetValue(aliasKey, out currentAlias);
            if (!currentExists || currentAlias is null)
            {
                continue;
            }

            BuildAliasCoreEntry(
                aliasKey,
                "Flags",
                previousExists ? previousAlias!.Flags : null,
                currentAlias.Flags,
                previousExists,
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasPackageData",
                aliasKey,
                previousExists ? previousAlias!.PackageData : null,
                currentAlias.PackageData,
                static item => $"FormLink:{GetFormLinkKey(item)}",
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasKeyword",
                aliasKey,
                previousExists ? previousAlias!.Keywords : null,
                currentAlias.Keywords,
                static item => $"FormLink:{GetFormLinkKey(item)}",
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasFaction",
                aliasKey,
                previousExists ? previousAlias!.Factions : null,
                currentAlias.Factions,
                static item => $"FormLink:{GetFormLinkKey(item)}",
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasSpell",
                aliasKey,
                previousExists ? previousAlias!.Spells : null,
                currentAlias.Spells,
                static item => $"FormLink:{GetFormLinkKey(item)}",
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasItem",
                aliasKey,
                previousExists ? previousAlias!.Items : null,
                currentAlias.Items,
                static item => $"Item:{GetContainerItemKey(item)}",
                modKey,
                loadOrderIndex,
                entries);
            BuildNestedCollectionEntries(
                "QuestAliasCondition",
                aliasKey,
                previousExists ? previousAlias!.Conditions : null,
                currentAlias.Conditions,
                static item => $"Condition:{QuestFingerprint.ConditionNear(item)}",
                modKey,
                loadOrderIndex,
                entries);
        }
    }

    private static void BuildAliasCoreEntry(
        string aliasKey,
        string fieldName,
        object? previousValue,
        object? currentValue,
        bool previousExists,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        var previousFingerprint = previousExists ? QuestFingerprint.Exact(previousValue) : "<missing>";
        var currentFingerprint = QuestFingerprint.Exact(currentValue);
        var classification = Classify(previousExists, currentExists: true, previousFingerprint, currentFingerprint);
        var componentKey = new ComponentKey("QuestAliasCore", $"{aliasKey}.{fieldName}");
        entries[componentKey] = new ComponentDelta(
            componentKey,
            classification.Kind,
            classification.Evidence,
            classification.RemovalKind,
            previousFingerprint,
            currentFingerprint,
            modKey,
            loadOrderIndex);
    }

    private static void BuildVmadScriptPropertyEntries(
        KeyedSectionSnapshot<ScriptEntry>? previous,
        KeyedSectionSnapshot<ScriptEntry> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        if (!current.Present)
        {
            return;
        }

        foreach (var scriptKey in current.Items.Keys.Union(previous?.Items.Keys ?? [], StringComparer.Ordinal))
        {
            ScriptEntry? previousScript = null;
            var previousExists = previous is not null && previous.Items.TryGetValue(scriptKey, out previousScript);
            ScriptEntry? currentScript = null;
            var currentExists = current.Items.TryGetValue(scriptKey, out currentScript);
            if (!currentExists || currentScript is null)
            {
                continue;
            }

            BuildNestedCollectionEntries(
                "VmadScriptProperty",
                scriptKey,
                previousExists ? previousScript!.Properties : null,
                currentScript.Properties,
                static item => $"Property:{GetPropertyName(item)}",
                modKey,
                loadOrderIndex,
                entries);
        }
    }

    private static void BuildNestedCollectionEntries(
        string kind,
        string parentKey,
        IEnumerable? previous,
        IEnumerable? current,
        Func<object, string> keyFactory,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        var previousItems = BuildNestedItemMap(previous, keyFactory);
        var currentItems = BuildNestedItemMap(current, keyFactory);

        foreach (var itemKey in currentItems.Keys.Union(previousItems.Keys, StringComparer.Ordinal))
        {
            var previousExists = previousItems.TryGetValue(itemKey, out var previousValue);
            var currentExists = currentItems.TryGetValue(itemKey, out var currentValue);
            var previousFingerprint = previousExists ? QuestFingerprint.Exact(previousValue) : "<missing>";
            var currentFingerprint = currentExists ? QuestFingerprint.Exact(currentValue) : "<missing>";
            var classification = Classify(previousExists, currentExists, previousFingerprint, currentFingerprint);
            var componentKey = new ComponentKey(kind, $"{parentKey}:{itemKey}");
            entries[componentKey] = new ComponentDelta(
                componentKey,
                classification.Kind,
                classification.Evidence,
                classification.RemovalKind,
                previousFingerprint,
                currentFingerprint,
                modKey,
                loadOrderIndex);
        }
    }

    private static Dictionary<string, object> BuildNestedItemMap(
        IEnumerable? enumerable,
        Func<object, string> keyFactory)
    {
        var items = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var item in EnumerateCollection(enumerable))
        {
            items[keyFactory(item)] = item;
        }

        return items;
    }

    private static IEnumerable<object> EnumerateCollection(IEnumerable? enumerable)
    {
        if (enumerable is null)
        {
            yield break;
        }

        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static string GetFormLinkKey(object item)
    {
        return item is IFormLinkGetter formLink
            ? formLink.FormKey.ToString()
            : item.ToString() ?? "<null>";
    }

    private static string GetContainerItemKey(object item)
    {
        return $"{QuestFingerprint.Exact(GetPropertyValue(item, "Item"))}|{QuestFingerprint.Exact(GetPropertyValue(item, "Data"))}";
    }

    private static string GetPropertyName(object item)
    {
        return Convert.ToString(GetPropertyValue(item, "Name"), System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "<null>";
    }

    private static object GetPropertyValue(object? target, string propertyName)
    {
        if (target is null)
        {
            return "<null>";
        }

        return target.GetType()
                   .GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                   ?.GetValue(target)
               ?? "<null>";
    }

    private static void BuildKeyedEntries<T>(
        string kind,
        KeyedSectionSnapshot<T>? previous,
        KeyedSectionSnapshot<T> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
        where T : class
    {
        if (!current.Present)
        {
            return;
        }

        foreach (var key in current.Items.Keys.Union(previous?.Items.Keys ?? [], StringComparer.Ordinal))
        {
            T? previousValue = null;
            var previousExists = previous is not null && previous.Items.TryGetValue(key, out previousValue);
            T? currentValue = null;
            var currentExists = current.Items.TryGetValue(key, out currentValue);
            var previousFingerprint = previousExists ? QuestFingerprint.Exact(previousValue) : "<missing>";
            var currentFingerprint = currentExists ? QuestFingerprint.Exact(currentValue) : "<missing>";
            var classification = Classify(previousExists, currentExists, previousFingerprint, currentFingerprint);
            entries[new ComponentKey(kind, key)] = new ComponentDelta(
                new ComponentKey(kind, key),
                classification.Kind,
                classification.Evidence,
                classification.RemovalKind,
                previousFingerprint,
                currentFingerprint,
                modKey,
                loadOrderIndex);
        }
    }

    private static void BuildStageCoreEntries(
        KeyedSectionSnapshot<QuestStage>? previous,
        KeyedSectionSnapshot<QuestStage> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        if (!current.Present)
        {
            return;
        }

        foreach (var key in current.Items.Keys.Union(previous?.Items.Keys ?? [], StringComparer.Ordinal))
        {
            QuestStage? previousStage = null;
            var previousExists = previous is not null && previous.Items.TryGetValue(key, out previousStage);
            QuestStage? currentStage = null;
            var currentExists = current.Items.TryGetValue(key, out currentStage);
            if (!currentExists || currentStage is null)
            {
                continue;
            }

            BuildStageCoreEntry(key, "Index", previousExists ? previousStage!.Index : null, currentStage.Index, previousExists, modKey, loadOrderIndex, entries);
            BuildStageCoreEntry(key, "Flags", previousExists ? previousStage!.Flags : null, currentStage.Flags, previousExists, modKey, loadOrderIndex, entries);
            BuildStageCoreEntry(key, "Unknown", previousExists ? previousStage!.Unknown : null, currentStage.Unknown, previousExists, modKey, loadOrderIndex, entries);
        }
    }

    private static void BuildStageCoreEntry(
        string stageKey,
        string fieldName,
        object? previousValue,
        object currentValue,
        bool previousExists,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        var previousFingerprint = previousExists ? QuestFingerprint.Exact(previousValue) : "<missing>";
        var currentFingerprint = QuestFingerprint.Exact(currentValue);
        var classification = Classify(previousExists, currentExists: true, previousFingerprint, currentFingerprint);
        entries[new ComponentKey("StageCore", $"{stageKey}.{fieldName}")] = new ComponentDelta(
            new ComponentKey("StageCore", $"{stageKey}.{fieldName}"),
            classification.Kind,
            classification.Evidence,
            classification.RemovalKind,
            previousFingerprint,
            currentFingerprint,
            modKey,
            loadOrderIndex);
    }

    private static void BuildOrderedEntry<T>(
        string kind,
        OrderedSectionSnapshot<T>? previous,
        OrderedSectionSnapshot<T> current,
        ModKey modKey,
        int loadOrderIndex,
        IDictionary<ComponentKey, ComponentDelta> entries)
    {
        if (!current.Present)
        {
            return;
        }

        var previousFingerprint = previous is not null && previous.Present
            ? QuestFingerprint.Exact(previous.Items)
            : "<missing>";
        var currentFingerprint = QuestFingerprint.Exact(current.Items);
        var classification = Classify(previous is not null && previous.Present, current.Present, previousFingerprint, currentFingerprint);
        entries[new ComponentKey(kind, kind)] = new ComponentDelta(
            new ComponentKey(kind, kind),
            classification.Kind,
            classification.Evidence,
            classification.RemovalKind,
            previousFingerprint,
            currentFingerprint,
            modKey,
            loadOrderIndex);
    }

    private static (QuestDeltaKind Kind, QuestDeltaEvidenceKind Evidence, QuestRemovalKind RemovalKind) Classify(
        bool previousExists,
        bool currentExists,
        string previousFingerprint,
        string currentFingerprint)
    {
        if (!previousExists && currentExists)
        {
            return (QuestDeltaKind.Added, QuestDeltaEvidenceKind.Addition, QuestRemovalKind.None);
        }

        if (previousExists && !currentExists)
        {
            return (QuestDeltaKind.Removed, QuestDeltaEvidenceKind.ExplicitRemoval, QuestRemovalKind.Explicit);
        }

        return StringComparer.Ordinal.Equals(previousFingerprint, currentFingerprint)
            ? (QuestDeltaKind.Unchanged, QuestDeltaEvidenceKind.PureCarryForward, QuestRemovalKind.None)
            : (QuestDeltaKind.Modified, QuestDeltaEvidenceKind.DirectModification, QuestRemovalKind.None);
    }
}
