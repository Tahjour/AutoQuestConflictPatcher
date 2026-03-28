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
            ("HPMO favors repeated meaningful value over ITM-only occurrences", HpmoFavorsRepeatedMeaningfulValueOverItmOnlyOccurrences),
            ("HPMO preserves shared ITM across all sources", HpmoPreservesSharedItmAcrossAllSources),
            ("HPMO ties break toward the highest-priority meaningful value", HpmoTiesBreakTowardHighestPriorityMeaningfulValue),
            ("HPMO counts non-official restore as meaningful", HpmoCountsNonOfficialRestoreAsMeaningful),
            ("Official restore does not beat a meaningful change unless shared", OfficialRestoreDoesNotBeatMeaningfulChangeUnlessShared),
            ("Alias merge preserves unique flags and dedupes keywords", AliasMergePreservesUniqueFlagsAndDedupesKeywords),
            ("Alias conditions merge per condition and dedupe duplicates", AliasConditionsMergePerConditionAndDedupeDuplicates),
            ("Empty alias keyword lists stay absent", EmptyAliasKeywordListsStayAbsent),
            ("Alias package data keeps HPMO insertion slot when winner omits alias", AliasPackageDataKeepsHpmoInsertionSlotWhenWinnerOmitsAlias),
            ("Aliases keep HPMO slot when later winner omits bucket", AliasesKeepHpmoSlotWhenLaterWinnerOmitsBucket),
            ("Later ITM slot occurrence does not undo earlier meaningful slot", LaterItmSlotOccurrenceDoesNotUndoEarlierMeaningfulSlot),
            ("Remove and readd keeps reintroduced slot order", RemoveAndReaddKeepsReintroducedSlotOrder),
            ("Stage log entries preserve deliberate exact duplicates", StageLogEntriesPreserveDeliberateExactDuplicates),
            ("Condition removals beat sibling ITM retention", ConditionRemovalsBeatSiblingItmRetention),
            ("Condition add remove readd keeps reintroduced value", ConditionAddRemoveReaddKeepsReintroducedValue),
            ("VMAD property buckets collapse duplicate property names", VmadPropertyBucketsCollapseDuplicatePropertyNames),
            ("VMAD property canonical collapse removes logical duplicates", VmadPropertyCanonicalCollapseRemovesLogicalDuplicates),
            ("VMAD alias buckets preserve distinct alias bindings", VmadAliasBucketsPreserveDistinctAliasBindings),
            ("VMAD quest fragment aliases survive blank property names", VmadQuestFragmentAliasesSurviveBlankPropertyNames),
            ("VMAD alias nested subfields survive mixed ITM and unique branches", VmadAliasNestedSubfieldsSurviveMixedItmAndUniqueBranches),
            ("VMAD alias nested subfields survive unrelated later omissions", VmadAliasNestedSubfieldsSurviveUnrelatedLaterOmissions),
            ("VMAD alias property stays valid when ancestor bucket wins", VmadAliasPropertyStaysValidWhenAncestorBucketWins),
            ("VMAD alias empty script lists stay allocated", VmadAliasEmptyScriptListsStayAllocated),
            ("VMAD sanitizer removes aliases with null property payloads", VmadSanitizerRemovesAliasesWithNullPropertyPayloads),
            ("VMAD property type conflict falls back to whole-property HPMO", VmadPropertyTypeConflictFallsBackToWholePropertyHpmo),
            ("Stage index metadata survives unrelated later omissions", StageIndexMetadataSurvivesUnrelatedLaterOmissions),
            ("Stage bucket can be removed by a present later stages container", StageBucketCanBeRemovedByPresentLaterStagesContainer),
            ("Mutagen contract exposes quest VMAD alias and stage members", MutagenContractExposesQuestVmadAliasAndStageMembers),
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

    private static void HpmoFavorsRepeatedMeaningfulValueOverItmOnlyOccurrences()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest => { quest.Priority = 50; }),
            Spec("PatchB.esp", new[] { "Master.esm", "PatchA.esp" }, quest => { quest.Priority = 50; }));

        var merged = Merge(conflict);
        AssertEqual((byte)50, merged.Priority, "Expected repeated meaningful support to beat ITM-only occurrences.");
    }

    private static void HpmoPreservesSharedItmAcrossAllSources()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 10; }),
            Spec("PatchB.esp", new[] { "Skyrim.esm", "PatchA.esp" }, quest => { quest.Priority = 10; }));

        var merged = Merge(conflict);
        AssertEqual((byte)10, merged.Priority, "Expected a shared ITM value across all sources to be preserved.");
    }

    private static void HpmoTiesBreakTowardHighestPriorityMeaningfulValue()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest => { quest.Priority = 50; }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest => { quest.Priority = 70; }));

        var merged = Merge(conflict);
        AssertEqual((byte)70, merged.Priority, "Expected equal meaningful counts to break toward the highest-priority meaningful occurrence.");
    }

    private static void HpmoCountsNonOfficialRestoreAsMeaningful()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest => { quest.Priority = 40; }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest => { quest.Priority = 70; }),
            Spec("PatchC.esp", new[] { "Master.esm", "PatchA.esp" }, quest => { quest.Priority = 40; }));

        var merged = Merge(conflict);
        AssertEqual((byte)40, merged.Priority, "Expected restoring an older non-official mod value over a later conflict to count as meaningful support.");
    }

    private static void OfficialRestoreDoesNotBeatMeaningfulChangeUnlessShared()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest => { quest.Priority = 10; }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 40; }),
            Spec("PatchB.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 70; }),
            Spec("PatchC.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 10; }));

        var merged = Merge(conflict);
        AssertEqual((byte)70, merged.Priority, "Expected an official-master restore to lose to a competing meaningful change unless every source keeps the official value.");
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
        AssertEqual(QuestAlias.Flag.StoresText, alias.Flags, "Expected alias flags to keep the HPMO-selected unique value.");
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
        AssertEqual(CompareOperator.GreaterThan, condition.CompareOperator, "Expected per-condition HPMO to preserve the strongest supported variant.");
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

    private static void AliasPackageDataKeepsHpmoInsertionSlotWhenWinnerOmitsAlias()
    {
        var basePackage = Link<IPackageGetter>("000100:Master.esm");
        var insertedA = Link<IPackageGetter>("000101:PatchA.esp");
        var insertedB = Link<IPackageGetter>("000102:PatchA.esp");

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(insertedA);
                alias.PackageData!.Add(insertedB);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("Winner.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single(candidate => candidate.ID == 18);
        AssertEqual(3, alias.PackageData!.Count, "Expected merged alias package data to keep all surviving packages.");
        AssertEqual(insertedA.FormKey, ((IFormLinkGetter)alias.PackageData![0]).FormKey, "Expected the first inserted package to stay at the front of the merged list.");
        AssertEqual(insertedB.FormKey, ((IFormLinkGetter)alias.PackageData![1]).FormKey, "Expected the second inserted package to stay ahead of the inherited package.");
        AssertEqual(basePackage.FormKey, ((IFormLinkGetter)alias.PackageData![2]).FormKey, "Expected the inherited package to keep the later HPMO slot instead of being appended first.");
    }

    private static void AliasesKeepHpmoSlotWhenLaterWinnerOmitsBucket()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.Aliases!.Add(NewAlias(1));
                quest.Aliases!.Add(NewAlias(2));
                quest.Aliases!.Add(NewAlias(3));
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.Aliases!.Add(NewAlias(1));
                quest.Aliases!.Add(NewAlias(17));
                quest.Aliases!.Add(NewAlias(2));
                quest.Aliases!.Add(NewAlias(3));
            }),
            Spec("Winner.esp", new[] { "Master.esm" }, quest =>
            {
                quest.Aliases!.Add(NewAlias(1));
                quest.Aliases!.Add(NewAlias(2));
                quest.Aliases!.Add(NewAlias(3));
                quest.Priority = 25;
            }));

        var merged = Merge(conflict);
        AssertSequenceEqual(new uint[] { 1, 17, 2, 3 }, merged.Aliases!.Select(alias => alias.ID).ToArray(), "Expected the added alias to stay in its HPMO-selected slot instead of moving to the end.");
    }

    private static void LaterItmSlotOccurrenceDoesNotUndoEarlierMeaningfulSlot()
    {
        var basePackage = Link<IPackageGetter>("000110:Master.esm");
        var inserted = Link<IPackageGetter>("000111:PatchA.esp");

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(inserted);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchB.esp", new[] { "Master.esm", "PatchA.esp" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(inserted);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single();
        AssertEqual(inserted.FormKey, ((IFormLinkGetter)alias.PackageData![0]).FormKey, "Expected the repeated ITM slot occurrence to preserve the earlier unique insertion order.");
        AssertEqual(basePackage.FormKey, ((IFormLinkGetter)alias.PackageData![1]).FormKey, "Expected the inherited package to keep the moved slot after an ITM repeat.");
    }

    private static void RemoveAndReaddKeepsReintroducedSlotOrder()
    {
        var basePackage = Link<IPackageGetter>("000120:Master.esm");
        var inserted = Link<IPackageGetter>("000121:PatchA.esp");

        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(inserted);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchB.esp", new[] { "Master.esm", "PatchA.esp" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(inserted);
                quest.Aliases!.Add(alias);
            }),
            Spec("PatchC.esp", new[] { "Master.esm", "PatchA.esp", "PatchB.esp" }, quest =>
            {
                var alias = NewAlias(18);
                alias.PackageData!.Add(inserted);
                alias.PackageData!.Add(basePackage);
                quest.Aliases!.Add(alias);
            }));

        var merged = Merge(conflict);
        var alias = merged.Aliases!.Single();
        AssertEqual(inserted.FormKey, ((IFormLinkGetter)alias.PackageData![0]).FormKey, "Expected the surviving inserted package to remain in the reintroduced slot.");
        AssertEqual(basePackage.FormKey, ((IFormLinkGetter)alias.PackageData![1]).FormKey, "Expected the reintroduced package to keep the later slot chosen by HPMO.");
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

    private static void VmadAliasNestedSubfieldsSurviveMixedItmAndUniqueBranches()
    {
        var conflict = BuildConflict(
            Spec("Master.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderOne", 10));
            }),
            Spec("PatchA.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var alias = NewQuestFragmentAlias("CartRiderOne", 10);
                alias.Scripts!.Single().Properties!.Add(new ScriptIntProperty
                {
                    Name = "StageToSetOnDeath",
                    Data = 52,
                });
                quest.VirtualMachineAdapter!.Aliases!.Add(alias);
            }),
            Spec("PatchB.esp", new[] { "Master.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                quest.VirtualMachineAdapter!.Aliases!.Add(NewQuestFragmentAlias("CartRiderOne", 10));
            }));

        var merged = Merge(conflict);
        var alias = merged.VirtualMachineAdapter!.Aliases!.Single();
        AssertTrue(alias.Property is not null, "Expected the VMAD alias property payload to survive mixed ITM and unique branches.");
        AssertEqual((short)10, alias.Property!.Alias, "Expected the VMAD alias binding to remain intact.");
        AssertEqual(1, alias.Scripts!.Count, "Expected the VMAD alias script list to stay present.");
        AssertEqual(1, alias.Scripts![0].Properties!.Count, "Expected nested VMAD alias script properties to be forwarded instead of skipped.");
        AssertEqual("StageToSetOnDeath", alias.Scripts![0].Properties![0].Name, "Expected the nested VMAD alias property name to survive.");
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

    private static void VmadAliasNestedSubfieldsSurviveUnrelatedLaterOmissions()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest => { }),
            Spec("USSEP.esp", new[] { "Skyrim.esm" }, quest =>
            {
                quest.VirtualMachineAdapter = NewQuestAdapter();
                var alias = NewQuestFragmentAlias("TelravAlias", 4);
                alias.Scripts!.Single().Name = "defaultaliasondeathscript";
                alias.Scripts![0].Properties!.Add(new ScriptIntProperty
                {
                    Name = "StageToSetOnDeath",
                    Data = 52,
                });
                quest.VirtualMachineAdapter!.Aliases!.Add(alias);
            }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 50; }),
            Spec("Winner.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 75; }));

        var merged = Merge(conflict);
        AssertEqual(1, merged.VirtualMachineAdapter!.Aliases!.Count, "Expected the VMAD alias bucket to survive unrelated later omissions.");
        var alias = merged.VirtualMachineAdapter!.Aliases!.Single();
        AssertTrue(alias.Property is not null, "Expected the VMAD alias property payload to be retained.");
        AssertEqual((short)4, alias.Property!.Alias, "Expected the VMAD alias binding to stay intact.");
        AssertEqual("defaultaliasondeathscript", alias.Scripts!.Single().Name, "Expected the VMAD alias script name to be forwarded.");
        AssertEqual(1, alias.Scripts![0].Properties!.Count, "Expected the nested VMAD alias script property to survive.");
        AssertEqual("StageToSetOnDeath", alias.Scripts![0].Properties![0].Name, "Expected the nested VMAD alias property name to be preserved.");
    }

    private static void VmadQuestFragmentAliasesSurviveBlankPropertyNames()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest =>
            {
                quest.VirtualMachineAdapter = new QuestAdapter
                {
                    Scripts = [],
                    Fragments = [],
                    Aliases =
                    [
                        new QuestFragmentAlias
                        {
                            Property = new ScriptObjectProperty
                            {
                                Object = Link<IQuestGetter>("123456:Master.esm"),
                                Alias = 10,
                            },
                            Scripts =
                            [
                                new ScriptEntry
                                {
                                    Name = "CartRiderScript",
                                    Properties = [],
                                },
                            ],
                        },
                    ],
                };
            }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 50; }));

        var merged = Merge(conflict);
        AssertEqual(1, merged.VirtualMachineAdapter!.Aliases!.Count, "Expected a real-shaped quest fragment alias with a blank property name to survive sanitize/validate.");
        var alias = merged.VirtualMachineAdapter!.Aliases!.Single();
        AssertTrue(alias.Property is not null, "Expected the VMAD quest fragment alias property payload to survive.");
        AssertEqual((short)10, alias.Property!.Alias, "Expected the VMAD alias binding to remain intact when the property name is blank.");
        AssertEqual(1, alias.Scripts!.Count, "Expected the alias script list to remain allocated and populated.");
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

    private static void VmadPropertyTypeConflictFallsBackToWholePropertyHpmo()
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
        AssertTrue(property is ScriptIntProperty, "Expected a whole-property HPMO fallback to keep the higher-priority type variant.");
        AssertEqual(42, ((ScriptIntProperty)property).Data, "Expected the higher-priority property value to win.");
    }

    private static void StageIndexMetadataSurvivesUnrelatedLaterOmissions()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest => { }),
            Spec("USSEP.esp", new[] { "Skyrim.esm" }, quest =>
            {
                quest.Stages!.Add(new QuestStage
                {
                    Index = 51,
                    Flags = QuestStage.Flag.StartUpStage,
                    Unknown = 95,
                    LogEntries = [],
                });
            }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 50; }),
            Spec("Winner.esp", new[] { "Skyrim.esm" }, quest => { quest.Priority = 75; }));

        var merged = Merge(conflict);
        var stage = merged.Stages!.Single();
        AssertEqual((ushort)51, stage.Index, "Expected the stage index to survive unrelated later omissions.");
        AssertEqual(QuestStage.Flag.StartUpStage, stage.Flags, "Expected the stage index flags to be forwarded.");
        AssertEqual((byte)95, stage.Unknown, "Expected the stage index unknown value to be forwarded.");
    }

    private static void StageBucketCanBeRemovedByPresentLaterStagesContainer()
    {
        var conflict = BuildConflict(
            Spec("Skyrim.esm", Array.Empty<string>(), quest =>
            {
                quest.Stages!.Add(new QuestStage
                {
                    Index = 51,
                    Flags = QuestStage.Flag.StartUpStage,
                    Unknown = 95,
                    LogEntries = [],
                });
                quest.Stages!.Add(new QuestStage
                {
                    Index = 52,
                    Flags = QuestStage.Flag.StartUpStage,
                    Unknown = 96,
                    LogEntries = [],
                });
            }),
            Spec("PatchA.esp", new[] { "Skyrim.esm" }, quest =>
            {
                quest.Stages!.Add(new QuestStage
                {
                    Index = 52,
                    Flags = QuestStage.Flag.StartUpStage,
                    Unknown = 96,
                    LogEntries = [],
                });
            }));

        var merged = Merge(conflict);
        AssertEqual(1, merged.Stages!.Count, "Expected a missing stage bucket to be removable when a later stages container is still present.");
        AssertEqual((ushort)52, merged.Stages![0].Index, "Expected the remaining stage bucket to be the one still present in the later stages container.");
    }

    private static void MutagenContractExposesQuestVmadAliasAndStageMembers()
    {
        AssertTrue(typeof(QuestAdapter).GetProperty(nameof(QuestAdapter.Aliases)) is not null, "Expected Mutagen QuestAdapter to expose the Aliases property.");
        AssertTrue(typeof(QuestFragmentAlias).GetProperty(nameof(QuestFragmentAlias.Property)) is not null, "Expected Mutagen QuestFragmentAlias to expose the Property payload.");
        AssertTrue(typeof(QuestFragmentAlias).GetProperty(nameof(QuestFragmentAlias.Scripts)) is not null, "Expected Mutagen QuestFragmentAlias to expose the Scripts list.");
        AssertTrue(typeof(QuestStage).GetProperty(nameof(QuestStage.Index)) is not null, "Expected Mutagen QuestStage to expose the Index field.");
        AssertTrue(typeof(QuestStage).GetProperty(nameof(QuestStage.Flags)) is not null, "Expected Mutagen QuestStage to expose the Flags field.");
        AssertTrue(typeof(QuestStage).GetProperty(nameof(QuestStage.Unknown)) is not null, "Expected Mutagen QuestStage to expose the Unknown field.");
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

    private static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (expected.Count != actual.Count || !expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{message} Expected: [{string.Join(", ", expected)}]. Actual: [{string.Join(", ", actual)}].");
        }
    }

    private sealed record ConflictSpec(string ModName, string[] Masters, Action<Quest> Configure);
}
