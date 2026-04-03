using System.Collections;
using System.Reflection;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestSnapshotBuilder
{
    private static readonly HashSet<string> SkippedScalarProperties =
    [
        "FormKey",
        "FormVersion",
        "Version2",
        "VersionControl",
        "MajorRecordFlagsRaw",
        "IsCompressed",
        "IsDeleted",
        "TitleString",
        "Aliases",
        "Objectives",
        "Stages",
        "TextDisplayGlobals",
        "DialogConditions",
        "EventConditions",
        "VirtualMachineAdapter",
    ];

    public QuestSnapshot Build(QuestSourceContext context)
    {
        var quest = context.Quest;
        return new QuestSnapshot(
            context,
            BuildScalars(quest),
            BuildOrderedSection(quest.TextDisplayGlobals),
            BuildOrderedSection(quest.DialogConditions),
            BuildOrderedSection(quest.EventConditions),
            BuildKeyedSection<QuestAlias>(quest.Aliases, BuildQuestAliasKey),
            BuildKeyedSection<QuestFragmentAlias>(quest.VirtualMachineAdapter?.Aliases, BuildVmadAliasKey),
            BuildKeyedSection<ScriptEntry>(quest.VirtualMachineAdapter?.Scripts, static script => $"Script:{script.Name}"),
            BuildKeyedSection<QuestStage>(quest.Stages, static stage => $"Stage:{stage.Index}"),
            BuildKeyedSection<QuestObjective>(quest.Objectives, static objective => $"Objective:{objective.Index}"),
            BuildOrderedTypedSection<QuestScriptFragment>(quest.VirtualMachineAdapter?.Fragments),
            BuildKeyedSection<QuestScriptFragment>(quest.VirtualMachineAdapter?.Fragments, BuildFragmentKey));
    }

    private static IReadOnlyDictionary<string, object?> BuildScalars(IQuestGetter quest)
    {
        var scalars = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in quest.GetType()
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name))
        {
            if (SkippedScalarProperties.Contains(property.Name))
            {
                continue;
            }

            if (!IsLeafType(property.PropertyType))
            {
                continue;
            }

            scalars[property.Name] = Clone(property.GetValue(quest));
        }

        return scalars;
    }

    private static OrderedSectionSnapshot<object> BuildOrderedSection(IEnumerable? enumerable)
    {
        if (enumerable is null)
        {
            return new OrderedSectionSnapshot<object>(present: false, []);
        }

        var items = new List<object>();
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            items.Add(Clone(item)!);
        }

        return new OrderedSectionSnapshot<object>(present: true, items);
    }

    private static OrderedSectionSnapshot<T> BuildOrderedTypedSection<T>(IEnumerable? enumerable)
        where T : class
    {
        if (enumerable is null)
        {
            return new OrderedSectionSnapshot<T>(present: false, []);
        }

        var items = new List<T>();
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                continue;
            }

            var clone = Clone(item) as T;
            if (clone is null)
            {
                continue;
            }

            items.Add(clone);
        }

        return new OrderedSectionSnapshot<T>(present: true, items);
    }

    private static KeyedSectionSnapshot<T> BuildKeyedSection<T>(
        IEnumerable? enumerable,
        Func<T, string> keyFactory)
        where T : class
    {
        if (enumerable is null)
        {
            return new KeyedSectionSnapshot<T>(present: false, new Dictionary<string, T>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal), []);
        }

        var items = new Dictionary<string, T>(StringComparer.Ordinal);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderedKeys = new List<string>();
        var index = 0;
        foreach (var item in enumerable)
        {
            if (item is null)
            {
                index++;
                continue;
            }

            var clone = Clone(item) as T;
            if (clone is null)
            {
                index++;
                continue;
            }

            var key = keyFactory(clone);
            if (!order.ContainsKey(key))
            {
                order[key] = index;
                orderedKeys.Add(key);
            }

            items[key] = clone;
            index++;
        }

        return new KeyedSectionSnapshot<T>(present: true, items, order, orderedKeys);
    }

    private static object? Clone(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return DeepCopyHelper.DeepCopyObject(value);
        }
        catch
        {
            return value;
        }
    }

    private static string BuildQuestAliasKey(QuestAlias alias)
    {
        return $"Alias:{alias.ID}";
    }

    private static string BuildVmadAliasKey(QuestFragmentAlias alias)
    {
        if (alias.Property is not null && alias.Property.Alias >= 0)
        {
            return $"Alias:{alias.Property.Alias}";
        }

        var formKey = alias.Property?.Object.FormKey.ToString() ?? "<null>";
        return $"VmadAlias:{formKey}:{QuestFingerprint.Exact(alias)}";
    }

    private static string BuildFragmentKey(QuestScriptFragment fragment)
    {
        return $"Fragment:{GetPropertyValue(fragment, "Stage")}:{GetPropertyValue(fragment, "StageIndex")}:{GetPropertyValue(fragment, "ScriptName")}:{GetPropertyValue(fragment, "FragmentName")}";
    }

    private static object GetPropertyValue(object target, string propertyName)
    {
        return target.GetType()
                   .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(target)
               ?? "<null>";
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

        return type.FullName?.Contains("Mutagen.Bethesda.Strings", StringComparison.Ordinal) == true
            || type.FullName?.Contains("MemorySlice", StringComparison.Ordinal) == true;
    }
}
