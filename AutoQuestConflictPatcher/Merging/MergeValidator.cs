using System.Reflection;

using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class MergeValidator
{
    public QuestValidationResult Validate(Quest quest)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (quest.Aliases is not null)
        {
            var duplicateAliases = quest.Aliases
                .GroupBy(static alias => alias.ID)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();
            foreach (var duplicateAlias in duplicateAliases)
            {
                errors.Add($"Duplicate alias ID {duplicateAlias}.");
            }
        }

        var aliasIds = quest.Aliases?.Select(static alias => alias.ID).ToHashSet() ?? [];
        if (quest.VirtualMachineAdapter?.Aliases is not null)
        {
            for (var index = 0; index < quest.VirtualMachineAdapter.Aliases.Count; index++)
            {
                var alias = quest.VirtualMachineAdapter.Aliases[index];
                if (alias.Property is null)
                {
                    errors.Add($"VMAD alias #{index} has no property payload.");
                    continue;
                }

                if (alias.Property.Object.FormKey == default)
                {
                    errors.Add($"VMAD alias #{index} has no bound object FormKey.");
                }

                if (alias.Property.Alias < 0)
                {
                    errors.Add($"VMAD alias #{index} has an invalid alias binding.");
                }
                else if (!aliasIds.Contains((uint)alias.Property.Alias))
                {
                    warnings.Add($"VMAD alias #{index} points to missing quest alias ID {alias.Property.Alias}.");
                }

                if (alias.Scripts is null)
                {
                    errors.Add($"VMAD alias #{index} has no script collection.");
                    continue;
                }

                foreach (var script in alias.Scripts)
                {
                    ValidateScript(script, $"VMAD alias #{index}", errors);
                }
            }
        }

        if (quest.VirtualMachineAdapter?.Scripts is not null)
        {
            foreach (var script in quest.VirtualMachineAdapter.Scripts)
            {
                ValidateScript(script, "Quest VMAD script", errors);
            }
        }

        var stageIds = quest.Stages?.Select(static stage => stage.Index).ToHashSet() ?? [];
        if (quest.VirtualMachineAdapter?.Fragments is not null)
        {
            foreach (var fragment in quest.VirtualMachineAdapter.Fragments)
            {
                var stage = Convert.ToUInt16(GetNumericProperty(fragment, "Stage"));
                var stageIndex = Convert.ToUInt16(GetNumericProperty(fragment, "StageIndex"));
                if (stage != 0 && !stageIds.Contains(stage) && stageIndex != 0 && !stageIds.Contains(stageIndex))
                {
                    warnings.Add($"Fragment {QuestFingerprint.Exact(fragment)} points to a missing stage.");
                }
            }
        }

        return errors.Count == 0 && warnings.Count == 0
            ? QuestValidationResult.Success
            : new QuestValidationResult(errors, warnings);
    }

    public bool AreCompatible(QuestAlias? left, QuestAlias? right)
    {
        if (left is null || right is null)
        {
            return true;
        }

        if (left.ID != right.ID)
        {
            return false;
        }

        return string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(left.Name)
            || string.IsNullOrWhiteSpace(right.Name);
    }

    public bool AreCompatible(QuestFragmentAlias? left, QuestFragmentAlias? right)
    {
        if (left is null || right is null)
        {
            return true;
        }

        if (left.Property is null || right.Property is null)
        {
            return false;
        }

        if (left.Property.Alias != right.Property.Alias || left.Property.Object.FormKey != right.Property.Object.FormKey)
        {
            return false;
        }

        var leftScripts = left.Scripts?.Select(static script => script.Name).OrderBy(static name => name).ToArray() ?? [];
        var rightScripts = right.Scripts?.Select(static script => script.Name).OrderBy(static name => name).ToArray() ?? [];
        return leftScripts.SequenceEqual(rightScripts, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateScript(ScriptEntry script, string owner, ICollection<string> errors)
    {
        if (script.Properties is null)
        {
            errors.Add($"{owner} script {script.Name} has no property collection.");
            return;
        }

        var duplicatePropertyKeys = script.Properties
            .Select(property => $"{property.Name}:{property.GetType().Name}")
            .GroupBy(static key => key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        foreach (var duplicatePropertyKey in duplicatePropertyKeys)
        {
            errors.Add($"{owner} script {script.Name} has duplicate property {duplicatePropertyKey}.");
        }
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
}
