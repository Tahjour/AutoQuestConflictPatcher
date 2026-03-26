using AutoQuestConflictPatcher.Configuration;
using AutoQuestConflictPatcher.Merging;
using AutoQuestConflictPatcher.Reporting;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

using Noggog;

namespace AutoQuestConflictPatcher.Tests;

public static class Program
{
    public static int Main()
    {
        var tests = new (string Name, Action Body)[]
        {
            ("HPU keeps highest unique leaf value", HpuKeepsHighestUniqueLeafValue),
            ("HPU keeps highest unique non-leaf ancestor value", HpuKeepsHighestUniqueNonLeafAncestorValue),
            ("Alias merge preserves unique flags and dedupes keywords", AliasMergePreservesUniqueFlagsAndDedupesKeywords),
            ("Alias conditions merge per condition and dedupe duplicates", AliasConditionsMergePerConditionAndDedupeDuplicates),
            ("VMAD property type conflict falls back to whole-property HPU", VmadPropertyTypeConflictFallsBackToWholePropertyHpu),
            ("No-op merge matches winning quest", NoOpMergeMatchesWinningQuest),
        };

        var failures = 0;
        foreach (var (name, body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine(ex);
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void HpuKeepsHighestUniqueLeafValue()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest => { quest.Priority = 50; }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest => { quest.Priority = 10; }));

        var merged = Merge(conflict);
        AssertEqual((byte)50, merged.Priority, "Expected HPU to keep the highest unique leaf value.");
    }

    private static void HpuKeepsHighestUniqueNonLeafAncestorValue()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("USSEP.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 50; }),
            Spec("LatePatch.esp", new[] { "Skyrim.esm", "USSEP.esp" }, quest => { quest.Priority = 10; }));

        var merged = Merge(conflict);
        AssertEqual((byte)50, merged.Priority, "Expected HPU to preserve the highest unique introduced value even when it comes from a non-leaf ancestor.");
    }

    private static void AliasMergePreservesUniqueFlagsAndDedupesKeywords()
    {
        var keywordA = Link<IKeywordGetter>("000800:Master.esm");
        var keywordB = Link<IKeywordGetter>("000801:Master.esm");

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.Aliases!.Add(NewAlias(1));
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(1);
                alias.Flags = QuestAlias.Flag.StoresText;
                alias.Keywords!.Add(keywordA);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(1);
                alias.Keywords!.Add(keywordA);
                alias.Keywords!.Add(keywordB);
                quest.Aliases!.Add(alias);
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single();
        AssertEqual(QuestAlias.Flag.StoresText, alias.Flags, "Expected alias flags to keep the unique HPU value.");
        AssertEqual(2, alias.Keywords!.Count, "Expected merged alias keywords to dedupe duplicates.");
    }

    private static void AliasConditionsMergePerConditionAndDedupeDuplicates()
    {
        var masterCondition = NewCondition(1.0f, CompareOperator.EqualTo);
        var branchCondition = NewCondition(1.0f, CompareOperator.GreaterThan);

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                var alias = NewAlias(1);
                alias.Conditions!.Add(masterCondition);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(1);
                alias.Conditions!.Add(branchCondition);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(1);
                alias.Conditions!.Add(masterCondition.DeepCopy());
                alias.Conditions!.Add(masterCondition.DeepCopy());
                quest.Aliases!.Add(alias);
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single();
        AssertEqual(1, alias.Conditions!.Count, "Expected duplicate conditions to collapse to one entry.");
        var condition = (ConditionFloat)alias.Conditions!.Single();
        AssertEqual(CompareOperator.GreaterThan, condition.CompareOperator, "Expected per-condition HPU to preserve the highest unique variant.");
    }

    private static void VmadPropertyTypeConflictFallsBackToWholePropertyHpu()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptStringProperty { Name = "PropA", Data = "Master" });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptStringProperty { Name = "PropA", Data = "BranchA" });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptIntProperty { Name = "PropA", Data = 42 });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }));

        var merged = Merge(conflict);
        var property = merged.VirtualMachineAdapter!.Scripts!.Single().Properties!.Single();
        AssertTrue(property is ScriptIntProperty, "Expected a whole-property HPU fallback to keep the higher-priority type variant.");
        AssertEqual(42, ((ScriptIntProperty)property).Data, "Expected the higher-priority property value to win.");
    }

    private static void NoOpMergeMatchesWinningQuest()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest => { quest.Priority = 10; }));

        var merged = Merge(conflict);
        AssertTrue(merged.Equals(conflict.WinningQuest), "Expected an identical override to stay unchanged after merging.");
    }

    private static QuestConflict BuildConflict(params ConflictSpec[] specs)
    {
        var formKey = FormKey.Factory("123456:Master.esm");
        var contexts = new List<QuestSourceContext>();

        for (var index = 0; index < specs.Length; index++)
        {
            var spec = specs[index];
            var quest = NewQuest(formKey);
            spec.Configure(quest);

            contexts.Add(new QuestSourceContext(
                ModKey.FromNameAndExtension(spec.ModName),
                quest,
                spec.Masters.Select(static master => ModKey.FromNameAndExtension(master)).ToArray(),
                index));
        }

        return new QuestConflict(contexts, ComputeLeafMods(contexts), QuestMergeSection.None);
    }

    private static Quest Merge(QuestConflict conflict)
    {
        return new QuestMergeEngine(new MergeReport()).Merge(conflict);
    }

    private static ConflictSpec Spec(string modName, string[] masters, Action<Quest> configure)
    {
        return new ConflictSpec(modName, masters, configure);
    }

    private static Quest NewQuest(FormKey formKey)
    {
        return new Quest(formKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "Q_Test",
            Aliases = [],
            Objectives = [],
            Stages = [],
            TextDisplayGlobals = [],
            DialogConditions = [],
            EventConditions = [],
        };
    }

    private static QuestAlias NewAlias(uint id)
    {
        return new QuestAlias
        {
            ID = id,
            Name = $"Alias{id}",
            Conditions = [],
            Keywords = [],
            Factions = [],
            Items = [],
            PackageData = [],
            Spells = [],
        };
    }

    private static QuestAdapter NewQuestAdapter()
    {
        return new QuestAdapter
        {
            Scripts = [],
            Aliases = [],
            Fragments = [],
        };
    }

    private static ScriptEntry NewScript(string name)
    {
        return new ScriptEntry
        {
            Name = name,
            Properties = [],
        };
    }

    private static ConditionFloat NewCondition(float value, CompareOperator compareOperator)
    {
        return new ConditionFloat
        {
            CompareOperator = compareOperator,
            ComparisonValue = value,
            Data = new GetDeadConditionData(),
        };
    }

    private static FormLink<T> Link<T>(string formKey) where T : class, IMajorRecordGetter
    {
        return new FormLink<T>(FormKey.Factory(formKey));
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

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ConflictSpec(string ModName, string[] Masters, Action<Quest> Configure);
}
