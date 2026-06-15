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

            if (!Enum.TryParse(phaseObj.ToString(), true, out UI.UIPhase phase))
                return Error($"Invalid phase '{phaseObj}'. Valid: " +
                    string.Join(", ", Enum.GetNames(typeof(UI.UIPhase))));
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

            if (!Enum.TryParse(sepObj.ToString(), true, out SephirahType sephirah))
                return Error($"Invalid sephirah '{sepObj}'. Valid: " +
                    string.Join(", ", Enum.GetNames(typeof(SephirahType))));
            ui.SetCurrentSephirah(sephirah);

            return Success($"Selected sephirah: {sephirah}");
        }

        // getFloor: get floor info
        // args: { "sephirah": "Malkuth" }
        public static Dictionary<string, object> DoGetFloor(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("sephirah", out var sepObj))
                return Error("sephirah parameter required");

            if (!Enum.TryParse(sepObj.ToString(), true, out SephirahType sephirahFloor))
                return Error($"Invalid sephirah '{sepObj}'. Valid: " +
                    string.Join(", ", Enum.GetNames(typeof(SephirahType))));
            var libModel = LibraryModel.Instance;
            if (libModel == null) return Error("LibraryModel not found");

            var floor = libModel.GetFloor(sephirahFloor);
            if (floor == null) return Error($"Floor {sephirahFloor} not found");

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["sephirah"] = sephirahFloor.ToString(),
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
