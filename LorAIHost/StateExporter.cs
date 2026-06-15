using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UI;

namespace LorAIHost
{
    public static class StateExporter
    {
        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Export the complete game state as a nested dictionary tree.
        /// </summary>
        public static Dictionary<string, object> ExportFullState()
        {
            var state = new Dictionary<string, object>
            {
                ["meta"] = new Dictionary<string, object>
                {
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["gameVersion"] = Application.version
                }
            };

            try { state["navigation"] = GetNavigationState(); }
            catch (Exception e) { state["navigation"] = Error(e); }

            try { state["progression"] = GetProgressionState(); }
            catch (Exception e) { state["progression"] = Error(e); }

            try { state["floors"] = GetFloorsState(); }
            catch (Exception e) { state["floors"] = Error(e); }

            try { state["inventory"] = GetInventoryState(); }
            catch (Exception e) { state["inventory"] = Error(e); }

            try { state["availableStages"] = GetAvailableStagesState(); }
            catch (Exception e) { state["availableStages"] = Error(e); }

            try { state["battle"] = GetBattleState(); }
            catch (Exception e) { state["battle"] = Error(e); }

            return state;
        }

        /// <summary>
        /// Get a specific state layer by name.
        /// </summary>
        public static Dictionary<string, object> GetLayer(string layer)
        {
            switch (layer.ToLower())
            {
                case "navigation": return GetNavigationState();
                case "progression": return GetProgressionState();
                case "floors": return GetFloorsState();
                case "inventory": return GetInventoryState();
                case "availablestages": return GetAvailableStagesState();
                case "battle": return GetBattleState();
                default: return new Dictionary<string, object> { ["error"] = "Unknown layer: " + layer };
            }
        }

        /// <summary>
        /// Persist a state dictionary to a JSON file under the mod output directory.
        /// </summary>
        public static void SaveToFile(Dictionary<string, object> state, string filename)
        {
            var dir = Path.Combine(Application.dataPath, "Mods", "LorAIHost", "output");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, filename), JsonHelper.Serialize(state, true));
        }

        // ----------------------------------------------------------------
        //  Navigation State  (UI phase, scene, sephirah)
        // ----------------------------------------------------------------

        private static Dictionary<string, object> GetNavigationState()
        {
            var result = new Dictionary<string, object>();

            try
            {
                var uiCtrl = UI.UIController.Instance;
                if (uiCtrl != null)
                {
                    // Public properties
                    result["currentUIPhase"] = uiCtrl.CurrentUIPhase.ToString();
                    result["currentSephirah"] = uiCtrl.CurrentSephirah.ToString();

                    // Current stage info
                    var stageInfo = uiCtrl.CurrentStageInfo;
                    if (stageInfo != null)
                    {
                        result["currentStageId"] = stageInfo.id;
                        result["currentStageName"] = stageInfo.stageName;
                    }

                    // UI phase stack (private Stack<UIPhase>)
                    var stack = ReflectionHelper.GetFieldValue(uiCtrl, "_uiPhaseStack");
                    if (stack is IEnumerable enumerable)
                    {
                        var phases = new List<string>();
                        foreach (var item in enumerable)
                        {
                            phases.Add(item.ToString());
                        }
                        result["uiPhaseStack"] = phases;
                    }
                }
            }
            catch (Exception e)
            {
                result["uiError"] = e.Message;
            }

            // Active scene detection via GameSceneManager
            try
            {
                var gsm = GameSceneManager.Instance;
                if (gsm != null)
                {
                    if (gsm.titleScene != null && ((Component)gsm.titleScene).gameObject.activeSelf)
                        result["activeScene"] = "Title";
                    else if (gsm.battleScene != null && ((Component)gsm.battleScene).gameObject.activeSelf)
                        result["activeScene"] = "Battle";
                    else if (gsm.storyRoot != null && ((Component)gsm.storyRoot).gameObject.activeSelf)
                        result["activeScene"] = "Story";
                    else
                        result["activeScene"] = "Main";
                }
            }
            catch (Exception e)
            {
                result["sceneError"] = e.Message;
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Progression State  (chapter, opened sephirah, clear info)
        // ----------------------------------------------------------------

        private static Dictionary<string, object> GetProgressionState()
        {
            var result = new Dictionary<string, object>();

            var lib = LibraryModel.Instance;
            if (lib == null) return result;

            // Current chapter (private _currentChapter, exposed via GetChapter())
            result["currentChapter"] = lib.GetChapter();

            // Opened sephirah list (public method)
            var openedList = lib.GetOpenedSephirahList();
            if (openedList != null)
            {
                result["openedSephirah"] = openedList.Select(s => s.ToString()).ToList();
            }

            // Opened floors with levels
            var openedFloors = lib.GetOpenedFloorList();
            if (openedFloors != null)
            {
                var floorSummaries = new List<Dictionary<string, object>>();
                foreach (var floor in openedFloors)
                {
                    floorSummaries.Add(new Dictionary<string, object>
                    {
                        ["sephirah"] = floor.Sephirah.ToString(),
                        ["level"] = floor.Level
                    });
                }
                result["openedFloors"] = floorSummaries;
            }

            // Library level (public)
            result["libraryLevel"] = lib.GetLibraryLevel();

            // Clear info summary
            try
            {
                var clearInfo = lib.ClearInfo;
                if (clearInfo != null)
                {
                    var stageInfoList = ReflectionHelper.GetFieldValue(clearInfo, "_stageInfoList") as IList;
                    result["totalClears"] = stageInfoList?.Count ?? 0;
                }
            }
            catch (Exception e)
            {
                result["clearInfoError"] = e.Message;
            }

            // Play history highlights
            try
            {
                var history = lib.PlayHistory;
                if (history != null)
                {
                    var ph = new Dictionary<string, object>();
                    ph["tutorial_EnterBattle"] = ReflectionHelper.GetFieldValue(history, "tutorial_EnterBattle");
                    ph["Start_EndContents"] = ReflectionHelper.GetFieldValue(history, "Start_EndContents");
                    ph["currentchapterLevel"] = ReflectionHelper.GetFieldValue(history, "currentchapterLevel");
                    result["playHistory"] = ph;
                }
            }
            catch (Exception e)
            {
                result["playHistoryError"] = e.Message;
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Floors State
        // ----------------------------------------------------------------

        private static Dictionary<string, object> GetFloorsState()
        {
            var result = new Dictionary<string, object>();

            var lib = LibraryModel.Instance;
            if (lib == null) return result;

            // Access private _floorList for all floors (including unopened)
            var floorList = ReflectionHelper.GetFieldValue(lib, "_floorList") as IList;
            if (floorList == null) return result;

            var floors = new List<Dictionary<string, object>>();
            foreach (var floorObj in floorList)
            {
                var floor = new Dictionary<string, object>();

                // Sephirah (public property)
                var sephirah = ReflectionHelper.GetFieldValue(floorObj, "Sephirah");
                floor["sephirah"] = sephirah?.ToString();

                // Opened state
                var isOpened = lib.IsOpenedSephirah(
                    sephirah is SephirahType st ? st : SephirahType.None);
                floor["opened"] = isOpened;

                // Level (public property)
                floor["level"] = ReflectionHelper.GetFieldValue(floorObj, "Level");
                floor["maxLevel"] = ReflectionHelper.GetFieldValue(floorObj, "Maxlevel");

                // Progress toward next level
                floor["progress"] = ReflectionHelper.GetFieldValue(floorObj, "_progress");

                // Formation id
                floor["formationId"] = ReflectionHelper.GetFieldValue(floorObj, "_formationId");

                // Units on this floor — try method first, then field
                var getUnitMethod = floorObj.GetType().GetMethod("GetUnitDataList",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getUnitMethod != null)
                {
                    var units = getUnitMethod.Invoke(floorObj, null) as IList;
                    if (units != null)
                    {
                        var unitList = new List<Dictionary<string, object>>();
                        foreach (var unitObj in units)
                        {
                            unitList.Add(ExtractFloorUnit(unitObj));
                        }
                        floor["units"] = unitList;
                    }
                }
                else
                {
                    // Fallback: try the field directly
                    var units = ReflectionHelper.GetFieldValue(floorObj, "_unitDataList") as IList;
                    if (units != null)
                    {
                        var unitList = new List<Dictionary<string, object>>();
                        foreach (var unitObj in units)
                        {
                            unitList.Add(ExtractFloorUnit(unitObj));
                        }
                        floor["units"] = unitList;
                    }
                }

                floors.Add(floor);
            }

            result["floors"] = floors;
            return result;
        }

        /// <summary>
        /// Extract summary info from a UnitDataModel (floor-level librarian data).
        /// </summary>
        private static Dictionary<string, object> ExtractFloorUnit(object unitData)
        {
            var d = new Dictionary<string, object>();
            if (unitData == null) return d;

            d["name"] = ReflectionHelper.GetFieldValue(unitData, "name")
                     ?? ReflectionHelper.GetFieldValue(unitData, "Name");

            // IsLockUnit is a method, not a field
            var isLockVal = false;
            var isLockMethod = unitData.GetType().GetMethod("IsLockUnit",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (isLockMethod != null)
            {
                try { isLockVal = Convert.ToBoolean(isLockMethod.Invoke(unitData, null)); }
                catch { }
            }
            else
            {
                var v = ReflectionHelper.GetFieldValue(unitData, "isLock");
                if (v != null) try { isLockVal = Convert.ToBoolean(v); } catch { }
            }
            d["isLock"] = isLockVal;
            d["isSephirah"] = ReflectionHelper.GetFieldValue(unitData, "isSephirah");

            // Equipped book
            var bookItem = ReflectionHelper.GetFieldValue(unitData, "bookItem");
            if (bookItem != null)
            {
                var classInfo = ReflectionHelper.GetFieldValue(bookItem, "ClassInfo");
                d["bookId"] = classInfo != null
                    ? ReflectionHelper.GetFieldValue(classInfo, "id")
                    : null;
            }

            return d;
        }

        // ----------------------------------------------------------------
        //  Inventory State  (cards, books, drop books)
        // ----------------------------------------------------------------

        private static Dictionary<string, object> GetInventoryState()
        {
            var result = new Dictionary<string, object>();

            // --- Cards (combat pages) ---
            try
            {
                var invModel = Singleton<InventoryModel>.Instance;
                if (invModel != null)
                {
                    var cardList = invModel.GetCardList();
                    var cards = new List<Dictionary<string, object>>();
                    if (cardList != null)
                    {
                        foreach (var cardItem in cardList)
                        {
                            var cardDict = new Dictionary<string, object>();
                            cardDict["id"] = cardItem.GetID().ToString();
                            cardDict["name"] = cardItem.ClassInfo != null
                                ? ReflectionHelper.GetFieldValue(cardItem.ClassInfo, "Name") : null;
                            cardDict["rarity"] = cardItem.ClassInfo != null
                                ? ReflectionHelper.GetFieldValue(cardItem.ClassInfo, "grade") : null;
                            cardDict["cost"] = cardItem.ClassInfo != null
                                ? ReflectionHelper.GetFieldValue(cardItem.ClassInfo, "cost") : null;
                            cardDict["count"] = cardItem.num;
                            cards.Add(cardDict);
                        }
                    }
                    result["cards"] = cards;
                    result["cardTypeCount"] = invModel.GetCardTypeCount();
                }
            }
            catch (Exception e)
            {
                result["cardsError"] = e.Message;
            }

            // --- Books (key pages / core pages) ---
            try
            {
                var bookInv = Singleton<BookInventoryModel>.Instance;
                if (bookInv != null)
                {
                    var bookList = bookInv.GetBookList_equip();
                    var books = new List<Dictionary<string, object>>();
                    if (bookList != null)
                    {
                        foreach (var book in bookList)
                        {
                            var bookDict = new Dictionary<string, object>();
                            bookDict["id"] = book.GetBookClassInfoId().ToString();
                            bookDict["instanceId"] = book.instanceId;
                            bookDict["name"] = book.ClassInfo != null
                                ? ReflectionHelper.GetFieldValue(book.ClassInfo, "Name") : null;
                            bookDict["rarity"] = book.ClassInfo != null
                                ? ReflectionHelper.GetFieldValue(book.ClassInfo, "grade") : null;
                            bookDict["isEquipped"] = book.owner != null;
                            var ownerSep = book.owner != null
                                ? ReflectionHelper.GetFieldValue(book.owner, "OwnerSephirah") : null;
                            bookDict["ownerSephirah"] = ownerSep != null ? ownerSep.ToString() : null;
                            books.Add(bookDict);
                        }
                    }
                    result["books"] = books;
                    result["bookTypeCount"] = bookInv.GetBooksTypeCount();
                }
            }
            catch (Exception e)
            {
                result["booksError"] = e.Message;
            }

            // --- Drop books (invitation books) ---
            try
            {
                var dropInv = Singleton<DropBookInventoryModel>.Instance;
                if (dropInv != null)
                {
                    var dropList = dropInv.GetBookList();
                    var dropBooks = new List<Dictionary<string, object>>();
                    if (dropList != null)
                    {
                        foreach (var dropBook in dropList)
                        {
                            var dbDict = new Dictionary<string, object>();
                            dbDict["id"] = dropBook.XmlInfo?.id?.ToString();
                            dbDict["name"] = dropBook.XmlInfo?.Name;
                            dbDict["num"] = dropBook.num;
                            dbDict["chapter"] = dropBook.XmlInfo?.chapter;
                            dbDict["bookValue"] = dropBook.XmlInfo?.bookvalue;
                            dropBooks.Add(dbDict);
                        }
                    }
                    result["dropBooks"] = dropBooks;
                }
            }
            catch (Exception e)
            {
                result["dropBooksError"] = e.Message;
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Available Stages State
        // ----------------------------------------------------------------

        private static List<Dictionary<string, object>> _stagesCache;
        private static float _stagesCacheTime;
        private const float STAGES_CACHE_TTL = 10f; // seconds

        private static Dictionary<string, object> GetAvailableStagesState()
        {
            var result = new Dictionary<string, object>();

            try
            {
                // Use cached stages if fresh
                if (_stagesCache != null && (Time.time - _stagesCacheTime) < STAGES_CACHE_TTL)
                {
                    result["stages"] = _stagesCache;
                    result["totalCount"] = _stagesCache.Count;
                    return result;
                }

                var stageList = Singleton<StageClassInfoList>.Instance;
                if (stageList == null) return result;

                var allStages = stageList.GetAllDataList();
                if (allStages == null) return result;

                var lib = LibraryModel.Instance;
                var stages = new List<Dictionary<string, object>>();

                foreach (var stage in allStages)
                {
                    var stageDict = new Dictionary<string, object>();
                    stageDict["id"] = stage.id.ToString();
                    stageDict["name"] = ReflectionHelper.GetFieldValue(stage, "stageName");
                    stageDict["stageType"] = stage.stageType.ToString();
                    stageDict["chapter"] = stage.chapter;
                    stageDict["floorNum"] = stage.floorNum;
                    stageDict["isNamed"] = stage.IsNamedStage;

                    // Story type (line)
                    stageDict["storyType"] = stage.storyType;

                    // Clear count from LibraryModel
                    if (lib?.ClearInfo != null)
                    {
                        try
                        {
                            stageDict["clearCount"] = lib.ClearInfo.GetClearCount(stage.id);
                        }
                        catch
                        {
                            stageDict["clearCount"] = 0;
                        }
                    }

                    // Invitation info
                    if (stage.stageType == StageType.Invitation && stage.invitationInfo != null)
                    {
                        var invInfo = new Dictionary<string, object>();
                        invInfo["combine"] = stage.invitationInfo.combine.ToString();
                        invInfo["bookNum"] = stage.invitationInfo.bookNum;
                        invInfo["bookValue"] = stage.invitationInfo.bookValue;
                        if (stage.invitationInfo.needsBooks != null)
                        {
                            invInfo["needsBooks"] = stage.invitationInfo.needsBooks
                                .Select(b => b.ToString()).ToList();
                        }
                        stageDict["invitation"] = invInfo;
                    }

                    stages.Add(stageDict);
                }

                // Update cache
                _stagesCache = stages;
                _stagesCacheTime = Time.time;

                result["stages"] = stages;
                result["totalCount"] = stages.Count;
            }
            catch (Exception e)
            {
                result["error"] = e.Message;
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  Battle State  (the most important layer)
        // ----------------------------------------------------------------

        private static Dictionary<string, object> GetBattleState()
        {
            var result = new Dictionary<string, object>();

            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null)
            {
                result["inBattle"] = false;
                return result;
            }

            // Determine if we are in battle
            var battleState = stageCtrl.battleState;
            var phase = stageCtrl.Phase;
            bool inBattle = battleState != StageController.BattleState.None
                         && phase != StageController.StagePhase.EndBattle;

            result["inBattle"] = inBattle;
            if (!inBattle) return result;

            // --- Core battle info ---
            result["phase"] = phase.ToString();
            result["battleState"] = battleState.ToString();
            result["stageType"] = stageCtrl.stageType.ToString();
            result["currentFloor"] = stageCtrl.CurrentFloor.ToString();
            result["currentWave"] = stageCtrl.CurrentWave;
            result["roundTurn"] = stageCtrl.RoundTurn;
            result["stageState"] = stageCtrl.State.ToString();

            // Stage model info
            try
            {
                var stageModel = stageCtrl.GetStageModel();
                if (stageModel?.ClassInfo != null)
                {
                    result["stageId"] = stageModel.ClassInfo.id.ToString();
                    result["stageName"] = ReflectionHelper.GetFieldValue(stageModel.ClassInfo, "stageName");
                    result["stageChapter"] = stageModel.ClassInfo.chapter;
                }
            }
            catch (Exception e)
            {
                result["stageModelError"] = e.Message;
            }

            // --- Units from BattleObjectManager ---
            try
            {
                var bom = BattleObjectManager.instance;
                if (bom != null)
                {
                    // Player units (faction == Player)
                    var playerUnits = bom.GetList(Faction.Player);
                    var playerList = new List<Dictionary<string, object>>();
                    if (playerUnits != null)
                    {
                        foreach (var unit in playerUnits)
                        {
                            playerList.Add(ExtractBattleUnit(unit));
                        }
                    }
                    result["playerUnits"] = playerList;

                    // Enemy units (faction == Enemy)
                    var enemyUnits = bom.GetList(Faction.Enemy);
                    var enemyList = new List<Dictionary<string, object>>();
                    if (enemyUnits != null)
                    {
                        foreach (var unit in enemyUnits)
                        {
                            enemyList.Add(ExtractBattleUnit(unit));
                        }
                    }
                    result["enemyUnits"] = enemyList;

                    // Summary counts
                    result["playerAliveCount"] = bom.GetAliveList(Faction.Player)?.Count ?? 0;
                    result["enemyAliveCount"] = bom.GetAliveList(Faction.Enemy)?.Count ?? 0;
                }
            }
            catch (Exception e)
            {
                result["unitsError"] = e.Message;
            }

            // --- Equipped cards (_allCardList: assigned playing cards for the round) ---
            try
            {
                var allCards = ReflectionHelper.GetFieldValue(stageCtrl, "_allCardList") as IList;
                if (allCards != null)
                {
                    var equipped = new List<Dictionary<string, object>>();
                    foreach (var cardData in allCards)
                    {
                        var cardEntry = new Dictionary<string, object>();

                        var owner = ReflectionHelper.GetFieldValue(cardData, "owner");
                        var target = ReflectionHelper.GetFieldValue(cardData, "target");
                        var card = ReflectionHelper.GetFieldValue(cardData, "card");

                        cardEntry["ownerIndex"] = ReflectionHelper.GetFieldValue(owner, "index");
                        cardEntry["targetIndex"] = target != null
                            ? ReflectionHelper.GetFieldValue(target, "index")
                            : -1;

                        if (card != null)
                        {
                            var cardDict = new Dictionary<string, object>();

                            // Card id: try GetID() method first, then _id field
                            var getIdMethod = card.GetType().GetMethod("GetID",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (getIdMethod != null)
                                cardDict["id"] = getIdMethod.Invoke(card, null)?.ToString();
                            else
                                cardDict["id"] = ReflectionHelper.GetFieldValue(card, "_id")?.ToString();

                            // Card name: try property, then GetName() method
                            var cardName = ReflectionHelper.GetFieldValue(card, "Name");
                            if (cardName == null)
                            {
                                var getNameMethod = card.GetType().GetMethod("GetName",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (getNameMethod != null)
                                    cardName = getNameMethod.Invoke(card, null);
                            }
                            cardDict["name"] = cardName;

                            // Card cost: try GetCost() method first, then _cost field
                            var getCostMethod = card.GetType().GetMethod("GetCost",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (getCostMethod != null)
                                cardDict["cost"] = getCostMethod.Invoke(card, null);
                            else
                                cardDict["cost"] = ReflectionHelper.GetFieldValue(card, "_cost");

                            cardEntry["card"] = cardDict;
                        }

                        cardEntry["speedValue"] = ReflectionHelper
                            .GetFieldValue(cardData, "speedDiceResultValue");

                        equipped.Add(cardEntry);
                    }
                    result["equippedCards"] = equipped;
                }
            }
            catch (Exception e)
            {
                result["equippedCardsError"] = e.Message;
            }

            return result;
        }

        // ----------------------------------------------------------------
        //  ExtractBattleUnit  –  detailed snapshot of a BattleUnitModel
        // ----------------------------------------------------------------

        private static Dictionary<string, object> ExtractBattleUnit(BattleUnitModel unit)
        {
            var d = new Dictionary<string, object>();
            if (unit == null) return d;

            // --- Identity ---
            d["index"] = unit.index;
            d["id"] = unit.id;
            d["faction"] = unit.faction.ToString();

            // --- HP ---
            d["hp"] = unit.hp;
            d["maxHp"] = unit.MaxHp;

            // --- Turn state ---
            d["turnState"] = unit.turnState.ToString();
            d["isDead"] = unit.IsDead();
            d["isKnockout"] = unit.IsKnockout();
            d["isExtinction"] = unit.IsExtinction();

            // --- Break detail ---
            try
            {
                var breakDetail = unit.breakDetail;
                if (breakDetail != null)
                {
                    d["breakGauge"] = breakDetail.breakGauge;
                    d["breakLife"] = breakDetail.breakLife;
                    d["maxBreakLife"] = ReflectionHelper.GetFieldValue(breakDetail, "MaxBreakLife")
                        ?? ReflectionHelper.GetFieldValue(breakDetail, "maxBreakLife");
                }
            }
            catch (Exception e)
            {
                d["breakError"] = e.Message;
            }

            // --- Emotion detail ---
            try
            {
                var emotionDetail = unit.emotionDetail;
                if (emotionDetail != null)
                {
                    d["emotionLevel"] = emotionDetail.EmotionLevel;
                    d["emotionCoinCount"] = emotionDetail.AllEmotionCoins?.Count ?? 0;
                    d["skillPoint"] = emotionDetail.skillPoint;
                }
            }
            catch (Exception e)
            {
                d["emotionError"] = e.Message;
            }

            // --- Card slot detail (play point / light) ---
            try
            {
                var cardSlotDetail = unit.cardSlotDetail;
                if (cardSlotDetail != null)
                {
                    d["playPoint"] = unit.PlayPoint;
                    d["maxPlayPoint"] = unit.MaxPlayPoint;
                    d["reservedPlayPoint"] = ReflectionHelper
                        .GetFieldValue(cardSlotDetail, "_reservedPlayPoint");
                }
            }
            catch (Exception e)
            {
                d["cardSlotError"] = e.Message;
            }

            // --- Speed dice ---
            try
            {
                var speedDiceResult = unit.speedDiceResult;
                if (speedDiceResult != null)
                {
                    var dice = new List<Dictionary<string, object>>();
                    foreach (var die in speedDiceResult)
                    {
                        dice.Add(new Dictionary<string, object>
                        {
                            ["value"] = die.value,
                            ["faces"] = ReflectionHelper.GetFieldValue(die, "faces"),
                            ["breaked"] = die.breaked,
                            ["isOn"] = ReflectionHelper.GetFieldValue(die, "isOn"),
                            ["isBlocked"] = ReflectionHelper.GetFieldValue(die, "isBlocked")
                        });
                    }
                    d["speedDice"] = dice;
                    d["speedDiceCount"] = unit.speedDiceCount;
                }
            }
            catch (Exception e)
            {
                d["speedDiceError"] = e.Message;
            }

            // --- Hand cards (ally card detail) ---
            try
            {
                var allyCardDetail = unit.allyCardDetail;
                if (allyCardDetail != null)
                {
                    var hand = ReflectionHelper.GetFieldValue(allyCardDetail, "_cardInHand") as IList;
                    if (hand != null)
                    {
                        var cards = new List<Dictionary<string, object>>();
                        foreach (var card in hand)
                        {
                            cards.Add(ExtractCardSummary(card));
                        }
                        d["handCards"] = cards;
                    }

                    d["deckCount"] = (ReflectionHelper
                        .GetFieldValue(allyCardDetail, "_cardInDeck") as IList)?.Count ?? 0;
                    d["discardCount"] = (ReflectionHelper
                        .GetFieldValue(allyCardDetail, "_cardInDiscarded") as IList)?.Count ?? 0;
                    d["inUseCount"] = (ReflectionHelper
                        .GetFieldValue(allyCardDetail, "_cardInUse") as IList)?.Count ?? 0;
                }
            }
            catch (Exception e)
            {
                d["handError"] = e.Message;
            }

            // --- Buffs ---
            try
            {
                var bufListDetail = unit.bufListDetail;
                if (bufListDetail != null)
                {
                    var bufList = ReflectionHelper.GetFieldValue(bufListDetail, "_bufList") as IList;
                    if (bufList != null)
                    {
                        var buffs = new List<Dictionary<string, object>>();
                        foreach (var buf in bufList)
                        {
                            var stack = ReflectionHelper.GetFieldValue(buf, "stack");
                            if (stack != null && Convert.ToInt32(stack) > 0)
                            {
                                buffs.Add(new Dictionary<string, object>
                                {
                                    ["type"] = buf.GetType().Name,
                                    ["stack"] = stack
                                });
                            }
                        }
                        d["buffs"] = buffs;
                    }
                }
            }
            catch (Exception e)
            {
                d["buffsError"] = e.Message;
            }

            // --- Book / key page info ---
            try
            {
                var book = unit.Book;
                if (book != null)
                {
                    d["bookId"] = book.GetBookClassInfoId().ToString();
                    d["bookName"] = book.ClassInfo?.Name;
                }
            }
            catch (Exception e)
            {
                d["bookError"] = e.Message;
            }

            // --- Unit data (name, character) ---
            try
            {
                var unitData = unit.UnitData;
                if (unitData?.unitData != null)
                {
                    d["name"] = unitData.unitData.name;
                }
            }
            catch (Exception e)
            {
                d["unitDataError"] = e.Message;
            }

            // --- Passive abilities ---
            try
            {
                var passiveDetail = unit.passiveDetail;
                if (passiveDetail != null)
                {
                    var passiveList = ReflectionHelper.GetFieldValue(passiveDetail, "PassiveList") as IList
                        ?? ReflectionHelper.GetFieldValue(passiveDetail, "_passiveList") as IList
                        ?? ReflectionHelper.GetFieldValue(passiveDetail, "passiveList") as IList;
                    if (passiveList != null)
                    {
                        var passives = new List<string>();
                        foreach (var passive in passiveList)
                        {
                            var pName = ReflectionHelper.GetFieldValue(passive, "name")
                                     ?? ReflectionHelper.GetFieldValue(passive, "Name");
                            if (pName != null)
                                passives.Add(pName.ToString());
                        }
                        d["passives"] = passives;
                    }
                }
            }
            catch (Exception e)
            {
                d["passivesError"] = e.Message;
            }

            return d;
        }

        // ----------------------------------------------------------------
        //  Helper: extract card summary from a BattleDiceCardModel
        // ----------------------------------------------------------------

        private static Dictionary<string, object> ExtractCardSummary(object card)
        {
            var cardDict = new Dictionary<string, object>();
            if (card == null) return cardDict;

            // Card id
            var getIdMethod = card.GetType().GetMethod("GetID",
                BindingFlags.Public | BindingFlags.Instance);
            if (getIdMethod != null)
                cardDict["id"] = getIdMethod.Invoke(card, null)?.ToString();
            else
                cardDict["id"] = ReflectionHelper.GetFieldValue(card, "_id")?.ToString();

            // Card name: try property, then GetName() method
            var cardName = ReflectionHelper.GetFieldValue(card, "Name");
            if (cardName == null)
            {
                var getNameMethod = card.GetType().GetMethod("GetName",
                    BindingFlags.Public | BindingFlags.Instance);
                if (getNameMethod != null)
                    cardName = getNameMethod.Invoke(card, null);
            }
            cardDict["name"] = cardName;

            // Card cost
            var getCostMethod = card.GetType().GetMethod("GetCost",
                BindingFlags.Public | BindingFlags.Instance);
            if (getCostMethod != null)
                cardDict["cost"] = getCostMethod.Invoke(card, null);
            else
                cardDict["cost"] = ReflectionHelper.GetFieldValue(card, "_cost");

            return cardDict;
        }

        // ----------------------------------------------------------------
        //  Utility
        // ----------------------------------------------------------------

        private static Dictionary<string, object> Error(Exception e)
        {
            return new Dictionary<string, object>
            {
                ["error"] = e.Message,
                ["errorType"] = e.GetType().Name
            };
        }
    }
}
