using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LOR_DiceSystem;
using UI;
using UI.Title;
using UnityEngine;

namespace LorAIHost
{
    /// <summary>
    /// Advanced battle actions ported from BridgePatchHost.
    /// These were previously in a separate LorBridgePatch mod.
    /// </summary>
    public static class AdvancedActions
    {
        // ─────────────────────────────────────────────────────────────
        // startGame: click Continue (or New_Game) on title screen
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoStartGame(Dictionary<string, object> args)
        {
            try
            {
                UITitleController controller = UITitleController.Controller;
                if (controller == null)
                    return Error("UITitleController.Controller is null");

                TitleActionType action = GlobalGameManager.Instance.CheckSaveData(1)
                    ? TitleActionType.Continue
                    : TitleActionType.New_Game;

                controller.OnSelectButton(action);
                controller.OnClickActionButton(action);
                return Success("Title action invoked: " + action);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // prepareBattle: auto-select best books by HP, call PrepareBattle
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoPrepareBattle(Dictionary<string, object> args)
        {
            try
            {
                if (!args.TryGetValue("stageId", out var stageIdObj))
                    return Error("stageId parameter required");

                int stageId = Convert.ToInt32(stageIdObj);

                StageClassInfo stageInfo = Singleton<StageClassInfoList>.Instance.GetData(stageId);
                if (stageInfo == null)
                    return Error("Stage " + stageId + " not found");

                UI.UIController ui = UI.UIController.Instance;
                if (ui == null)
                    return Error("UIController instance is null");

                // ── Determine max books from stage info ──
                int maxBooks = 5;
                if (stageInfo.stageType == StageType.Invitation && stageInfo.invitationInfo != null)
                    maxBooks = stageInfo.invitationInfo.bookNum;
                if (maxBooks <= 0) maxBooks = 5;

                // ── Mode 1: Explicit book IDs from the LLM ──
                if (args.TryGetValue("bookIds", out var bookIdsObj) && bookIdsObj is List<object> explicitIds)
                {
                    var selectedBooks = new List<DropBookXmlInfo>();
                    foreach (var idObj in explicitIds)
                    {
                        try
                        {
                            LorId lorId = ParseLorId(idObj.ToString());
                            DropBookXmlInfo book = DropBookXmlList.Instance.GetData(lorId);
                            if (book != null)
                                selectedBooks.Add(book);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[LorAI] prepareBattle: failed to parse bookId " + idObj + ": " + ex.Message);
                        }
                    }
                    if (selectedBooks.Count > 0)
                    {
                        ui.PrepareBattle(stageInfo, selectedBooks);
                        return BuildPrepareResult(stageId, selectedBooks, "explicit");
                    }
                    // Fall through to auto-select if explicit IDs failed
                }

                // ── Mode 2: Auto-select best books ──
                // Get available books from invitation list first
                List<LorId> availableBookIds = Singleton<DropBookInventoryModel>.Instance.GetBookList_invitationBookList();
                if (availableBookIds == null)
                    availableBookIds = new List<LorId>();

                List<DropBookXmlInfo> candidateBooks = new List<DropBookXmlInfo>();
                foreach (LorId bookId in availableBookIds)
                {
                    DropBookXmlInfo book = DropBookXmlList.Instance.GetData(bookId);
                    if (book != null)
                        candidateBooks.Add(book);
                }

                // Fallback: if invitation list is too small, use full inventory
                if (candidateBooks.Count < Mathf.Min(maxBooks, 3))
                {
                    Debug.Log("[LorAI] prepareBattle: invitation list only had " + candidateBooks.Count
                              + " books, falling back to full inventory");
                    var fullList = Singleton<DropBookInventoryModel>.Instance.GetBookList();
                    if (fullList != null)
                    {
                        foreach (var dropItem in fullList)
                        {
                            try
                            {
                                // OwnDropBookModel has a public XmlInfo property (DropBookXmlInfo)
                                DropBookXmlInfo book = ReflectionHelper.GetFieldValue(dropItem, "XmlInfo") as DropBookXmlInfo;
                                if (book == null) continue;
                                if (!candidateBooks.Contains(book))
                                    candidateBooks.Add(book);
                            }
                            catch { }
                        }
                    }
                }

                if (candidateBooks.Count == 0)
                    return Error("No valid book data found (invitation: " + availableBookIds.Count
                                 + " books, fallback also empty)");

                // ── Sort by composite score (HP, breakLife, resistances) ──
                candidateBooks.Sort((a, b) => GetBookScore(b).CompareTo(GetBookScore(a)));

                maxBooks = Mathf.Min(candidateBooks.Count, maxBooks);
                var pickedBooks = new List<DropBookXmlInfo>();
                for (int i = 0; i < maxBooks; i++)
                    pickedBooks.Add(candidateBooks[i]);

                // Call PrepareBattle directly
                ui.PrepareBattle(stageInfo, pickedBooks);

                return BuildPrepareResult(stageId, pickedBooks, "auto");
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = "PrepareBattle failed: " + ex.Message,
                    ["stack"] = ex.StackTrace
                };
            }
        }

        private static LorId ParseLorId(string s)
        {
            // LorId(int) constructor; handle "packageId.innerId" by taking inner ID
            int dot = s.IndexOf('.');
            if (dot > 0)
                s = s.Substring(dot + 1);
            return new LorId(int.Parse(s));
        }

        /// <summary>
        /// Composite book score for auto-selection. Considers HP, break life,
        /// and bookValue (rarity proxy). Uses reflection for stats that may
        /// have different property names across game versions.
        /// </summary>
        private static float GetBookScore(DropBookXmlInfo book)
        {
            float hp = GetBookHp(book);

            // bookValue is a known public field on DropBookXmlInfo — acts as rarity/quality proxy
            int bookValue = 0;
            try { bookValue = book.bookvalue; } catch { }

            // Try to get breakLife via reflection from BookXmlInfo
            int breakLife = 0;
            try
            {
                BookXmlInfo bookXml = BookXmlList.Instance.GetData(book.id);
                if (bookXml != null && !bookXml.isError)
                {
                    var be = bookXml.EquipEffect;
                    if (be != null)
                    {
                        // Try various possible names
                        breakLife = Convert.ToInt32(
                            ReflectionHelper.GetFieldValue(be, "BreakLife")
                            ?? ReflectionHelper.GetFieldValue(be, "breakLife")
                            ?? 0);
                    }
                }
            }
            catch { }

            float score = hp * 10f
                        + breakLife * 15f
                        + bookValue * 2f;

            // Big bonus for books with HP >= 50 (real combat books vs basic 30HP pages)
            if (hp >= 50) score += 200f;

            return score;
        }

        private static Dictionary<string, object> BuildPrepareResult(
            int stageId, List<DropBookXmlInfo> selected, string mode)
        {
            var booksList = new List<object>();
            foreach (var b in selected)
            {
                booksList.Add(new Dictionary<string, object>
                {
                    ["id"] = b.id.ToString(),
                    ["name"] = GetBookName(b),
                    ["hp"] = GetBookHp(b),
                    ["score"] = Math.Round(GetBookScore(b), 1)
                });
            }

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["stageId"] = stageId,
                ["mode"] = mode,
                ["booksSelected"] = selected.Count,
                ["books"] = booksList
            };
        }

        // ─────────────────────────────────────────────────────────────
        // getBattleUnits: export all battle units (players + enemies)
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoGetBattleUnits(Dictionary<string, object> args)
        {
            try
            {
                BattleObjectManager bom = BattleObjectManager.instance;
                if (bom == null)
                    return Error("BattleObjectManager is null");

                IList<BattleUnitModel> allUnits = bom.GetList();
                if (allUnits == null)
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["players"] = new List<object>(),
                        ["enemies"] = new List<object>()
                    };

                var players = new List<object>();
                var enemies = new List<object>();

                foreach (BattleUnitModel unit in allUnits)
                {
                    if (unit == null) continue;
                    try
                    {
                        var unitData = ExtractUnitData(unit);
                        string faction = unit.faction.ToString();
                        if (faction == "Player")
                            players.Add(unitData);
                        else
                            enemies.Add(unitData);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LorAI] GetBattleUnits: error processing unit: " + ex.Message);
                    }
                }

                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["players"] = players,
                    ["enemies"] = enemies
                };
            }
            catch (Exception ex)
            {
                return Error("GetBattleUnits fatal: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // getEmotionCandidates: get emotion card candidates
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoGetEmotionCandidates(Dictionary<string, object> args)
        {
            try
            {
                object levelupUi = GetLevelUpUI();
                if (levelupUi == null)
                    return Error("ui_levelup is null");

                if (!IsLevelUpUIEnabled(levelupUi))
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["active"] = false,
                        ["candidates"] = new List<object>()
                    };

                object[] candidates = GetCandidatesArray(levelupUi);
                if (candidates == null)
                    return new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["active"] = true,
                        ["candidates"] = new List<object>()
                    };

                var candidateList = new List<object>();
                int realIdx = 0;
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (candidates[i] == null) continue;
                    Component comp = candidates[i] as Component;
                    if (comp == null || !comp.gameObject.activeSelf) continue;

                    candidateList.Add(BuildCandidateDict(candidates[i], realIdx));
                    realIdx++;
                }

                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["active"] = true,
                    ["candidates"] = candidateList
                };
            }
            catch (Exception ex)
            {
                return Error("GetEmotionCandidates failed: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // selectEmotionCard: select an emotion card by index
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoSelectEmotionCard(Dictionary<string, object> args)
        {
            try
            {
                if (!args.TryGetValue("index", out var indexObj))
                    return Error("index parameter required");

                int index = Convert.ToInt32(indexObj);
                if (index < 0)
                    return Error("invalid index: " + index);

                object levelupUi = GetLevelUpUI();
                if (levelupUi == null)
                    return Error("ui_levelup is null");

                Type lut = levelupUi.GetType();

                if (!IsLevelUpUIEnabled(levelupUi))
                    return Error("Emotion card UI not active");

                object[] rawCandidates = GetCandidatesArray(levelupUi);
                if (rawCandidates == null)
                    return Error("candidates array is null");

                // Build active candidates list
                var activeCandidates = new List<object>();
                foreach (var c in rawCandidates)
                {
                    if (c == null) continue;
                    Component comp = c as Component;
                    if (comp != null && comp.gameObject.activeSelf)
                        activeCandidates.Add(c);
                }

                if (index >= activeCandidates.Count)
                    return Error("index " + index + " out of range (active count: " + activeCandidates.Count + ")");

                object selected = activeCandidates[index];
                string cardName = GetCardName(selected);

                // Call OnSelectPassive(selected)
                MethodInfo onSelectPassive = lut.GetMethod("OnSelectPassive",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (onSelectPassive == null)
                    return Error("OnSelectPassive method not found");

                onSelectPassive.Invoke(levelupUi, new object[] { selected });
                Debug.Log("[LorAI] SelectEmotionCard(" + index + "): " + cardName);

                // Handle SelectOne target selection
                FieldInfo needUnitField = lut.GetField("_needUnitSelection",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (needUnitField != null && Convert.ToBoolean(needUnitField.GetValue(levelupUi)))
                {
                    BattleObjectManager bom = BattleObjectManager.instance;
                    if (bom != null)
                    {
                        IList<BattleUnitModel> aliveList = bom.GetAliveList((Faction)0);
                        if (aliveList != null && aliveList.Count > 0)
                        {
                            BattleUnitModel target = aliveList[0];
                            MethodInfo onClickTarget = lut.GetMethod("OnClickTargetUnit",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (onClickTarget != null)
                                onClickTarget.Invoke(levelupUi, new object[] { target });
                        }
                    }
                }

                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["index"] = index,
                    ["card"] = cardName
                };
            }
            catch (Exception ex)
            {
                return Error("SelectEmotionCard failed: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // forceAdvancePhase: force-advance a stuck battle phase
        // ─────────────────────────────────────────────────────────────

        public static Dictionary<string, object> DoForceAdvancePhase(Dictionary<string, object> args)
        {
            try
            {
                string currentPhase = "";
                if (args.TryGetValue("phase", out var phaseObj))
                    currentPhase = phaseObj.ToString();

                StageController sc = Singleton<StageController>.Instance;
                if (sc == null)
                    return Error("StageController.Instance is null");

                FieldInfo phaseField = typeof(StageController).GetField("_phase",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (phaseField == null)
                    return Error("_phase field not found");

                object currentVal = phaseField.GetValue(sc);
                string actualPhase = currentVal != null ? currentVal.ToString() : "null";

                string phaseToMatch = string.IsNullOrEmpty(currentPhase) ? actualPhase : currentPhase;
                string targetPhase = null;

                switch (phaseToMatch)
                {
                    // Battle story (intro/mid/outro cutscenes in suppression battles)
                    case "BattleStoryPhase":
                        targetPhase = "RoundStartPhase_UI"; break;
                    // Round start phases
                    case "RoundStartPhase_UI":
                        targetPhase = "RoundStartPhase_System"; break;
                    case "RoundStartPhase_System":
                        targetPhase = "SortUnitPhase"; break;
                    // Sort/draw phases
                    case "SortUnitPhase":
                        targetPhase = "DrawCardPhase"; break;
                    case "DrawCardPhase":
                        targetPhase = "ApplyEnemyCardPhase"; break;
                    case "ApplyEnemyCardPhase":
                        targetPhase = "ApplyLibrarianCardPhase"; break;
                    // Existing mappings
                    case "ArrangeEquippedCards":
                        targetPhase = "ActivateStartBattleEffect"; break;
                    case "ActivateStartBattleEffect":
                        targetPhase = "WaitStartBattleEffect"; break;
                    case "WaitStartBattleEffect":
                        targetPhase = "SetCurrentDiceAction"; break;
                    case "SetCurrentDiceAction":
                        targetPhase = "CheckFarAreaPlay"; break;
                    case "CheckFarAreaPlay":
                    case "ExecuteFarAreaPlay":
                    case "EndFarAreaPlay":
                        targetPhase = "MoveUnits"; break;
                    case "MoveUnits":
                        targetPhase = "WaitUnitsArrive"; break;
                    case "WaitUnitsArrive":
                        targetPhase = "CheckParrying"; break;
                    // Combat resolution phases (missing before)
                    case "CheckParrying":
                        targetPhase = "CheckOneSideAction"; break;
                    case "CheckOneSideAction":
                        targetPhase = "ProcessViewAction"; break;
                    case "ProcessViewAction":
                        targetPhase = "RoundEndPhase"; break;
                    case "RoundEndPhase":
                        targetPhase = "EndBattle"; break;
                    default:
                        return new Dictionary<string, object>
                        {
                            ["success"] = false,
                            ["error"] = "no force-advance mapping for phase: " + phaseToMatch,
                            ["currentPhase"] = actualPhase
                        };
                }

                Type phaseEnum = typeof(StageController).GetNestedType("StagePhase",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (phaseEnum == null)
                    return Error("StagePhase enum not found");

                object targetEnumVal = Enum.Parse(phaseEnum, targetPhase, true);
                phaseField.SetValue(sc, targetEnumVal);

                Debug.Log("[LorAI] ForceAdvancePhase: " + phaseToMatch + " -> " + targetPhase);
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["oldPhase"] = phaseToMatch,
                    ["newPhase"] = targetPhase
                };
            }
            catch (Exception ex)
            {
                return Error("ForceAdvancePhase failed: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Private helpers
        // ═══════════════════════════════════════════════════════════════

        private static Dictionary<string, object> ExtractUnitData(BattleUnitModel unit)
        {
            var d = new Dictionary<string, object>();

            d["id"] = unit.id;

            string unitName = "";
            try { unitName = unit.UnitData?.unitData?.name ?? ""; } catch { }
            d["name"] = unitName;

            string faction = "Unknown";
            try { faction = unit.faction.ToString(); } catch { }
            d["faction"] = faction;

            float hpVal = 0f;
            try { hpVal = unit.hp; } catch { }
            d["hp"] = Math.Round(hpVal, 1);

            int maxHpVal = 0;
            try { maxHpVal = unit.UnitData?.unitData?.MaxHp ?? 0; } catch { }
            d["maxHp"] = maxHpVal;

            int breakVal = 0;
            try { breakVal = unit.breakDetail?.breakLife ?? 0; } catch { }
            d["breakLife"] = breakVal;

            bool dead = false;
            try { dead = unit.IsDead(); } catch { }
            d["isDead"] = dead;

            int pp = 0;
            try { pp = unit.cardSlotDetail?.PlayPoint ?? 0; } catch { }
            d["playPoint"] = pp;

            int emoLvl = 0;
            try { emoLvl = unit.emotionDetail?.EmotionLevel ?? 0; } catch { }
            d["emotionLevel"] = emoLvl;

            int diceCount = 0;
            try { diceCount = unit.speedDiceResult?.Count ?? 0; } catch { }
            d["speedDiceCount"] = diceCount;

            // Speed dice
            var diceList = new List<object>();
            try
            {
                if (unit.speedDiceResult != null)
                {
                    foreach (SpeedDice sd in unit.speedDiceResult)
                    {
                        if (sd == null) continue;
                        diceList.Add(new Dictionary<string, object>
                        {
                            ["value"] = sd.value,
                            ["breaked"] = sd.breaked
                        });
                    }
                }
            }
            catch { }
            d["speedDice"] = diceList;

            // Hand cards
            var handList = new List<object>();
            try
            {
                if (unit.allyCardDetail != null)
                {
                    foreach (BattleDiceCardModel card in unit.allyCardDetail.GetHand())
                    {
                        if (card == null) continue;
                        handList.Add(new Dictionary<string, object>
                        {
                            ["id"] = card.GetID().id,
                            ["name"] = card.GetName(),
                            ["cost"] = card.GetCost()
                        });
                    }
                }
            }
            catch { }
            d["handCards"] = handList;

            return d;
        }

        // ── Emotion card helpers ──

        private static object GetLevelUpUI()
        {
            Type bmiType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("BattleManagerUI");
                if (t != null) { bmiType = t; break; }
            }
            if (bmiType == null) return null;

            PropertyInfo instProp = null;
            Type sbGeneric = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType("SingletonBehavior`1");
                if (t != null) { sbGeneric = t; break; }
            }
            if (sbGeneric != null)
            {
                Type sbConcrete = sbGeneric.MakeGenericType(bmiType);
                instProp = sbConcrete.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (instProp == null)
                instProp = bmiType.BaseType?.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instProp == null)
                instProp = bmiType.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (instProp == null) return null;

            object bmi = instProp.GetValue(null, null);
            if (bmi == null) return null;

            FieldInfo levelupField = bmiType.GetField("ui_levelup",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (levelupField == null) return null;

            return levelupField.GetValue(bmi);
        }

        private static bool IsLevelUpUIEnabled(object levelupUi)
        {
            PropertyInfo prop = levelupUi.GetType().GetProperty("IsEnabled");
            return prop != null && (bool)prop.GetValue(levelupUi, null);
        }

        private static object[] GetCandidatesArray(object levelupUi)
        {
            FieldInfo field = levelupUi.GetType().GetField("candidates",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return null;
            return field.GetValue(levelupUi) as object[];
        }

        // ── Card data helpers ──
        // EmotionCardXmlInfo uses public FIELDS (not properties) for its data members.
        // Use GetMember to handle both fields and properties transparently.

        private static object GetMemberValue(Type t, object obj, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (f != null) return f.GetValue(obj);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (p != null) return p.GetValue(obj, null);
            return null;
        }

        private static string GetCardName(object candidate)
        {
            try
            {
                PropertyInfo cardProp = candidate.GetType().GetProperty("Card");
                if (cardProp == null) return "";
                object card = cardProp.GetValue(candidate, null);
                if (card == null) return "";
                return GetMemberValue(card.GetType(), card, "Name") as string ?? "";
            }
            catch { return ""; }
        }

        private static Dictionary<string, object> BuildCandidateDict(object candidate, int index)
        {
            string name = GetCardName(candidate);
            string state = "";
            string targetType = "";
            int emotionLevel = 0;
            int id = 0;

            try
            {
                PropertyInfo cardProp = candidate.GetType().GetProperty("Card");
                if (cardProp != null)
                {
                    object card = cardProp.GetValue(candidate, null);
                    if (card != null)
                    {
                        Type ct = card.GetType();

                        object stateVal = GetMemberValue(ct, card, "State");
                        if (stateVal != null) state = stateVal.ToString();

                        object ttVal = GetMemberValue(ct, card, "TargetType");
                        if (ttVal != null) targetType = ttVal.ToString();

                        object elVal = GetMemberValue(ct, card, "EmotionLevel");
                        if (elVal != null) emotionLevel = Convert.ToInt32(elVal);

                        object idVal = GetMemberValue(ct, card, "id");
                        if (idVal != null) id = Convert.ToInt32(idVal);
                    }
                }
            }
            catch { }

            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["id"] = id,
                ["name"] = name,
                ["state"] = state,
                ["emotionLevel"] = emotionLevel,
                ["targetType"] = targetType
            };
        }

        // ── Book helpers ──

        private static int GetBookHp(DropBookXmlInfo book)
        {
            try
            {
                // DropBookXmlInfo has no HP; look up BookXmlInfo by same ID
                BookXmlInfo bookXml = BookXmlList.Instance.GetData(book.id);
                if (bookXml != null && !bookXml.isError)
                    return bookXml.EquipEffect.Hp;
            }
            catch { }
            return 0;
        }

        private static string GetBookName(DropBookXmlInfo book)
        {
            try
            {
                // DropBookXmlInfo.Name is a property (resolves localized text)
                return book.Name ?? "";
            }
            catch { }
            return "";
        }

        // ── Common ──

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
