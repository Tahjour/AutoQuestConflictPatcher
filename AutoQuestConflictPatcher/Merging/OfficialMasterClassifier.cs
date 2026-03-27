using Mutagen.Bethesda.Plugins;

namespace AutoQuestConflictPatcher.Merging;

public sealed class OfficialMasterClassifier
{
    private static readonly string[] BaseOfficialPlugins =
    [
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm",
    ];

    private readonly HashSet<ModKey> _officialMasters;

    public OfficialMasterClassifier(string? dataFolderPath = null)
    {
        _officialMasters = BaseOfficialPlugins
            .Select(static plugin => ModKey.FromNameAndExtension(plugin))
            .ToHashSet();

        LoadCreationClubMasters(dataFolderPath);
    }

    public IReadOnlySet<ModKey> OfficialMasters => _officialMasters;

    public bool IsOfficial(ModKey modKey)
    {
        return _officialMasters.Contains(modKey);
    }

    private void LoadCreationClubMasters(string? dataFolderPath)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath))
        {
            return;
        }

        foreach (var cccPath in GetCandidateCccPaths(dataFolderPath))
        {
            if (!File.Exists(cccPath))
            {
                continue;
            }

            foreach (var rawLine in File.ReadAllLines(cccPath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                try
                {
                    _officialMasters.Add(ModKey.FromNameAndExtension(line));
                }
                catch (ArgumentException)
                {
                }
            }

            break;
        }
    }

    private static IEnumerable<string> GetCandidateCccPaths(string dataFolderPath)
    {
        yield return Path.Combine(dataFolderPath, "Skyrim.ccc");

        var gameFolder = Directory.Exists(dataFolderPath)
            ? Directory.GetParent(dataFolderPath)?.FullName
            : Path.GetDirectoryName(dataFolderPath);

        if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            yield return Path.Combine(gameFolder, "Skyrim.ccc");
        }
    }
}
