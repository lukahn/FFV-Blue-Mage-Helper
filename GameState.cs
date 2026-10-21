using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    private static readonly HashSet<int> SessionLearnedBlue = new();
    private static readonly object SessionLearnedLock = new();

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

            // The live sets are normally populated, but read the serialized
            // lists as well so a freshly loaded save is covered immediately.
            try
            {
                var save = userData.saveData;
                if (save != null)
                {
                    AddBlueIdsFromSaveJson(learned, save.LearnedAbilityList);
                    AddBlueIdsFromSaveJson(learned, save.OwnedMagicList);
                }
            }
            catch
            {
                // Fallback only.
            }

            var characters = userData.OwnedCharacterList;
            if (characters != null)
            {
                foreach (var character in characters)
                {
                    if (character == null)
                        continue;

                    try
                    {
                        AddList(learned, character.LearningAbilitys);

                        // Blue spells can also live in the character's ability list.
                        // Read the serialized ability id directly: the resolved
                        // OwnedAbility.ability master object can be null for entries
                        // that have not been opened in the ability menu yet.
                        var ownedAbilities = character.OwnedAbilityList;
                        if (ownedAbilities != null)
                        {
                            foreach (var ownedAbility in ownedAbilities)
                            {
                                try
                                {
                                    int abilityId = ownedAbility?.saveData?.abilityId ?? 0;
                                    if (abilityId == 0)
                                        abilityId = ownedAbility?.ability?.Id ?? 0;

                                    if (abilityId > 0)
                                        learned.Add(abilityId);

                                    int contentId = ownedAbility?.saveData?.contentId ?? 0;
                                    if (contentId > 0)
                                        learned.Add(contentId);
                                }
                                catch
                                {
                                    // Ignore partially loaded save entries.
                                }
                            }
                        }

                        // Fallback for a freshly loaded save: the live lists can be
                        // empty until the ability menu has been opened, but the raw
                        // serialized save strings already contain the learned ids.
                        try
                        {
                            var save = character.saveData;
                            if (save != null)
                            {
                                AddBlueIdsFromSaveJson(learned, save.LearningAbilitys);
                                AddBlueIdsFromSaveJson(learned, save.AbilityList);
                            }
                        }
                        catch
                        {
                            // Fallback only.
                        }
                    }
                    catch (Exception e)
                    {
                        if (BlueMageHelperPlugin.Verbose.Value)
                            BlueMageHelperPlugin.LogSource.LogWarning(
                                $"Could not read abilities for character {character.Id}: {e.Message}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not read learned abilities: {e.Message}");
        }

        ApplyBlueAliasMap(learned);

        lock (SessionLearnedLock)
        {
            foreach (int id in learned)
            {
                if (BlueMagicCatalog.IsBlueMagicId(id))
                    SessionLearnedBlue.Add(id);
            }

            learned.UnionWith(SessionLearnedBlue);
        }

        return learned;
    }

    /// <summary>
    /// Forgets what was learned this session. Called when a save is loaded or a
    /// new game starts, so an older save cannot stay suppressed by a spell that
    /// was learned in a save the player left behind.
    /// </summary>
    public static void ResetSessionLearned()
    {
        lock (SessionLearnedLock)
        {
            SessionLearnedBlue.Clear();
        }
    }

    /// <summary>
    /// Re-reads the live save state and remembers any blue spell learned during
    /// the fight. Called at battle end; the next battle start also re-reads the
    /// live lists, so learned spells do not come back.
    /// </summary>
    public static void RefreshSessionLearned()
    {
        try
        {
            var learned = GetLearnedAbilityIds();
            if (BlueMageHelperPlugin.Verbose.Value)
            {
                var blue = learned.Where(BlueMagicCatalog.IsBlueMagicId).OrderBy(x => x);
                BlueMageHelperPlugin.LogSource.LogInfo(
                    $"[BlueMagic] session learned blue ids=[{string.Join(", ", blue)}]");
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not refresh learned blue magic: {e.Message}");
        }
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
            log.LogInfo($"[SelfTest] Blue alias map entries = {BlueMagicCatalog.AbilityAliasCount}");
            if (provider != null)
            {
                foreach (int spellId in new[] { 205, 222, 227, 233, 400, 396, 405 })
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

            log.LogInfo($"[BlueMagic] learned blue ids=[{string.Join(", ", learned.Where(BlueMagicCatalog.IsBlueMagicId).OrderBy(x => x))}]");
            LogLearnedSources();

            if (provider != null)
            {
                for (int spellId = 205; spellId <= 234; spellId++)
                {
                    try
                    {
                        log.LogInfo($"[BlueMagic] learning id {spellId} -> {provider.GetLearningAbilityId(spellId)}");
                    }
                    catch
                    {
                        // Ignore individual mapping failures.
                    }
                }
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Diagnostics failed: {e.Message}");
        }
    }

    private static void LogLearnedSources()
    {
        try
        {
            var userData = UserDataManager.Instance();
            var characters = userData?.OwnedCharacterList;
            if (characters == null)
                return;

            foreach (var character in characters)
            {
                if (character == null)
                    continue;

                var learning = new List<int>();
                var abilityList = character.LearningAbilitys;
                if (abilityList != null)
                {
                    foreach (int id in abilityList)
                    {
                        if (BlueMagicCatalog.IsBlueMagicId(id))
                            learning.Add(id);
                    }
                }

                var owned = new List<int>();
                var ownedAbilities = character.OwnedAbilityList;
                if (ownedAbilities != null)
                {
                    foreach (var ownedAbility in ownedAbilities)
                    {
                        int id = 0;
                        try { id = ownedAbility?.saveData?.abilityId ?? 0; } catch { }
                        if (id == 0)
                        {
                            try { id = ownedAbility?.ability?.Id ?? 0; } catch { }
                        }

                        if (BlueMagicCatalog.IsBlueMagicId(id))
                            owned.Add(id);
                    }
                }

                BlueMageHelperPlugin.LogSource.LogInfo(
                    $"[BlueMagic] character {character.Name} (id {character.Id}): " +
                    $"learning=[{string.Join(", ", learning)}], owned blue=[{string.Join(", ", owned)}]");
            }
        }
        catch (Exception e)
        {
            BlueMageHelperPlugin.LogSource.LogWarning($"Could not log learned sources: {e.Message}");
        }
    }

    private static void AddMonster(List<int> ids, int monsterId)
    {
        if (monsterId > 0)
            ids.Add(monsterId);
    }

    private static void AddBlueIdsFromSaveJson(HashSet<int> target, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement items;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (!root.TryGetProperty("target", out items) ||
                 items.ValueKind != JsonValueKind.Array)
        {
            AddBlueIdsFromRawJson(target, json);
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number)
            {
                target.Add(item.GetInt32());
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                string value = item.GetString();
                if (int.TryParse(value, out int id))
                {
                    target.Add(id);
                    continue;
                }

                try
                {
                    using var inner = JsonDocument.Parse(value);
                    AddAbilityIds(target, inner.RootElement);
                }
                catch
                {
                    // Not an ability object.
                }
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                AddAbilityIds(target, item);
            }
        }
    }

    private static void AddAbilityIds(HashSet<int> target, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("abilityId", out var abilityId) &&
            abilityId.ValueKind == JsonValueKind.Number)
        {
            target.Add(abilityId.GetInt32());
        }

        if (element.TryGetProperty("contentId", out var contentId) &&
            contentId.ValueKind == JsonValueKind.Number)
        {
            target.Add(contentId.GetInt32());
        }
    }

    private static void AddBlueIdsFromRawJson(HashSet<int> target, string json)
    {
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(
                     json, "\"(?:abilityId|contentId)\"\\s*:\\s*(\\d+)"))
        {
            if (int.TryParse(match.Groups[1].Value, out int id))
                target.Add(id);
        }
    }

    /// <summary>
    /// Learned blue spells are stored under the enemy ability alias (for example
    /// 400 for Goblin Punch), not the spell id (222). The embedded alias table
    /// links the two through the game's MSG_MAGIC_NAME_* content rows.
    /// </summary>
    private static void ApplyBlueAliasMap(HashSet<int> learned)
    {
        var candidates = new List<int>(learned);
        foreach (int id in candidates)
        {
            if (BlueMagicCatalog.TryGetSpellByAbilityAlias(id, out int spellId))
                learned.Add(spellId);
            if (BlueMagicCatalog.TryGetSpellByContentId(id, out spellId))
                learned.Add(spellId);
        }
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
