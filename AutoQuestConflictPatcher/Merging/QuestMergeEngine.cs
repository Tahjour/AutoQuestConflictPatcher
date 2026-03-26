using System.Collections;
using System.Reflection;

using AutoQuestConflictPatcher.Configuration;
using AutoQuestConflictPatcher.Reporting;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestMergeEngine
{
    private readonly MergeReport _report;

    public QuestMergeEngine(MergeReport report)
    {
        _report = report;
    }

    public Quest Merge(QuestConflict conflict)
    {
        var merged = conflict.WinningQuest.DeepCopy();
        var sources = conflict.ContextsLowToHigh
            .Select(context => new MergeSource(context, context.Quest, Exists: true))
            .ToArray();

        _report.Log($"Merging {conflict.DisplayName} from {conflict.ContextsLowToHigh.Count} contexts.");
        MergeObject(merged, sources, string.Empty, conflict);
        return merged;
    }

    private void MergeObject(object target, IReadOnlyList<MergeSource> sources, string path, QuestConflict conflict)
    {
        foreach (var property in target.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(static property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name))
        {
            if (ShouldSkipProperty(path, property.Name))
            {
                continue;
            }

            var propertyPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
            if (ShouldKeepWinner(conflict, propertyPath))
            {
                continue;
            }

            var projected = ProjectProperty(sources, property.Name);
            if (IsLeafType(property.PropertyType))
            {
                MergeLeafProperty(target, property, projected, propertyPath, conflict);
                continue;
            }

            if (IsListType(property.PropertyType))
            {
                MergeListProperty(target, property, projected, propertyPath, conflict);
                continue;
            }

            MergeComplexProperty(target, property, projected, propertyPath, conflict);
        }
    }

    private void MergeLeafProperty(
        object target,
        PropertyInfo property,
        IReadOnlyList<MergeSource> sources,
        string propertyPath,
        QuestConflict conflict)
    {
        if (!sources.Any(static source => source.Exists))
        {
            return;
        }

        var selection = HpuSelector.Select(sources, conflict.LeafMods);
        if (selection is null)
        {
            return;
        }

        if (!AssignPropertyValue(target, property, DeepCopyHelper.CloneForAssignment(selection.Value, property.PropertyType)))
        {
            _report.Log($"Skipped incompatible leaf assignment for {propertyPath}: {selection.Value?.GetType().FullName ?? "<null>"} -> {property.PropertyType.FullName ?? property.PropertyType.Name}.");
            return;
        }

        if (selection.SelectedFrom != conflict.WinningContext.ModKey)
        {
            _report.Log($"HPU selected {propertyPath} from {selection.SelectedFrom}.");
        }
    }

    private void MergeComplexProperty(
        object target,
        PropertyInfo property,
        IReadOnlyList<MergeSource> sources,
        string propertyPath,
        QuestConflict conflict)
    {
        if (!HasLeafValue(sources, conflict.LeafMods))
        {
            AssignPropertyValue(target, property, null);
            return;
        }

        object? childTarget = property.GetValue(target);
        if (childTarget is null)
        {
            var seed = GetSeedValue(sources);
            if (seed is null)
            {
                return;
            }

            childTarget = DeepCopyHelper.DeepCopyObject(seed);
            if (!AssignPropertyValue(target, property, childTarget))
            {
                _report.Log($"Skipped incompatible complex assignment for {propertyPath}: {childTarget.GetType().FullName} -> {property.PropertyType.FullName ?? property.PropertyType.Name}.");
                return;
            }
        }

        MergeObject(childTarget, sources, propertyPath, conflict);
    }

    private void MergeListProperty(
        object target,
        PropertyInfo property,
        IReadOnlyList<MergeSource> sources,
        string propertyPath,
        QuestConflict conflict)
    {
        ReplaceListContents(target, property, MergeListEntries(sources, propertyPath, conflict));
    }

    private IReadOnlyList<object> MergeListEntries(
        IReadOnlyList<MergeSource> sources,
        string propertyPath,
        QuestConflict conflict)
    {
        var compiled = sources
            .Select(source => new CompiledSource(source, BuildListEntries(source, propertyPath)))
            .ToArray();

        var winnerEntries = compiled[^1].Entries;
        var leafEntries = compiled
            .Where(source => conflict.LeafMods.Contains(source.Source.Context.ModKey))
            .SelectMany(source => source.Entries)
            .OrderBy(entry => entry.Context.LoadOrderIndex)
            .ThenBy(entry => entry.Index)
            .ToArray();

        var orderedKeys = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in winnerEntries)
        {
            if (seenKeys.Add(entry.BucketKey))
            {
                orderedKeys.Add(entry.BucketKey);
            }
        }

        foreach (var entry in leafEntries)
        {
            if (seenKeys.Add(entry.BucketKey))
            {
                orderedKeys.Add(entry.BucketKey);
            }
        }

        var emittedExact = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<object>();

        foreach (var bucketKey in orderedKeys)
        {
            var bucketSources = compiled
                .Select(source =>
                {
                    var entry = source.Entries.FirstOrDefault(candidate => candidate.BucketKey == bucketKey);
                    return entry is null
                        ? new MergeSource(source.Source.Context, null, Exists: false)
                        : new MergeSource(source.Source.Context, entry.Item, Exists: true);
                })
                .ToArray();

            if (!HasLeafValue(bucketSources, conflict.LeafMods))
            {
                continue;
            }

            var mergedEntry = MergeBucket(bucketSources, propertyPath, conflict);
            var exactKey = QuestFingerprint.Exact(mergedEntry);
            if (!emittedExact.Add(exactKey))
            {
                _report.Log($"Deduped duplicate entry at {conflict.DisplayName}::{propertyPath}.");
                continue;
            }

            merged.Add(mergedEntry);
        }

        return merged;
    }

    private object MergeBucket(
        IReadOnlyList<MergeSource> sources,
        string propertyPath,
        QuestConflict conflict)
    {
        var leafTypes = sources
            .Where(source => source.Exists && source.Value is not null && conflict.LeafMods.Contains(source.Context.ModKey))
            .Select(source => source.Value!.GetType())
            .Distinct()
            .ToArray();

        if (ShouldSelectWholeItem(propertyPath, leafTypes))
        {
            var selection = HpuSelector.Select(
                sources,
                conflict.LeafMods,
                QuestFingerprint.Exact);

            if (selection?.Value is null)
            {
                throw new InvalidOperationException($"Unable to select HPU entry for {propertyPath}.");
            }

            _report.Log($"Selected HPU entry for {propertyPath} from {selection.SelectedFrom}.");
            return DeepCopyHelper.DeepCopyObject(selection.Value);
        }

        var seed = GetSeedValue(sources)
            ?? throw new InvalidOperationException($"Unable to seed bucket merge for {propertyPath}.");

        var target = DeepCopyHelper.DeepCopyObject(seed);
        MergeObject(target, sources, propertyPath, conflict);
        return target;
    }

    private static IReadOnlyList<ListEntry> BuildListEntries(MergeSource source, string propertyPath)
    {
        if (!source.Exists || source.Value is null || source.Value is not IEnumerable enumerable)
        {
            return [];
        }

        var uniqueBuckets = UsesStableUniqueBucket(propertyPath);
        var entries = new List<ListEntry>();
        var exactSeen = new HashSet<string>(StringComparer.Ordinal);
        var bucketCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var bucketIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;

        foreach (var item in enumerable)
        {
            if (item is null)
            {
                index++;
                continue;
            }

            var exactKey = QuestFingerprint.Exact(item);
            if (!exactSeen.Add(exactKey))
            {
                index++;
                continue;
            }

            var bucketBase = GetBucketBaseKey(item, propertyPath);
            if (uniqueBuckets)
            {
                var entry = new ListEntry(
                    source.Context,
                    item,
                    bucketBase,
                    exactKey,
                    index);

                if (bucketIndices.TryGetValue(bucketBase, out var existingIndex))
                {
                    var existing = entries[existingIndex];
                    entries[existingIndex] = entry with { Index = existing.Index };
                }
                else
                {
                    bucketIndices[bucketBase] = entries.Count;
                    entries.Add(entry);
                }
            }
            else
            {
                bucketCounts.TryGetValue(bucketBase, out var occurrence);
                bucketCounts[bucketBase] = occurrence + 1;

                entries.Add(new ListEntry(
                    source.Context,
                    item,
                    $"{bucketBase}#{occurrence}",
                    exactKey,
                    index));
            }

            index++;
        }

        return entries;
    }

    private static string GetBucketBaseKey(object item, string propertyPath)
    {
        return propertyPath switch
        {
            "TextDisplayGlobals" => $"FormLink:{GetFormLinkKey(item)}",
            "Aliases" => $"Alias:{GetPropertyValue(item, "ID")}",
            "Aliases.Keywords" => $"FormLink:{GetFormLinkKey(item)}",
            "Aliases.Factions" => $"FormLink:{GetFormLinkKey(item)}",
            "Aliases.Spells" => $"FormLink:{GetFormLinkKey(item)}",
            "Aliases.PackageData" => $"FormLink:{GetFormLinkKey(item)}",
            "Aliases.Items" => $"Item:{GetContainerItemKey(item)}",
            "DialogConditions" or "EventConditions" => $"Condition:{QuestFingerprint.ConditionNear(item)}",
            var path when path.EndsWith(".Conditions", StringComparison.Ordinal) => $"Condition:{QuestFingerprint.ConditionNear(item)}",
            "Stages" => $"Stage:{GetPropertyValue(item, "Index")}",
            "Stages.LogEntries" => $"LogEntry:{QuestFingerprint.LogEntryNear(item)}",
            "Objectives" => $"Objective:{GetPropertyValue(item, "Index")}",
            "Objectives.Targets" => $"ObjectiveTarget:{GetPropertyValue(item, "AliasID")}",
            "VirtualMachineAdapter.Scripts" => $"Script:{GetPropertyValue(item, "Name")}",
            "VirtualMachineAdapter.Scripts.Properties" => $"Property:{GetPropertyValue(item, "Name")}",
            "VirtualMachineAdapter.Scripts.Properties.Objects" => $"ScriptObject:{GetScriptObjectKey(item)}",
            "VirtualMachineAdapter.Aliases" => $"FragmentAlias:{GetPropertyValue(GetPropertyValue(item, "Property"), "Name")}",
            "VirtualMachineAdapter.Aliases.Scripts" => $"Script:{GetPropertyValue(item, "Name")}",
            "VirtualMachineAdapter.Aliases.Scripts.Properties" => $"Property:{GetPropertyValue(item, "Name")}",
            "VirtualMachineAdapter.Aliases.Scripts.Properties.Objects" => $"ScriptObject:{GetScriptObjectKey(item)}",
            "VirtualMachineAdapter.Fragments" => $"Fragment:{GetPropertyValue(item, "Stage")}:{GetPropertyValue(item, "StageIndex")}:{GetPropertyValue(item, "ScriptName")}:{GetPropertyValue(item, "FragmentName")}",
            _ => $"Exact:{QuestFingerprint.Exact(item)}",
        };
    }

    private static bool ShouldSelectWholeItem(string propertyPath, IReadOnlyCollection<Type> leafTypes)
    {
        if (propertyPath is "DialogConditions" or "EventConditions")
        {
            return true;
        }

        if (propertyPath.EndsWith(".Conditions", StringComparison.Ordinal))
        {
            return true;
        }

        if (propertyPath is "TextDisplayGlobals" or "Aliases.Keywords" or "Aliases.Factions" or "Aliases.Spells" or "Aliases.PackageData")
        {
            return true;
        }

        if (propertyPath == "VirtualMachineAdapter.Scripts.Properties" && leafTypes.Count > 1)
        {
            return true;
        }

        return propertyPath switch
        {
            "Aliases" => false,
            "Aliases.Items" => false,
            "Stages" => false,
            "Stages.LogEntries" => false,
            "Objectives" => false,
            "Objectives.Targets" => false,
            "VirtualMachineAdapter.Scripts" => false,
            "VirtualMachineAdapter.Scripts.Properties" => false,
            "VirtualMachineAdapter.Scripts.Properties.Objects" => false,
            "VirtualMachineAdapter.Aliases" => false,
            "VirtualMachineAdapter.Aliases.Scripts" => false,
            "VirtualMachineAdapter.Aliases.Scripts.Properties" => false,
            "VirtualMachineAdapter.Aliases.Scripts.Properties.Objects" => false,
            "VirtualMachineAdapter.Fragments" => false,
            _ => true,
        };
    }

    private static bool ShouldKeepWinner(QuestConflict conflict, string propertyPath)
    {
        var section = GetSection(propertyPath);
        return section != QuestMergeSection.None && conflict.WinnerOnlySections.HasFlag(section);
    }

    private static QuestMergeSection GetSection(string propertyPath)
    {
        if (propertyPath is "DialogConditions" or "EventConditions")
        {
            return QuestMergeSection.Conditions;
        }

        if (propertyPath.Contains(".Conditions", StringComparison.Ordinal))
        {
            return QuestMergeSection.Conditions;
        }

        if (propertyPath.StartsWith("Aliases", StringComparison.Ordinal))
        {
            return QuestMergeSection.Aliases;
        }

        if (propertyPath.StartsWith("Stages", StringComparison.Ordinal))
        {
            return QuestMergeSection.Stages;
        }

        if (propertyPath.StartsWith("Objectives", StringComparison.Ordinal))
        {
            return QuestMergeSection.Objectives;
        }

        if (propertyPath.StartsWith("VirtualMachineAdapter", StringComparison.Ordinal))
        {
            return QuestMergeSection.Vmad;
        }

        return QuestMergeSection.TopLevel;
    }

    private static IReadOnlyList<MergeSource> ProjectProperty(IReadOnlyList<MergeSource> sources, string propertyName)
    {
        var projected = new MergeSource[sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (!source.Exists || source.Value is null)
            {
                projected[index] = new MergeSource(source.Context, null, Exists: false);
                continue;
            }

            var property = source.Value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            projected[index] = new MergeSource(
                source.Context,
                property?.GetValue(source.Value),
                Exists: true);
        }

        return projected;
    }

    private static bool HasLeafValue(IReadOnlyList<MergeSource> sources, IReadOnlySet<ModKey> leafMods)
    {
        return sources.Any(source => source.Exists && source.Value is not null && leafMods.Contains(source.Context.ModKey));
    }

    private static object? GetSeedValue(IReadOnlyList<MergeSource> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index].Exists && sources[index].Value is not null)
            {
                return sources[index].Value;
            }
        }

        return null;
    }

    private static bool IsListType(Type type)
    {
        return !IsLeafType(type) && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsLeafType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsPrimitive || type.IsEnum || type.IsValueType)
        {
            return true;
        }

        if (type == typeof(string))
        {
            return true;
        }

        if (typeof(IFormLinkIdentifier).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.FullName?.Contains("Mutagen.Bethesda.Strings", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.FullName?.Contains("MemorySlice", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return false;
    }

    private static bool ShouldSkipProperty(string path, string propertyName)
    {
        if (!string.IsNullOrEmpty(path))
        {
            return false;
        }

        return propertyName is "FormKey"
            or "FormVersion"
            or "Version2"
            or "VersionControl"
            or "MajorRecordFlagsRaw"
            or "IsCompressed"
            or "IsDeleted"
            or "TitleString";
    }

    private static object GetPropertyValue(object? target, string propertyName)
    {
        if (target is null)
        {
            return "<null>";
        }

        return target.GetType()
                   .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(target)
               ?? "<null>";
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

    private static string GetScriptObjectKey(object item)
    {
        return $"{QuestFingerprint.Exact(GetPropertyValue(item, "Object"))}|{QuestFingerprint.Exact(GetPropertyValue(item, "Alias"))}";
    }

    private static void ReplaceListContents(object target, PropertyInfo property, IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            AssignPropertyValue(target, property, null);
            return;
        }

        var list = property.GetValue(target) ?? System.Activator.CreateInstance(property.PropertyType)
            ?? throw new InvalidOperationException($"Unable to create list for {property.Name}.");

        property.SetValue(target, list);
        property.PropertyType.GetMethod("Clear", Type.EmptyTypes)?.Invoke(list, null);

        var addMethod = property.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Add")
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Unable to find Add method for {property.Name}.");

        foreach (var item in items)
        {
            addMethod.Invoke(list, [item]);
        }
    }

    private static bool AssignPropertyValue(object target, PropertyInfo property, object? value)
    {
        if (value is not null)
        {
            if (property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value);
                return true;
            }

            if (TryCoerceAssignmentValue(value, property.PropertyType, out var coerced))
            {
                property.SetValue(target, coerced);
                return true;
            }

            return false;
        }

        var current = property.GetValue(target);
        if (current is not null)
        {
            var clearMethod = current.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                ?? property.PropertyType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (clearMethod is not null)
            {
                clearMethod.Invoke(current, null);
                return true;
            }
        }

        property.SetValue(target, null);
        return true;
    }

    private static bool UsesStableUniqueBucket(string propertyPath)
    {
        return propertyPath switch
        {
            "TextDisplayGlobals" => true,
            "Aliases" => true,
            "Aliases.Keywords" => true,
            "Aliases.Factions" => true,
            "Aliases.Spells" => true,
            "Aliases.PackageData" => true,
            "Stages" => true,
            "Objectives" => true,
            "VirtualMachineAdapter.Scripts" => true,
            "VirtualMachineAdapter.Scripts.Properties" => true,
            "VirtualMachineAdapter.Scripts.Properties.Objects" => true,
            "VirtualMachineAdapter.Aliases" => true,
            "VirtualMachineAdapter.Aliases.Scripts" => true,
            "VirtualMachineAdapter.Aliases.Scripts.Properties" => true,
            "VirtualMachineAdapter.Aliases.Scripts.Properties.Objects" => true,
            "VirtualMachineAdapter.Fragments" => true,
            _ => false,
        };
    }

    private static bool TryCoerceAssignmentValue(object value, Type targetType, out object? coerced)
    {
        coerced = null;

        var nullableTarget = Nullable.GetUnderlyingType(targetType);
        var effectiveTarget = nullableTarget ?? targetType;
        if (effectiveTarget.IsInstanceOfType(value))
        {
            coerced = value;
            return true;
        }

        if (effectiveTarget.FullName?.Contains("MemorySlice", StringComparison.Ordinal) == true
            && TryCoerceMemorySlice(value, effectiveTarget, out var memorySlice))
        {
            coerced = memorySlice;
            return true;
        }

        return false;
    }

    private static bool TryCoerceMemorySlice(object value, Type targetType, out object? coerced)
    {
        coerced = null;

        var valueType = value.GetType();
        foreach (var candidate in GetMemorySliceCandidateInputs(value))
        {
            if (candidate is null)
            {
                continue;
            }

            var candidateType = candidate.GetType();

            var ctor = targetType.GetConstructor([candidateType]);
            if (ctor is not null)
            {
                coerced = ctor.Invoke([candidate]);
                return true;
            }

            var converter = targetType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (method.Name is not "op_Implicit" and not "op_Explicit")
                    {
                        return false;
                    }

                    if (method.ReturnType != targetType)
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(candidateType);
                });

            if (converter is not null)
            {
                coerced = converter.Invoke(null, [candidate]);
                return true;
            }
        }

        var valueConverters = valueType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit");

        foreach (var converter in valueConverters)
        {
            if (!targetType.IsAssignableFrom(converter.ReturnType))
            {
                continue;
            }

            var parameters = converter.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(valueType))
            {
                continue;
            }

            coerced = converter.Invoke(null, [value]);
            return true;
        }

        return false;
    }

    private static IEnumerable<object?> GetMemorySliceCandidateInputs(object value)
    {
        yield return value;

        if (value is IEnumerable<byte> bytes)
        {
            var array = bytes as byte[] ?? bytes.ToArray();
            yield return array;
            yield return new Memory<byte>(array);
            yield return new ReadOnlyMemory<byte>(array);
        }

        foreach (var methodName in new[] { "ToArray", "AsMemory" })
        {
            var method = value.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method is null)
            {
                continue;
            }

            var candidate = method.Invoke(value, null);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }
    }

    private sealed record CompiledSource(MergeSource Source, IReadOnlyList<ListEntry> Entries);

    private sealed record ListEntry(
        QuestSourceContext Context,
        object Item,
        string BucketKey,
        string ExactKey,
        int Index);
}
