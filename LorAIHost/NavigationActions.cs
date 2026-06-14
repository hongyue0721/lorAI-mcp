using System;
using System.Collections.Generic;
using UI;

namespace LorAIHost
{
    public static class NavigationActions
    {
        // navigate: change UI phase
        // args: { "phase": "Sephirah" | "Invitation" | "BattleSetting" | ... }
        public static Dictionary<string, object> DoNavigate(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("phase", out var phaseObj))
                return Error("phase parameter required");

            var ui = UI.UIController.Instance;
            if (ui == null) return Error("UIController.Instance is null");

            var phase = (UI.UIPhase)Enum.Parse(typeof(UI.UIPhase), phaseObj.ToString(), true);
            ui.CallUIPhase(phase);

            return Success($"Navigated to {phase}");
        }

        // selectSephirah: select a floor
        // args: { "sephirah": "Malkuth" | "Yesod" | ... }
        public static Dictionary<string, object> DoSelectSephirah(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("sephirah", out var sepObj))
                return Error("sephirah parameter required");

            var ui = UI.UIController.Instance;
            if (ui == null) return Error("UIController.Instance is null");

            var sephirah = (SephirahType)Enum.Parse(typeof(SephirahType), sepObj.ToString(), true);
            ui.SetCurrentSephirah(sephirah);

            return Success($"Selected sephirah: {sephirah}");
        }

        // getFloor: get floor info
        // args: { "sephirah": "Malkuth" }
        public static Dictionary<string, object> DoGetFloor(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("sephirah", out var sepObj))
                return Error("sephirah parameter required");

            var sephirah = (SephirahType)Enum.Parse(typeof(SephirahType), sepObj.ToString(), true);
            var libModel = LibraryModel.Instance;
            if (libModel == null) return Error("LibraryModel not found");

            var floor = libModel.GetFloor(sephirah);
            if (floor == null) return Error($"Floor {sephirah} not found");

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["sephirah"] = sephirah.ToString(),
                ["level"] = floor.Level,
                ["progress"] = ReflectionHelper.GetFieldValue(floor, "_progress"),
            };
        }

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
