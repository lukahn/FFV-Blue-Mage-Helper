using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FFVBlueMageHelper;

internal sealed class SpellInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int[] Enemies { get; set; }
}

internal sealed class MonsterInfo
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Msg { get; set; }
}

internal sealed class BlueMagicFile
{
    public List<SpellInfo> Spells { get; set; }
    public List<MonsterInfo> Monsters { get; set; }
}

/// <summary>
/// Blue magic data distilled from the community blue magic guide, matched to the
/// Pixel Remaster's monster IDs.
/// </summary>
internal static class BlueMagicCatalog
{
    private static readonly Dictionary<int, SpellInfo> SpellsById = new();
    private static readonly Dictionary<int, List<SpellInfo>> SpellsByMonster = new();
    private static readonly Dictionary<int, MonsterInfo> MonstersById = new();

    static BlueMagicCatalog()
    {
        try
        {
            Load();
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource?.LogError($"Could not load blue magic data: {e}");
        }
    }

    public static IReadOnlyList<SpellInfo> Spells => SpellsById.Values.OrderBy(s => s.Id).ToList();

    public static IReadOnlyList<SpellInfo> GetSpellsForMonster(int monsterId)
    {
        return SpellsByMonster.TryGetValue(monsterId, out var list)
            ? list
            : Array.Empty<SpellInfo>();
    }

    public static bool IsBlueMagicId(int abilityId)
    {
        return SpellsById.ContainsKey(abilityId);
    }

    public static string GetMonsterName(int monsterId, string fallback = null)
    {
        if (MonstersById.TryGetValue(monsterId, out var monster) &&
            !string.IsNullOrEmpty(monster.Name))
        {
            return monster.Name;
        }

        return fallback ?? $"Monster {monsterId}";
    }

    private static void Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("FFVBlueMageHelper.blue_magic.json");
        if (stream == null)
            throw new InvalidOperationException("Embedded blue_magic.json was not found.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var data = JsonSerializer.Deserialize<BlueMagicFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data?.Spells == null)
            throw new InvalidOperationException("blue_magic.json did not contain any spells.");

        foreach (var spell in data.Spells)
        {
            spell.Enemies ??= Array.Empty<int>();
            SpellsById[spell.Id] = spell;

            foreach (var monsterId in spell.Enemies)
            {
                if (!SpellsByMonster.TryGetValue(monsterId, out var list))
                {
                    list = new List<SpellInfo>();
                    SpellsByMonster[monsterId] = list;
                }

                list.Add(spell);
            }
        }

        foreach (var list in SpellsByMonster.Values)
            list.Sort((a, b) => a.Id.CompareTo(b.Id));

        if (data.Monsters != null)
        {
            foreach (var monster in data.Monsters)
                MonstersById[monster.Id] = monster;
        }

        BlueMageHelperPlugin.LogSource?.LogInfo(
            $"Loaded {SpellsById.Count} blue magic spells covering " +
            $"{SpellsByMonster.Count} monsters.");
    }
}
