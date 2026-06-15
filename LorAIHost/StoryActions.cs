using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LorAIHost
{
    public static class StoryActions
    {
        // skipStory: try every available method to skip the current story.
        // Works for both overworld stories and battle story phases.
        public static Dictionary<string, object> DoSkipStory(Dictionary<string, object> args)
        {
            var messages = new List<string>();

            // ── Strategy 1: StoryRoot → storyManager.SkipAll ──
            bool ok = TryInvokeOnStoryManager("SkipAll", new object[0], messages, "SkipAll");
            if (ok)
            {
                // After skipping story, also try to force-advance if in BattleStoryPhase
                TryForceAdvanceBattleStory(messages);
                return Success("Story skipped via SkipAll. Details: " + string.Join("; ", messages));
            }

            // ── Strategy 2: StoryRoot → storyManager.EndStory(true) ──
            ok = TryInvokeOnStoryManager("EndStory", new object[] { true }, messages, "EndStory(true)");
            if (ok)
            {
                TryForceAdvanceBattleStory(messages);
                return Success("Story ended via EndStory(true). Details: " + string.Join("; ", messages));
            }

            // ── Strategy 3: Force-advance StageController if in BattleStoryPhase ──
            bool advanced = TryForceAdvanceBattleStory(messages);
            if (advanced)
                return Success("Battle story force-advanced. Details: " + string.Join("; ", messages));

            // ── Strategy 4: ClickEvent to advance one step ──
            ok = TryInvokeOnStoryManager("ClickEvent", new object[] { true, false }, messages, "ClickEvent");
            if (ok)
                return Success("Story advanced via ClickEvent. Details: " + string.Join("; ", messages));

            return Error("All skip strategies failed. Details: " + string.Join("; ", messages));
        }

        // endStory
        public static Dictionary<string, object> DoEndStory(Dictionary<string, object> args)
        {
            bool forcely = args.TryGetValue("forcely", out var f) && Convert.ToBoolean(f);
            var messages = new List<string>();

            bool ok = TryInvokeOnStoryManager("EndStory", new object[] { forcely }, messages, "EndStory(" + forcely + ")");
            if (ok)
            {
                TryForceAdvanceBattleStory(messages);
                return Success($"Story ended (forcely={forcely}). Details: " + string.Join("; ", messages));
            }

            // Fallback: force-advance
            bool advanced = TryForceAdvanceBattleStory(messages);
            if (advanced)
                return Success("Battle story force-advanced (fallback). Details: " + string.Join("; ", messages));

            return Error("EndStory failed. Details: " + string.Join("; ", messages));
        }

        // advanceStory
        public static Dictionary<string, object> DoAdvanceStory(Dictionary<string, object> args)
        {
            var messages = new List<string>();

            bool ok = TryInvokeOnStoryManager("ClickEvent", new object[] { true, false }, messages, "ClickEvent(true,false)");
            if (!ok)
                ok = TryInvokeOnStoryManager("ClickEvent", new object[] { false, false }, messages, "ClickEvent(false,false)");
            if (!ok)
                ok = TryInvokeOnStoryManager("Advance", new object[0], messages, "Advance");

            if (ok)
                return Success("Story advanced. Details: " + string.Join("; ", messages));

            // Fallback: force-advance battle story
            bool advanced = TryForceAdvanceBattleStory(messages);
            if (advanced)
                return Success("Battle story force-advanced (fallback). Details: " + string.Join("; ", messages));

            return Error("Advance failed. Details: " + string.Join("; ", messages));
        }

        // ═══════════════════════════════════════════════════════════
        //  Private helpers
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Find the StoryManager via StoryRoot and invoke a method on it.
        /// Returns true if the method was found and invoked without error.
        /// </summary>
        private static bool TryInvokeOnStoryManager(
            string methodName, object[] methodArgs,
            List<string> messages, string label)
        {
            try
            {
                var storyRootType = ReflectionHelper.FindType("StoryRoot");
                if (storyRootType == null)
                {
                    messages.Add(label + ": StoryRoot type not found");
                    return false;
                }

                var storyRoot = UnityEngine.Object.FindObjectOfType(storyRootType);
                if (storyRoot == null)
                {
                    messages.Add(label + ": StoryRoot instance not found in scene");
                    return false;
                }

                var storyManager = ReflectionHelper.GetFieldValue(storyRoot, "storyManager");
                if (storyManager == null)
                {
                    messages.Add(label + ": storyManager field is null");
                    return false;
                }

                ReflectionHelper.InvokeMethod(storyManager, methodName, methodArgs);
                messages.Add(label + ": invoked successfully");
                Debug.Log("[LorAI] StoryActions " + label + " invoked successfully");
                return true;
            }
            catch (Exception ex)
            {
                messages.Add(label + ": " + ex.Message);
                Debug.LogWarning("[LorAI] StoryActions " + label + " failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// If StageController is in BattleStoryPhase, force-advance to RoundStartPhase_UI.
        /// Returns true if phase was advanced.
        /// </summary>
        private static bool TryForceAdvanceBattleStory(List<string> messages)
        {
            try
            {
                var stageCtrl = Singleton<StageController>.Instance;
                if (stageCtrl == null)
                {
                    messages.Add("forceAdvance: StageController is null");
                    return false;
                }

                var phase = stageCtrl.Phase.ToString();
                if (phase != "BattleStoryPhase")
                {
                    messages.Add("forceAdvance: not in BattleStoryPhase (current: " + phase + ")");
                    return false;
                }

                // Directly set the phase field
                FieldInfo phaseField = typeof(StageController).GetField("_phase",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (phaseField == null)
                {
                    messages.Add("forceAdvance: _phase field not found");
                    return false;
                }

                Type phaseEnum = typeof(StageController).GetNestedType("StagePhase",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (phaseEnum == null)
                {
                    messages.Add("forceAdvance: StagePhase enum not found");
                    return false;
                }

                object targetVal = Enum.Parse(phaseEnum, "RoundStartPhase_UI", true);
                phaseField.SetValue(stageCtrl, targetVal);
                messages.Add("forceAdvance: BattleStoryPhase → RoundStartPhase_UI");
                Debug.Log("[LorAI] Battle story force-advanced: BattleStoryPhase → RoundStartPhase_UI");
                return true;
            }
            catch (Exception ex)
            {
                messages.Add("forceAdvance: " + ex.Message);
                Debug.LogWarning("[LorAI] Battle story force-advance failed: " + ex.Message);
                return false;
            }
        }

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
