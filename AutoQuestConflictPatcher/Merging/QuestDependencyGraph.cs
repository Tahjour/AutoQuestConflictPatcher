using System.Reflection;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestDependencyGraph
{
    private static readonly IReadOnlySet<ComponentKey> Empty = new HashSet<ComponentKey>();

    public QuestDependencyGraph(IReadOnlyDictionary<ComponentKey, IReadOnlySet<ComponentKey>> edges)
    {
        Edges = edges;
    }

    public IReadOnlyDictionary<ComponentKey, IReadOnlySet<ComponentKey>> Edges { get; }

    public IReadOnlySet<ComponentKey> GetRelated(ComponentKey key)
    {
        return Edges.TryGetValue(key, out var related)
            ? related
            : Empty;
    }

    public static QuestDependencyGraph Build(IReadOnlyList<QuestSnapshot> snapshots)
    {
        var edges = new Dictionary<ComponentKey, HashSet<ComponentKey>>();
        foreach (var snapshot in snapshots)
        {
            foreach (var stage in snapshot.Stages.Items.Values)
            {
                var stageKey = new ComponentKey("Stage", $"Stage:{stage.Index}");
                foreach (var fragment in snapshot.Fragments.Items.Values)
                {
                    var stageValue = GetNumericProperty(fragment, "Stage");
                    var stageIndexValue = GetNumericProperty(fragment, "StageIndex");
                    if (stageValue != stage.Index && stageIndexValue != stage.Index)
                    {
                        continue;
                    }

                    AddEdge(edges, stageKey, new ComponentKey("Fragment", BuildFragmentKey(fragment)));
                }
            }

            foreach (var objective in snapshot.Objectives.Items.Values)
            {
                var objectiveKey = new ComponentKey("Objective", $"Objective:{objective.Index}");
                if (objective.Targets is null)
                {
                    continue;
                }

                foreach (var target in objective.Targets)
                {
                    var aliasId = Convert.ToUInt32(GetNumericProperty(target, "AliasID"));
                    AddEdge(edges, objectiveKey, new ComponentKey("QuestAlias", $"Alias:{aliasId}"));
                }
            }
        }

        return new QuestDependencyGraph(edges.ToDictionary(static pair => pair.Key, static pair => (IReadOnlySet<ComponentKey>)pair.Value));
    }

    private static void AddEdge(
        IDictionary<ComponentKey, HashSet<ComponentKey>> edges,
        ComponentKey source,
        ComponentKey destination)
    {
        if (!edges.TryGetValue(source, out var bucket))
        {
            bucket = new HashSet<ComponentKey>();
            edges[source] = bucket;
        }

        bucket.Add(destination);
    }

    private static int GetNumericProperty(object target, string propertyName)
    {
        var value = target.GetType()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(target);
        return value is null
            ? -1
            : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string BuildFragmentKey(object fragment)
    {
        return $"Fragment:{GetProperty(fragment, "Stage")}:{GetProperty(fragment, "StageIndex")}:{GetProperty(fragment, "ScriptName")}:{GetProperty(fragment, "FragmentName")}";
    }

    private static object GetProperty(object target, string propertyName)
    {
        return target.GetType()
                   .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                   ?.GetValue(target)
               ?? "<null>";
    }
}
