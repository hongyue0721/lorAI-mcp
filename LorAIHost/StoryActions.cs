using System;
using System.Collections.Generic;
using UnityEngine;

namespace LorAIHost
{
    public static class StoryActions
    {
        // skipStory: skip current story
        public static Dictionary<string, object> DoSkipStory(Dictionary<string, object> args)
        {
            var storyRootType = ReflectionHelper.FindType("StoryRoot");
            if (storyRootType == null) return Error("StoryRoot type not found in any assembly");

            // Try StoryRoot.Instance.storyManager.SkipAll()
            var storyRoot = UnityEngine.Object.FindObjectOfType(storyRootType);
            if (storyRoot != null)
            {
                var storyManager = ReflectionHelper.GetFieldValue(storyRoot, "storyManager");
                if (storyManager != null)
                {
                    try
                    {
                        ReflectionHelper.InvokeMethod(storyManager, "SkipAll", new object[0]);
                        return Success("Story skipped via SkipAll");
                    }
                    catch (Exception ex) { Debug.LogWarning("[LorAI] SkipAll failed: " + ex.Message); }

                    try
                    {
                        ReflectionHelper.InvokeMethod(storyManager, "EndStory", new object[] { true });
                        return Success("Story ended via EndStory(true)");
                    }
                    catch (Exception ex) { Debug.LogWarning("[LorAI] EndStory(true) failed: " + ex.Message); }
                }
            }

            return Error("No active story to skip");
        }

        // endStory
        public static Dictionary<string, object> DoEndStory(Dictionary<string, object> args)
        {
            bool forcely = args.TryGetValue("forcely", out var f) && Convert.ToBoolean(f);

            var storyRootType = ReflectionHelper.FindType("StoryRoot");
            if (storyRootType == null) return Error("StoryRoot type not found in any assembly");

            var storyRoot = UnityEngine.Object.FindObjectOfType(storyRootType);
            if (storyRoot != null)
            {
                var storyManager = ReflectionHelper.GetFieldValue(storyRoot, "storyManager");
                if (storyManager != null)
                {
                    ReflectionHelper.InvokeMethod(storyManager, "EndStory", new object[] { forcely });
                    return Success($"Story ended (forcely={forcely})");
                }
            }

            return Error("No active story to end");
        }

        // advanceStory
        public static Dictionary<string, object> DoAdvanceStory(Dictionary<string, object> args)
        {
            var storyRootType = ReflectionHelper.FindType("StoryRoot");
            if (storyRootType == null) return Error("StoryRoot type not found in any assembly");

            var storyRoot = UnityEngine.Object.FindObjectOfType(storyRootType);
            if (storyRoot != null)
            {
                var storyManager = ReflectionHelper.GetFieldValue(storyRoot, "storyManager");
                if (storyManager != null)
                {
                    ReflectionHelper.InvokeMethod(storyManager, "ClickEvent", new object[] { true, false });
                    return Success("Story advanced");
                }
            }

            return Error("No active story to advance");
        }

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
