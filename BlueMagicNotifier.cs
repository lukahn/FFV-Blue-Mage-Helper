using System;
using System.Collections.Generic;
using Last.Battle;

namespace FFVBlueMageHelper;

internal static class BlueMagicNotifier
{
    public static void OnBattleStart(BattleController controller)
    {
        try
        {
            Banner.EnsureCreated();
            Banner.Instance?.Schedule(controller);
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogError($"Could not start blue magic scan: {e}");
        }
    }

    /// <summary>
    /// Returns the unlearned blue magic lines for the current battle, or null when
    /// the enemies are not available yet.
    /// </summary>
    public static List<string> BuildLines(BattleController controller)
    {
        var monsterIds = GameState.GetBattleMonsterIds(controller);
        if (monsterIds.Count == 0)
            return null;

        var learned = GameState.GetLearnedAbilityIds();
        var provider = GameState.GetBlueMagicProvider();
        GameState.LogDiagnostics(controller, monsterIds, learned, provider);

        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (int monsterId in monsterIds)
        {
            var spells = BlueMagicCatalog.GetSpellsForMonster(monsterId);
            if (spells.Count == 0)
                continue;

            string enemyName = GameState.GetMonsterName(monsterId);

            foreach (var spell in spells)
            {
                if (GameState.IsSpellLearned(spell.Id, learned, provider))
                    continue;

                string line = $"{enemyName} \u2014 {GameState.GetSpellName(spell)}";
                if (seen.Add(line))
                    lines.Add(line);
            }
        }

        return lines;
    }
}
