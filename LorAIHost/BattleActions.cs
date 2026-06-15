using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UI;

namespace LorAIHost
{
    public static class BattleActions
    {
        // startStage: set stage info (no coroutine, immediate)
        public static Dictionary<string, object> DoStartStage(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("stageId", out var stageIdObj))
                return Error("stageId parameter required");

            int stageId = Convert.ToInt32(stageIdObj);
            var stageInfo = Singleton<StageClassInfoList>.Instance.GetData(stageId);
            if (stageInfo == null) return Error($"Stage {stageId} not found");

            // Try invitation flow
            var invPanel = UnityEngine.Object.FindObjectOfType<UIInvitationPanel>();
            if (invPanel != null)
            {
                try
                {
                    invPanel.SetCurrentStage(stageInfo);
                    return Success($"Stage {stageId} set on invitation panel");
                }
                catch (Exception ex) { return Error($"SetCurrentStage failed: {ex.Message}"); }
            }

            return Error("UIInvitationPanel not found");
        }

        // runStage: full coroutine flow (navigate -> invite -> battle -> autoPlay)
        public static Dictionary<string, object> DoRunStage(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("stageId", out var stageIdObj))
                return Error("stageId parameter required");

            int stageId = Convert.ToInt32(stageIdObj);

            // Navigate to Invitation
            var uiCtrl = UI.UIController.Instance;
            if (uiCtrl != null) uiCtrl.CallUIPhase(UI.UIPhase.Invitation);

            // Get stage info
            var stageInfo = Singleton<StageClassInfoList>.Instance.GetData(stageId);
            if (stageInfo == null) return Error($"Stage {stageId} not found");

            // Create deferred request
            string reqId = "runstage_" + Interlocked.Increment(ref UpdateHook._nextDeferredId);

            // Find host and start coroutine
            var host = UnityEngine.Object.FindObjectOfType<UpdateHook>();
            if (host == null) return Error("UpdateHook not found");

            host.StartCoroutine(RunStageCoroutine(stageId, stageInfo, reqId));

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["_deferred"] = true,
                ["_deferredReqId"] = reqId,
                ["message"] = "runStage coroutine started"
            };
        }

        private static IEnumerator RunStageCoroutine(int stageId, StageClassInfo stageInfo, string reqId)
        {
            var result = new Dictionary<string, object>();

            // Wait for UI to initialize (yield outside try)
            yield return new WaitForSeconds(2f);

            // Set current stage on invitation panel
            UIInvitationPanel invPanel = null;
            try { invPanel = UnityEngine.Object.FindObjectOfType<UIInvitationPanel>(); }
            catch (Exception ex) { result["error"] = "FindObjectOfType failed: " + ex.Message; }
            if (invPanel == null)
            {
                result["error"] = "UIInvitationPanel not found after 2s wait";
                ArchiveResult(reqId, result);
                yield break;
            }

            try { invPanel.SetCurrentStage(stageInfo); }
            catch (Exception ex)
            {
                result["error"] = "SetCurrentStage failed: " + ex.Message;
                ArchiveResult(reqId, result);
                yield break;
            }

            yield return new WaitForSeconds(1f);

            // Get right panel and set up invitation
            UIInvitationRightMainPanel rightPanel = null;
            try { rightPanel = ReflectionHelper.GetFieldValue(invPanel, "_invRightMainPanel") as UIInvitationRightMainPanel; }
            catch { }

            if (rightPanel == null)
            {
                // Retry 3 times
                for (int i = 0; i < 3; i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    try { rightPanel = ReflectionHelper.GetFieldValue(invPanel, "_invRightMainPanel") as UIInvitationRightMainPanel; }
                    catch { }
                    if (rightPanel != null) break;
                }
            }

            if (rightPanel != null)
            {
                try
                {
                    rightPanel.Initialized();
                    rightPanel.OpenInit();

                    // Get LorId from stageInfo
                    var lorId = stageInfo.id;
                    rightPanel.ApplyFixedInviationSlotIdAuto(lorId);

                    // Send invitation
                    rightPanel.OnClickSendButton();
                }
                catch (Exception ex)
                {
                    result["error"] = "Invitation setup failed: " + ex.Message;
                    ArchiveResult(reqId, result);
                    yield break;
                }
            }

            yield return new WaitForSeconds(2f);

            // Skip story
            try { StoryActions.DoSkipStory(new Dictionary<string, object>()); }
            catch (Exception ex) { Debug.LogWarning("[LorAI] runStage skipStory #1: " + ex.Message); }
            yield return new WaitForSeconds(2f);
            try { StoryActions.DoSkipStory(new Dictionary<string, object>()); }
            catch (Exception ex) { Debug.LogWarning("[LorAI] runStage skipStory #2: " + ex.Message); }
            yield return new WaitForSeconds(1f);

            // Start battle
            try
            {
                var battlePanel = UnityEngine.Object.FindObjectOfType<UIBattleSettingPanel>();
                if (battlePanel != null)
                {
                    ReflectionHelper.InvokeMethod(battlePanel, "OnClickBattleStart", new object[0]);
                }
                else
                {
                    Singleton<StageController>.Instance.StartBattle();
                }
            }
            catch (Exception ex)
            {
                result["error"] = "Start battle failed: " + ex.Message;
                ArchiveResult(reqId, result);
                yield break;
            }

            yield return new WaitForSeconds(1f);

            // Auto play first round
            try { DoAutoPlay(new Dictionary<string, object>()); }
            catch (Exception ex) { Debug.LogWarning("[LorAI] runStage autoPlay: " + ex.Message); }

            result["success"] = true;
            result["message"] = $"runStage completed for stage {stageId}";

            ArchiveResult(reqId, result);
        }

        private static void ArchiveResult(string reqId, Dictionary<string, object> result)
        {
            lock (UpdateHook._deferLock)
            {
                UpdateHook._deferredResults[reqId] = result;
            }
        }

        // startBattle: start battle from setting panel
        public static Dictionary<string, object> DoStartBattle(Dictionary<string, object> args)
        {
            var battlePanel = UnityEngine.Object.FindObjectOfType<UIBattleSettingPanel>();
            if (battlePanel != null)
            {
                try { ReflectionHelper.InvokeMethod(battlePanel, "OnClickBattleStart", new object[0]); }
                catch { }
                return Success("Battle started via UIBattleSettingPanel");
            }

            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController.Instance is null");
            stageCtrl.StartBattle();
            return Success("Battle started via StageController");
        }

        // autoPlay: game's built-in auto card placement
        public static Dictionary<string, object> DoAutoPlay(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");
            stageCtrl.SetAutoCardForPlayer();
            return Success("AutoPlay executed");
        }

        // confirmCards: confirm card placement and advance round
        public static Dictionary<string, object> DoConfirmCards(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");

            // Check phase
            var phase = stageCtrl.Phase;
            if (phase.ToString() != "ApplyLibrarianCardPhase")
                return Error($"Not in card phase. Current: {phase}");

            stageCtrl.CompleteApplyingLibrarianCardPhase(false);
            return Success("Cards confirmed, round advancing");
        }

        // playBattleRound: atomic autoPlay + confirmCards
        public static Dictionary<string, object> DoPlayBattleRound(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");

            var phase = stageCtrl.Phase;
            var phaseStr = phase.ToString();

            if (phaseStr != "ApplyLibrarianCardPhase")
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["acted"] = false,
                    ["phase"] = phaseStr,
                    ["message"] = "Not in card phase, no action taken"
                };
            }

            // Auto play
            stageCtrl.SetAutoCardForPlayer();
            // Confirm
            stageCtrl.CompleteApplyingLibrarianCardPhase(false);

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["acted"] = true,
                ["phase"] = phaseStr,
                ["message"] = "AutoPlay + ConfirmCards executed"
            };
        }

        // endBattle
        public static Dictionary<string, object> DoEndBattle(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");
            stageCtrl.EndBattle();
            return Success("EndBattle called");
        }

        // closeBattleScene
        public static Dictionary<string, object> DoCloseBattleScene(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");
            stageCtrl.CloseBattleScene();
            return Success("CloseBattleScene called");
        }

        // clickBattleResult
        public static Dictionary<string, object> DoClickBattleResult(Dictionary<string, object> args)
        {
            var resultPanel = UnityEngine.Object.FindObjectOfType<UIBattleResultPanel>();
            if (resultPanel == null) return Error("UIBattleResultPanel not found");
            resultPanel.OnClickEndBattleButton();
            return Success("Battle result clicked");
        }

        // gameOver
        public static Dictionary<string, object> DoGameOver(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");

            bool isWin = args.TryGetValue("isWin", out var w) && Convert.ToBoolean(w);
            bool isBackButton = args.TryGetValue("isBackButton", out var b) && Convert.ToBoolean(b);
            stageCtrl.GameOver(isWin, isBackButton);
            return Success($"GameOver called: isWin={isWin}, isBackButton={isBackButton}");
        }

        // killAllEnemy (cheat/test)
        public static Dictionary<string, object> DoKillAllEnemy(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");
            stageCtrl.KillAllEnemy();
            return Success("All enemies killed");
        }

        // getStageInfo
        public static Dictionary<string, object> DoGetStageInfo(Dictionary<string, object> args)
        {
            var stageCtrl = Singleton<StageController>.Instance;
            if (stageCtrl == null) return Error("StageController not found");

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["state"] = stageCtrl.State.ToString(),
                ["phase"] = stageCtrl.Phase.ToString(),
                ["currentWave"] = stageCtrl.CurrentWave,
                ["roundTurn"] = stageCtrl.RoundTurn,
                ["isEndContents"] = stageCtrl.IsEndContents
            };
        }

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
