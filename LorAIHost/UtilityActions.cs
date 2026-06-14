using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UI;

namespace LorAIHost
{
    public static class UtilityActions
    {
        // listMethods: list all public methods on a type
        public static Dictionary<string, object> DoListMethods(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("type", out var typeObj))
                return Error("type parameter required");

            var type = ReflectionHelper.FindType(typeObj.ToString());
            if (type == null) return Error($"Type '{typeObj}' not found");

            var methods = ReflectionHelper.ListMethods(type);
            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["type"] = type.FullName,
                ["methods"] = methods
            };
        }

        // callMethod: invoke arbitrary method via reflection
        public static Dictionary<string, object> DoCallMethod(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("type", out var typeObj))
                return Error("type parameter required");
            if (!args.TryGetValue("method", out var methodObj))
                return Error("method parameter required");

            var typeName = typeObj.ToString();
            var methodName = methodObj.ToString();

            // Find the type
            var type = ReflectionHelper.FindType(typeName);
            if (type == null) return Error($"Type '{typeName}' not found");

            // Get instance (singleton or find in scene)
            var instance = ReflectionHelper.GetSingleton(typeName) ?? ReflectionHelper.FindObjectInstance(typeName);
            if (instance == null) return Error($"No instance found for '{typeName}'");

            // Parse args
            object[] methodArgs = new object[0];
            if (args.TryGetValue("args", out var argsList) && argsList is IList argList)
            {
                methodArgs = ReflectionHelper.ParseTypedArgs(new List<object>(argList.Cast<object>()));
            }

            // Invoke
            var result = ReflectionHelper.InvokeMethod(instance, methodName, methodArgs);

            return new Dictionary<string, object>
            {
                ["success"] = true,
                ["type"] = typeName,
                ["method"] = methodName,
                ["result"] = result?.ToString() ?? "null"
            };
        }

        // getGameState: diagnostic dump
        public static Dictionary<string, object> DoGetGameState(Dictionary<string, object> args)
        {
            var result = new Dictionary<string, object>();

            // Check available singletons
            var checks = new (string Name, Func<bool> Check)[]
            {
                ("UIController", () => ReflectionHelper.GetSingleton("UI.UIController") != null),
                ("StageController", () => Singleton<StageController>.Instance != null),
                ("LibraryModel", () => LibraryModel.Instance != null),
                ("GameSceneManager", () => GameSceneManager.Instance != null),
                ("BattleObjectManager", () => BattleObjectManager.instance != null),
            };

            var singletons = new Dictionary<string, object>();
            foreach (var (name, check) in checks)
            {
                try { singletons[name] = check(); }
                catch { singletons[name] = "error"; }
            }
            result["singletons"] = singletons;

            // Stage controller details
            try
            {
                var sc = Singleton<StageController>.Instance;
                if (sc != null)
                {
                    result["stageState"] = sc.State.ToString();
                    result["stagePhase"] = sc.Phase.ToString();
                    result["battleState"] = sc.battleState.ToString();
                    result["currentWave"] = sc.CurrentWave;
                    result["roundTurn"] = sc.RoundTurn;
                }
            }
            catch (Exception e) { result["stageError"] = e.Message; }

            return result;
        }

        private static Dictionary<string, object> Success(string msg) =>
            new Dictionary<string, object> { ["success"] = true, ["message"] = msg };
        private static Dictionary<string, object> Error(string msg) =>
            new Dictionary<string, object> { ["success"] = false, ["error"] = msg };
    }
}
