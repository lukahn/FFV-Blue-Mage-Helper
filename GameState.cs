using System;
using System.Collections.Generic;
using System.Linq;
using Last.Battle;
using Last.Data.Master;
using Last.Management;
using Serial;
using Serial.Template.Management;

namespace FFVBlueMageHelper;

internal static class GameState
{
    // The Crystal bosses are nameless in battle ("????"). Use their bestiary
    // names so the banner is useful.
    private static readonly Dictionary<int, string> NameOverrides = new()
    {
        [330] = "Water Crystal",
        [331] = "Wind Crystal"
    };

    public static BlueMagicProvider GetBlueMagicProvider()
    {
        try
        {
            return ProviderManager.Instance?.BlueMagicProvider;
        }
        catch (Exception e)
        {
            if (BlueMageHelperPlugin.Verbose.Value)
                BlueMageHelperPlugin.LogSource.LogWarning($"Could not read BlueMagicProvider: {e.Message}");

            return null;
        }
    }

    public static HashSet<int> GetLearnedAbilityIds()
    {
        var learned = new HashSet<int>();

        try
        {
            var userData = UserDataManager.Instance();
            if (userData == null)
                return learned;

            AddHashSet(learned, userData.LearnedAbilityList);
            AddHashSet(learned, userData.OwnedMagicList);

            var characters = userData.OwnedCharacterList;
            if (characters != null)
            {
                foreach (var character in characters)
                {
                    if (character == null)
                        continue;

                    AddList(learned, character.LearningAbilitys);

                    // Some saves also carry learned blue spells as ordinary
                    // owned abilities. Only look at blue magic ids here so other
                    // job abilities cannot hide a spell.
                    var ownedAbilities = character.OwnedAbilityList;
                    if (ownedAbilities != null)
                    {
                        foreach (var ownedAbility in ownedAbilities)
                        {
                            try
                            {
                                var ability = ownedAbility?.ability;
                                if (ability != null && BlueMagicCatalog.IsBlueMagicId(ability.Id))
                                    learned.Add(ability.Id);
                            }
                            catch
                            {
                                // Ignore partially loaded save entries.
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not read learned abilities: {e.Message}");
        }

        return learned;
    }

    /// <summary>
    /// Blue magic is learned as an ability. Depending on the game version the
    /// learned list can contain the spell id itself or the "learning" ability id,
    /// so both are checked.
    /// </summary>
    public static bool IsSpellLearned(int spellId, HashSet<int> learned, BlueMagicProvider provider)
    {
        if (learned.Contains(spellId))
            return true;

        if (provider != null)
        {
            try
            {
                int learningId = provider.GetLearningAbilityId(spellId);
                if (learningId > 0 && learned.Contains(learningId))
                    return true;
            }
            catch
            {
                // The mapping is only a fallback; the spell id check above is enough.
            }

            try
            {
                var data = provider.serialData?.TryCast<Serial.FF5.Management.BlueMagicData>();
                var map = data?.BlueMagicLearningAbilityIds;
                if (map != null && map.ContainsKey(spellId))
                {
                    var candidates = map[spellId];
                    if (candidates != null)
                    {
                        foreach (int candidate in candidates)
                        {
                            if (learned.Contains(candidate))
                                return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore mapping failures.
            }
        }

        return false;
    }

    public static List<int> GetBattleMonsterIds(BattleController controller)
    {
        var ids = new List<int>();
        if (controller == null)
            return ids;

        try
        {
            int partyId = controller.startMonsterPartyId;
            if (partyId == 0)
                partyId = controller.monstarPartyId;

            if (partyId != 0)
            {
                var master = MasterManager.Instance;
                var party = master?.GetData<MonsterParty>(partyId);
                if (party != null)
                {
                    AddMonster(ids, party.Monster1);
                    AddMonster(ids, party.Monster2);
                    AddMonster(ids, party.Monster3);
                    AddMonster(ids, party.Monster4);
                    AddMonster(ids, party.Monster5);
                    AddMonster(ids, party.Monster6);
                    AddMonster(ids, party.Monster7);
                    AddMonster(ids, party.Monster8);
                    AddMonster(ids, party.Monster9);
                }
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not read monster party: {e.Message}");
        }

        if (ids.Count == 0)
        {
            try
            {
                var entities = UnityEngine.Object.FindObjectsOfType<BattleEnemyEntity>();
                if (entities.Length > 0)
                {
                    var assetMap = BuildMonsterAssetMap();
                    foreach (var entity in entities)
                    {
                        if (entity == null)
                            continue;

                        if (assetMap.TryGetValue(entity.MonsterAssetId, out var monsterIds))
                            ids.AddRange(monsterIds);
                    }
                }
            }
            catch (Exception e)
            {
                BlueMageHelperPlugin.LogSource.LogWarning($"Could not read battle enemies: {e.Message}");
            }
        }

        return ids.Distinct().ToList();
    }

    /// <summary>
    /// Fallback for battles where the monster party id is not available: map the
    /// sprite asset ids of the spawned enemies back to monster master ids.
    /// </summary>
    private static Dictionary<int, List<int>> BuildMonsterAssetMap()
    {
        var map = new Dictionary<int, List<int>>();

        try
        {
            var monsters = MasterManager.Instance?.GetList<Monster>();
            if (monsters == null)
                return map;

            foreach (var pair in monsters)
            {
                var monster = pair.Value;
                if (monster == null)
                    continue;

                int assetId = monster.MonsterAssetId;
                if (assetId <= 0)
                    continue;

                if (!map.TryGetValue(assetId, out var list))
                {
                    list = new List<int>();
                    map[assetId] = list;
                }

                list.Add(pair.Key);
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not build monster asset map: {e.Message}");
        }

        return map;
    }

    public static string GetMonsterName(int monsterId)
    {
        if (NameOverrides.TryGetValue(monsterId, out var overrideName))
            return overrideName;

        try
        {
            var master = MasterManager.Instance;
            var monster = master?.GetData<Monster>(monsterId);
            if (monster != null && !string.IsNullOrEmpty(monster.MesIdName))
            {
                string localized = GetMessage(monster.MesIdName);
                if (!string.IsNullOrEmpty(localized) && localized != monster.MesIdName)
                    return localized;
            }
        }
        catch
        {
            // Fall back to the embedded English name.
        }

        return BlueMagicCatalog.GetMonsterName(monsterId);
    }

    public static string GetSpellName(SpellInfo spell)
    {
        string key = $"MSG_MAGIC_NAME_{spell.Id}";
        string localized = GetMessage(key);
        return string.IsNullOrEmpty(localized) || localized == key ? spell.Name : localized;
    }

    public static string GetMessage(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            var messages = MessageManager.Instance;
            if (messages == null)
                return null;

            return messages.GetMessage(key);
        }
        catch
        {
            return null;
        }
    }

    public static void LogSelfTest()
    {
        try
        {
            var log = BlueMageHelperPlugin.LogSource;
            var master = MasterManager.Instance;
            log.LogInfo($"[SelfTest] MasterManager.Instance = {(master != null)}");

            var monster = master?.GetData<Monster>(1);
            log.LogInfo($"[SelfTest] Monster 1 = {monster?.MesIdName} / {GetMonsterName(1)}");
            log.LogInfo($"[SelfTest] Monster 1 spells = {string.Join(", ", BlueMagicCatalog.GetSpellsForMonster(1).Select(s => s.Name))}");
            foreach (int testId in new[] { 306, 330, 331 })
            {
                log.LogInfo($"[SelfTest] Monster {testId} = {GetMonsterName(testId)}; spells = " +
                            $"{string.Join(", ", BlueMagicCatalog.GetSpellsForMonster(testId).Select(s => s.Name))}");
            }

            var party = master?.GetData<MonsterParty>(3);
            log.LogInfo($"[SelfTest] MonsterParty 3 monsters = {party?.Monster1}, {party?.Monster2}, {party?.Monster3}");

            var provider = ProviderManager.Instance?.BlueMagicProvider;
            log.LogInfo($"[SelfTest] BlueMagicProvider = {(provider != null)}");
            if (provider != null)
            {
                var data = provider.serialData?.TryCast<Serial.FF5.Management.BlueMagicData>();
                var map = data?.BlueMagicLearningAbilityIds;
                log.LogInfo($"[SelfTest] BlueMagic map entries = {map?.Count}");

                foreach (int spellId in new[] { 205, 222, 227, 233 })
                {
                    try
                    {
                        log.LogInfo($"[SelfTest] GetLearningAbilityId({spellId}) = {provider.GetLearningAbilityId(spellId)}");
                    }
                    catch (Exception e)
                    {
                        log.LogWarning($"[SelfTest] GetLearningAbilityId({spellId}) failed: {e.Message}");
                    }
                }
            }

            var messages = MessageManager.Instance;
            log.LogInfo($"[SelfTest] MessageManager = {(messages != null)}");
            log.LogInfo($"[SelfTest] MSG_MON_NAME_94 = {messages?.GetMessage("MSG_MON_NAME_94")}");

            var userData = UserDataManager.Instance();
            log.LogInfo($"[SelfTest] UserDataManager = {(userData != null)}");
            if (userData != null)
            {
                log.LogInfo($"[SelfTest] OwnedCharacterList = {userData.OwnedCharacterList?.Count}");
                log.LogInfo($"[SelfTest] LearnedAbilityList = {userData.LearnedAbilityList?.Count}");
                log.LogInfo($"[SelfTest] OwnedMagicList = {userData.OwnedMagicList?.Count}");
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"[SelfTest] Failed: {e}");
        }
    }

    public static void LogDiagnostics(BattleController controller, List<int> monsterIds,
        HashSet<int> learned, BlueMagicProvider provider)
    {
        if (!BlueMageHelperPlugin.Verbose.Value)
            return;

        try
        {
            var log = BlueMageHelperPlugin.LogSource;
            log.LogInfo($"[BlueMagic] startMonsterPartyId={controller.startMonsterPartyId}, " +
                        $"monstarPartyId={controller.monstarPartyId}, monsters=[{string.Join(", ", monsterIds)}]");

            foreach (int monsterId in monsterIds)
                log.LogInfo($"[BlueMagic] monster {monsterId} = \"{GetMonsterName(monsterId)}\"");

            log.LogInfo($"[BlueMagic] learned ids=[{string.Join(", ", learned.OrderBy(x => x))}]");

            var data = provider?.serialData?.TryCast<Serial.FF5.Management.BlueMagicData>();
            var map = data?.BlueMagicLearningAbilityIds;
            if (map != null)
            {
                foreach (var entry in map)
                {
                    var values = new List<int>();
                    foreach (int value in entry.Value)
                        values.Add(value);

                    log.LogInfo($"[BlueMagic] mapping {entry.Key} -> [{string.Join(", ", values)}]");
                }
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Diagnostics failed: {e.Message}");
        }
    }

    private static void AddMonster(List<int> ids, int monsterId)
    {
        if (monsterId > 0)
            ids.Add(monsterId);
    }

    private static void AddList(HashSet<int> target, Il2CppSystem.Collections.Generic.List<int> source)
    {
        if (source == null)
            return;

        foreach (int value in source)
            target.Add(value);
    }

    private static void AddHashSet(HashSet<int> target, Il2CppSystem.Collections.Generic.HashSet<int> source)
    {
        if (source == null)
            return;

        foreach (int value in source)
            target.Add(value);
    }
}
