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
            ("Empty alias keyword lists stay absent", EmptyAliasKeywordListsStayAbsent),
            ("Stage log entries preserve deliberate exact duplicates", StageLogEntriesPreserveDeliberateExactDuplicates),
            ("Condition removals beat sibling ITM retention", ConditionRemovalsBeatSiblingItmRetention),
            ("Condition add remove readd keeps reintroduced value", ConditionAddRemoveReaddKeepsReintroducedValue),
            ("VMAD property buckets collapse duplicate property names", VmadPropertyBucketsCollapseDuplicatePropertyNames),
            ("VMAD property canonical collapse removes logical duplicates", VmadPropertyCanonicalCollapseRemovesLogicalDuplicates),
            ("VMAD alias buckets preserve distinct alias bindings", VmadAliasBucketsPreserveDistinctAliasBindings),
            ("VMAD alias property stays valid when ancestor bucket wins", VmadAliasPropertyStaysValidWhenAncestorBucketWins),
            ("VMAD alias empty script lists stay allocated", VmadAliasEmptyScriptListsStayAllocated),
            ("VMAD sanitizer removes aliases with null property payloads", VmadSanitizerRemovesAliasesWithNullPropertyPayloads),
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

    private static void EmptyAliasKeywordListsStayAbsent()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.Aliases!.Add(new QuestAlias
                {
                    ID = 1,
                    Name = "Alias1",
                });
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.Aliases!.Add(new QuestAlias
                {
                    ID = 1,
                    Name = "Alias1",
                });
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single();
        AssertTrue(alias.Keywords is null, "Expected empty keyword lists to stay absent instead of materializing an empty field.");
    }

    private static void StageLogEntriesPreserveDeliberateExactDuplicates()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.Stages!.Add(NewStageWithDuplicateLogEntries(10));
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.Stages!.Add(NewStageWithDuplicateLogEntries(10));
            }));

        var merged = Merge(conflict);
        var logEntries = merged.Stages!.Single().LogEntries!;
        AssertEqual(2, logEntries.Count, "Expected deliberate duplicate stage log entries to survive merging.");
    }

    private static void ConditionRemovalsBeatSiblingItmRetention()
    {
        var retainedCondition = NewCondition(1.0f, CompareOperator.EqualTo);

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.DialogConditions!.Add(retainedCondition);
            }),
            Spec("RemoveBranch.esp", new[] { "Master.esm" }, quest => { }),
            Spec("KeepSibling.esp", new[] { "Master.esm" }, quest =>
            {
                quest.DialogConditions!.Add(retainedCondition.DeepCopy());
            }));

        var merged = Merge(conflict);
        AssertEqual(0, merged.DialogConditions!.Count, "Expected a deliberate condition removal to beat sibling ITM retention.");
    }

    private static void ConditionAddRemoveReaddKeepsReintroducedValue()
    {
        var addedCondition = NewCondition(2.0f, CompareOperator.GreaterThan);

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { }),
            Spec("AddBranch.esp", new[] { "Master.esm" }, quest =>
            {
                quest.DialogConditions!.Add(addedCondition);
            }),
            Spec("RemoveBranch.esp", new[] { "Master.esm", "AddBranch.esp" }, quest => { }),
            Spec("ReaddSibling.esp", new[] { "Master.esm" }, quest =>
            {
                quest.DialogConditions!.Add(addedCondition.DeepCopy());
            }));

        var merged = Merge(conflict);
        AssertEqual(1, merged.DialogConditions!.Count, "Expected a reintroduced condition to survive after an intermediate removal.");
        var condition = (ConditionFloat)merged.DialogConditions!.Single();
        AssertEqual(CompareOperator.GreaterThan, condition.CompareOperator, "Expected the reintroduced condition variant to win.");
    }

    private static void VmadPropertyBucketsCollapseDuplicatePropertyNames()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptStringProperty { Name = "PropA", Data = "First" });
                script.Properties!.Add(new ScriptStringProperty { Name = "PropA", Data = "Second" });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptStringProperty { Name = "PropA", Data = "Patch" });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }));

        var merged = Merge(conflict);
        var properties = merged.VirtualMachineAdapter!.Scripts!.Single().Properties!;
        AssertEqual(1, properties.Count, "Expected duplicate VMAD property names to collapse into a single merged property.");
    }

    private static void VmadPropertyCanonicalCollapseRemovesLogicalDuplicates()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptObjectProperty { Name = "Alias_banditA", Object = Link<IQuestGetter>("123456:Master.esm"), Alias = 5 });
                script.Properties!.Add(new ScriptObjectProperty { Name = "Alias_banditA ", Object = Link<IQuestGetter>("123456:Master.esm"), Alias = 5 });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var script = NewScript("ScriptA");
                script.Properties!.Add(new ScriptObjectProperty { Name = "Alias_banditA", Object = Link<IQuestGetter>("123456:Master.esm"), Alias = 5 });
                quest.VirtualMachineAdapter!.Scripts!.Add(script);
            }));

        var merged = Merge(conflict);
        var properties = merged.VirtualMachineAdapter!.Scripts!.Single().Properties!;
        AssertEqual(1, properties.Count, "Expected logically identical VMAD properties to collapse to one final property.");
    }

    private static void VmadAliasBucketsPreserveDistinctAliasBindings()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderOne", 10));
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderPlayer", 13));
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderOne", 10));
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderPlayer", 13));
            }));

        var merged = Merge(conflict);
        var aliases = merged.VirtualMachineAdapter!.Aliases!;
        AssertEqual(2, aliases.Count, "Expected distinct VMAD alias bindings to survive list bucketing instead of collapsing together.");
        AssertEqual((short)10, aliases[0].Property!.Alias, "Expected the first VMAD alias binding to remain intact.");
        AssertEqual((short)13, aliases[1].Property!.Alias, "Expected the second VMAD alias binding to remain intact.");
    }

    private static void VmadAliasPropertyStaysValidWhenAncestorBucketWins()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { }),
            Spec("AddAncestor.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderOne", 10));
            }),
            Spec("LeafDescendant.esp", new[] { "Master.esm", "AddAncestor.esp" }, quest =>
            {
                quest.Priority = 25;
            }),
            Spec("Sibling.esp", new[] { "Master.esm" }, quest =>
            {
                quest.Priority = 50;
            }));

        var merged = Merge(conflict);
        var alias = merged.VirtualMachineAdapter!.Aliases!.Single();
        AssertTrue(alias.Property is not null, "Expected the surviving VMAD alias bucket to keep a non-null property payload.");
        AssertEqual((short)10, alias.Property!.Alias, "Expected the surviving VMAD alias property payload to remain intact.");
    }

    private static void VmadAliasEmptyScriptListsStayAllocated()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(new QuestFragmentAlias
                {
                    Property = new ScriptObjectProperty
                    {
                        Name = "CartRiderOne",
                        Object = Link<IQuestGetter>("123456:Master.esm"),
                        Alias = 10,
                    },
                    Scripts = [],
                });
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(new QuestFragmentAlias
                {
                    Property = new ScriptObjectProperty
                    {
                        Name = "CartRiderOne",
                        Object = Link<IQuestGetter>("123456:Master.esm"),
                        Alias = 10,
                    },
                    Scripts = [],
                });
            }));

        var merged = Merge(conflict);
        var alias = merged.VirtualMachineAdapter!.Aliases!.Single();
        AssertTrue(alias.Scripts is not null, "Expected VMAD alias script lists to stay allocated even when empty.");
        AssertEqual(0, alias.Scripts!.Count, "Expected an empty VMAD alias script list to remain empty instead of becoming null.");
    }

    private static void VmadSanitizerRemovesAliasesWithNullPropertyPayloads()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(new QuestFragmentAlias
                {
                    Scripts = [],
                });
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(new QuestFragmentAlias
                {
                    Scripts = [],
                });
            }));

        var merged = Merge(conflict);
        AssertEqual(0, merged.VirtualMachineAdapter!.Aliases!.Count, "Expected invalid VMAD aliases with null property payloads to be removed before write.");
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

    private static QuestFragmentAlias NewQuestFragmentAlias(string name, short alias)
    {
        return new QuestFragmentAlias
        {
            Property = new ScriptObjectProperty
            {
                Name = name,
                Object = Link<IQuestGetter>("123456:Master.esm"),
                Alias = alias,
            },
            Scripts =
            [
                new ScriptEntry
                {
                    Name = "CartRiderScript",
                    Properties = [],
                },
            ],
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

    private static QuestStage NewStageWithDuplicateLogEntries(ushort index)
    {
        var logEntry = new QuestLogEntry
        {
            Entry = "Duplicate",
            Conditions = [NewCondition(10.0f, CompareOperator.NotEqualTo)],
        };

        return new QuestStage
        {
            Index = index,
            LogEntries =
            [
                logEntry,
                logEntry.DeepCopy(),
            ],
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
