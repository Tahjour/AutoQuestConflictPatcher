using AutoQuestConflictPatcher.Configuration;
using AutoQuestConflictPatcher.Reporting;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AutoQuestConflictPatcher.Merging;

public sealed class QuestConflictScanner
{
    public IReadOnlyList<QuestConflict> Scan(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        AutoQuestConflictPatcherSettings settings,
        MergeReport report)
    {
        var listings = state.LoadOrder.ListedOrder
            .Select((listing, index) => new ListingData(listing.ModKey, index, listing.Mod))
            .Where(data => data.Mod is not null)
            .ToDictionary(data => data.ModKey);

        var conflicts = new List<QuestConflict>();
        foreach (var winningContext in state.LoadOrder.PriorityOrder.Quest().WinningContextOverrides(includeDeletedRecords: false))
        {
            var winningQuest = winningContext.Record;
            if (settings.IsIgnored(winningQuest.FormKey, winningQuest.EditorID))
            {
                report.Log($"Skipped ignored quest {winningQuest.EditorID ?? winningQuest.FormKey.ToString()}.");
                continue;
            }

            var contexts = state.LinkCache.ResolveAllSimpleContexts<IQuestGetter>(winningQuest.FormKey)
                .Where(context => listings.ContainsKey(context.ModKey))
                .Select(context =>
                {
                    var listing = listings[context.ModKey];
                    return new QuestSourceContext(
                        context.ModKey,
                        context.Record,
                        listing.Mod!.MasterReferences.Select(reference => reference.Master).ToArray(),
                        listing.Index);
                })
                .OrderBy(context => context.LoadOrderIndex)
                .ToList();

            if (contexts.Count <= 1)
            {
                continue;
            }

            conflicts.Add(new QuestConflict(
                contexts,
                ComputeLeafMods(contexts),
                settings.GetWinnerOnlySections(winningQuest.FormKey, winningQuest.EditorID)));
        }

        return conflicts;
    }

    private static IReadOnlySet<ModKey> ComputeLeafMods(IReadOnlyList<QuestSourceContext> contexts)
    {
        var leafs = contexts.Select(context => context.ModKey).ToHashSet();
        var available = leafs.ToHashSet();

        foreach (var context in contexts)
        {
            foreach (var master in context.Masters)
            {
                if (available.Contains(master))
                {
                    leafs.Remove(master);
                }
            }
        }

        return leafs;
    }

    private sealed record ListingData(ModKey ModKey, int Index, ISkyrimModGetter? Mod);
}
