using System;
using UnityEngine;
using UI;
using UI.Title;
namespace LorAIHost
{
    /// <summary>
    /// 从游戏运行时对象采集 GameStateFacts（唯一采集入口）。
    /// 每个探针独立 try/catch：采不到就保持默认值，
    /// 由 ActionAvailability 按“不可判定 → 不 available”处理，绝不猜测。
    /// 必须在 Unity 主线程调用（HttpServer 队列已在主线程处理）。
    /// </summary>
    public static class GameFactsCollector
    {
        public static GameStateFacts Collect()
        {
            var f = new GameStateFacts();

            // ── 场景归属（与 StateExporter 导航层同源判定）──
            try
            {
                var gsm = GameSceneManager.Instance;
                if (gsm != null)
                {
                    if (gsm.titleScene != null && ((Component)gsm.titleScene).gameObject.activeSelf)
                        f.ActiveScene = "Title";
                    else if (gsm.battleScene != null && ((Component)gsm.battleScene).gameObject.activeSelf)
                        f.ActiveScene = "Battle";
                    else if (gsm.storyRoot != null && ((Component)gsm.storyRoot).gameObject.activeSelf)
                        f.ActiveScene = "Story";
                    else
                        f.ActiveScene = "Main";
                }
            }
            catch { }

            // ── UI 控制器 ──
            try
            {
                var ui = UI.UIController.Instance;
                if (ui != null) f.UiPhase = ui.CurrentUIPhase.ToString();
            }
            catch { }

            // ── 战斗状态机（inBattle 语义与 StateExporter.GetBattleState 完全一致）──
            try
            {
                var sc = Singleton<StageController>.Instance;
                if (sc != null)
                {
                    f.StageControllerExists = true;
                    f.BattleState = sc.battleState.ToString();
                    f.BattlePhase = sc.Phase.ToString();
                    f.InBattle = f.BattleState != "None" && f.BattlePhase != "EndBattle";
                }
            }
            catch { }

            // ── 图书馆模型（库存/楼层可查询的前提）──
            try { f.LibraryReady = LibraryModel.Instance != null; }
            catch { }

            // ── 标题控制器 ──
            try { f.TitleUiAvailable = UITitleController.Controller != null; }
            catch { }

            // ── 面板在场性：只在对应场景探测，避免战斗内每帧全场景搜索的开销 ──
            if (f.ActiveScene == "Main")
            {
                try { f.InvitationPanelActive = UnityEngine.Object.FindObjectOfType<UIInvitationPanel>() != null; }
                catch { }
                try { f.BattleSettingPanelActive = UnityEngine.Object.FindObjectOfType<UIBattleSettingPanel>() != null; }
                catch { }
            }
            else if (f.ActiveScene == "Battle")
            {
                try { f.BattleResultPanelActive = UnityEngine.Object.FindObjectOfType<UIBattleResultPanel>() != null; }
                catch { }
            }

            // ── 情绪卡选择：levelup UI enabled 且存在活跃候选（与 selectEmotionCard 的前置同源）──
            if (f.InBattle && f.BattlePhase == "RoundEndPhase")
            {
                try
                {
                    var levelupUi = AdvancedActions.GetLevelUpUI();
                    if (levelupUi != null && AdvancedActions.IsLevelUpUIEnabled(levelupUi))
                    {
                        var raw = AdvancedActions.GetCandidatesArray(levelupUi);
                        if (raw != null)
                        {
                            foreach (var c in raw)
                                if (c != null) { f.EmotionSelectActive = true; break; }
                        }
                    }
                }
                catch { }
            }

            return f;
        }
    }
}
