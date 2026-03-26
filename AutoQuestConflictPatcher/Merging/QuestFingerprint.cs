using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public static class QuestFingerprint
{
    public static string Exact(object? value)
    {
        var builder = new StringBuilder();
        Append(builder, value, static _ => false);
        return builder.ToString();
    }

    public static string ConditionNear(object? value)
    {
        var builder = new StringBuilder();
        Append(builder, value, static name =>
            name == "CompareOperator"
            || name == "Unknown1"
            || name == "Unknown2"
            || name.StartsWith("Unknown", StringComparison.Ordinal)
            || name.StartsWith("FirstUnused", StringComparison.Ordinal)
            || name.StartsWith("SecondUnused", StringComparison.Ordinal));
        return builder.ToString();
    }

    public static string LogEntryNear(object? value)
    {
        if (value is not IQuestLogEntryGetter entry)
        {
            return Exact(value);
        }

        var builder = new StringBuilder();
        builder.Append("LogEntry{Conditions=[");
        var first = true;
        foreach (var condition in entry.Conditions)
        {
            if (!first)
            {
                builder.Append('|');
            }

            first = false;
            builder.Append(ConditionNear(condition));
        }

        builder.Append("]}");
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, object? value, Func<string, bool> skipProperty)
    {
        if (value is null)
        {
            builder.Append("<null>");
            return;
        }

        switch (value)
        {
            case string text:
                builder.Append('"').Append(text).Append('"');
                return;
            case ITranslatedStringGetter translated:
                builder.Append('"').Append(translated.String ?? string.Empty).Append('"');
                return;
            case IFormLinkGetter formLink:
                builder.Append(formLink.FormKey);
                return;
            case IFormLinkIdentifier formLinkIdentifier:
                builder.Append(formLinkIdentifier.FormKey);
                return;
            case FormKey formKey:
                builder.Append(formKey);
                return;
            case ModKey modKey:
                builder.Append(modKey);
                return;
            case IEnumerable<byte> bytes:
                builder.Append(Convert.ToHexString(bytes.ToArray()));
                return;
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            builder.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture));
            return;
        }

        if (type.IsPrimitive || value is decimal)
        {
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            return;
        }

        if (value is IEnumerable enumerable)
        {
            builder.Append('[');
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                Append(builder, item, skipProperty);
            }

            builder.Append(']');
            return;
        }

        builder.Append(type.FullName).Append('{');
        foreach (var property in type
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name))
        {
            if (skipProperty(property.Name))
            {
                continue;
            }

            builder.Append(property.Name).Append('=');
            Append(builder, property.GetValue(value), skipProperty);
            builder.Append(';');
        }

        builder.Append('}');
    }
}
