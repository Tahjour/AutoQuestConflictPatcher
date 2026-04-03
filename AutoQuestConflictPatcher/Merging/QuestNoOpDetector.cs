using System.Collections;
using System.Reflection;
using System.Text;

using AutoQuestConflictPatcher.Reporting;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestNoOpDetector
{
    private readonly MergeReport _report;
    private readonly QuestSnapshotBuilder _snapshotBuilder;
    private readonly Func<object?, string> _leafFingerprint;

    public QuestNoOpDetector(
        MergeReport report,
        QuestSnapshotBuilder? snapshotBuilder = null,
        Func<object?, string>? leafFingerprint = null)
    {
        _report = report;
        _snapshotBuilder = snapshotBuilder ?? new QuestSnapshotBuilder();
        _leafFingerprint = leafFingerprint ?? QuestFingerprint.Exact;
    }

    public bool IsSemanticallyEqual(IQuestGetter left, IQuestGetter right, string? label = null)
    {
        try
        {
            var leftSnapshot = _snapshotBuilder.Build(BuildContext(left, 0));
            var rightSnapshot = _snapshotBuilder.Build(BuildContext(right, 1));
            return AreSemanticallyEqual(leftSnapshot, rightSnapshot);
        }
        catch (Exception ex)
        {
            var suffix = string.IsNullOrWhiteSpace(label) ? string.Empty : $" for {label}";
            _report.Log($"Null-safe no-op comparison failed{suffix}: {ex.GetType().Name}: {ex.Message}. Treating quest as changed.");
            return false;
        }
    }

    private static QuestSourceContext BuildContext(IQuestGetter quest, int loadOrderIndex)
    {
        return new QuestSourceContext(
            quest.FormKey.ModKey,
            quest,
            Array.Empty<ModKey>(),
            loadOrderIndex);
    }

    private bool AreSemanticallyEqual(QuestSnapshot left, QuestSnapshot right)
    {
        return AreScalarMapsEqual(left.Scalars, right.Scalars)
            && AreOrderedSectionsEqual(left.TextDisplayGlobals, right.TextDisplayGlobals)
            && AreOrderedSectionsEqual(left.DialogConditions, right.DialogConditions)
            && AreOrderedSectionsEqual(left.EventConditions, right.EventConditions)
            && AreKeyedSectionsEqual(left.QuestAliases, right.QuestAliases)
            && AreKeyedSectionsEqual(left.VmadAliases, right.VmadAliases)
            && AreKeyedSectionsEqual(left.VmadScripts, right.VmadScripts)
            && AreKeyedSectionsEqual(left.Stages, right.Stages)
            && AreKeyedSectionsEqual(left.Objectives, right.Objectives)
            && AreOrderedSectionsEqual(left.FragmentSection, right.FragmentSection)
            && AreKeyedSectionsEqual(left.Fragments, right.Fragments);
    }

    private bool AreScalarMapsEqual(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in left)
        {
            if (!right.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if (!ValuesAreSemanticallyEqual(leftValue, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private bool AreOrderedSectionsEqual<T>(OrderedSectionSnapshot<T> left, OrderedSectionSnapshot<T> right)
    {
        if (left.Items.Count == 0 && right.Items.Count == 0)
        {
            return true;
        }

        if (left.Items.Count != right.Items.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Items.Count; index++)
        {
            if (!ValuesAreSemanticallyEqual(left.Items[index], right.Items[index]))
            {
                return false;
            }
        }

        return true;
    }

    private bool AreKeyedSectionsEqual<T>(KeyedSectionSnapshot<T> left, KeyedSectionSnapshot<T> right)
        where T : class
    {
        if (left.Items.Count == 0 && right.Items.Count == 0)
        {
            return true;
        }

        if (left.Items.Count != right.Items.Count)
        {
            return false;
        }

        if (!left.OrderedKeys.SequenceEqual(right.OrderedKeys, StringComparer.Ordinal))
        {
            return false;
        }

        foreach (var key in left.OrderedKeys)
        {
            if (!right.Items.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if (!ValuesAreSemanticallyEqual(left.Items[key], rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValuesAreSemanticallyEqual(object? left, object? right)
    {
        return StringComparer.Ordinal.Equals(BuildSemanticFingerprint(left), BuildSemanticFingerprint(right));
    }

    private string BuildSemanticFingerprint(object? value)
    {
        var builder = new StringBuilder();
        AppendSemantic(builder, value, declaredType: null);
        return builder.ToString();
    }

    private void AppendSemantic(StringBuilder builder, object? value, Type? declaredType)
    {
        if (IsCollectionLike(declaredType) && IsNullOrEmptyEnumerable(value))
        {
            builder.Append("[]");
            return;
        }

        if (value is null)
        {
            builder.Append("<null>");
            return;
        }

        if (TryAppendLeaf(builder, value))
        {
            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var items = EnumerateCollection(enumerable).ToArray();
            if (items.Length == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');
            for (var index = 0; index < items.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendSemantic(builder, items[index], declaredType: null);
            }

            builder.Append(']');
            return;
        }

        var type = value.GetType();
        builder.Append(type.FullName).Append('{');
        foreach (var property in type
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
                     .OrderBy(static property => property.Name))
        {
            if (ShouldIgnoreDerivedProperty(property.Name))
            {
                continue;
            }

            builder.Append(property.Name).Append('=');
            if (!TryGetPropertyValue(value, property, out var propertyValue))
            {
                if (!IsCollectionLike(property.PropertyType))
                {
                    builder.Append("<getter-error>;");
                    continue;
                }

                propertyValue = null;
            }

            AppendSemantic(builder, propertyValue, property.PropertyType);
            builder.Append(';');
        }

        builder.Append('}');
    }

    private bool TryAppendLeaf(StringBuilder builder, object value)
    {
        var type = value.GetType();
        if (type.IsPrimitive
            || type.IsEnum
            || value is decimal
            || value is string
            || value is Mutagen.Bethesda.Plugins.IFormLinkGetter
            || value is Mutagen.Bethesda.Plugins.IFormLinkIdentifier
            || value is FormKey
            || value is ModKey
            || value is Mutagen.Bethesda.Strings.ITranslatedStringGetter)
        {
            builder.Append(_leafFingerprint(value));
            return true;
        }

        if (value is IEnumerable<byte> bytes)
        {
            var materialized = bytes.ToArray();
            if (materialized.Length == 0)
            {
                builder.Append("[]");
            }
            else
            {
                builder.Append(_leafFingerprint(materialized));
            }

            return true;
        }

        return false;
    }

    private static IEnumerable<object> EnumerateCollection(IEnumerable enumerable)
    {
        foreach (var item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static bool IsCollectionLike(Type? type)
    {
        if (type is null || type == typeof(string))
        {
            return false;
        }

        return typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsNullOrEmptyEnumerable(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is string)
        {
            return false;
        }

        if (value is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();
            try
            {
                return !enumerator.MoveNext();
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        return false;
    }

    private static bool TryGetPropertyValue(object target, PropertyInfo property, out object? value)
    {
        try
        {
            value = property.GetValue(target);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private static bool ShouldIgnoreDerivedProperty(string propertyName)
    {
        return propertyName.EndsWith("Count", StringComparison.Ordinal);
    }
}
