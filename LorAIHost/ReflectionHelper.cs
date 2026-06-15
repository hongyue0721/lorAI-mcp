using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LorAIHost
{
    /// <summary>
    /// Shared reflection utilities for finding game types, singletons, and invoking members
    /// in Library of Ruina (Assembly-CSharp.dll loaded by Unity).
    /// </summary>
    public static class ReflectionHelper
    {
        private static ConcurrentDictionary<string, Type> _typeCache = new ConcurrentDictionary<string, Type>();

        // ───────────────────────── Type Lookup ─────────────────────────

        /// <summary>
        /// Find a type by name. Searches Assembly-CSharp first, then all loaded assemblies.
        /// Results are cached.
        /// </summary>
        public static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            if (_typeCache.TryGetValue(typeName, out Type cached))
                return cached;

            Type found = null;

            // Try direct lookup (works for fully qualified names)
            found = Type.GetType(typeName);
            if (found != null)
            {
                _typeCache[typeName] = found;
                return found;
            }

            // Search all loaded assemblies, prioritizing Assembly-CSharp
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Assembly csharpAssembly = null;

            foreach (var asm in assemblies)
            {
                string asmName = asm.GetName().Name;
                if (asmName == "Assembly-CSharp")
                {
                    csharpAssembly = asm;
                    break;
                }
            }

            // Check Assembly-CSharp first
            if (csharpAssembly != null)
            {
                found = FindTypeInAssembly(csharpAssembly, typeName);
                if (found != null)
                {
                    _typeCache[typeName] = found;
                    return found;
                }
            }

            // Fallback: search all assemblies
            foreach (var asm in assemblies)
            {
                if (asm == csharpAssembly)
                    continue;

                found = FindTypeInAssembly(asm, typeName);
                if (found != null)
                {
                    _typeCache[typeName] = found;
                    return found;
                }
            }

            // Cache null results too to avoid repeated assembly scans
            _typeCache.TryAdd(typeName, null);
            return null;
        }

        private static Type FindTypeInAssembly(Assembly asm, string typeName)
        {
            // Try exact match first (handles "Namespace.TypeName" correctly)
            Type t = asm.GetType(typeName, false);
            if (t != null)
                return t;

            // Short name match — collect all candidates, then prefer
            // types that have a namespace over global ones to avoid
            // collisions (e.g. "UIController" matches both global and UI.UIController)
            Type globalMatch = null;
            Type namespacedMatch = null;
            foreach (var candidate in asm.GetTypes())
            {
                if (candidate.Name != typeName && candidate.FullName != typeName)
                    continue;
                if (string.IsNullOrEmpty(candidate.Namespace))
                    globalMatch = globalMatch ?? candidate;
                else
                    namespacedMatch = namespacedMatch ?? candidate;
            }

            return namespacedMatch ?? globalMatch;
        }

        // ───────────────────────── Singleton Access ─────────────────────────

        /// <summary>
        /// Find and return the singleton instance of a type.
        /// Tries common Library of Ruina singleton patterns:
        /// 1. Static property "Instance" or "instance"
        /// 2. Static field "_instance"
        /// 3. FindObjectOfType for MonoBehaviour types
        /// 4. Generic Singleton&lt;T&gt;.Instance via reflection
        /// </summary>
        public static object GetSingleton(string typeName)
        {
            Type type = FindType(typeName);
            if (type == null)
                return null;

            // 1. Static property "Instance" or "instance"
            foreach (var propName in new[] { "Instance", "instance" })
            {
                var prop = type.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (prop != null)
                {
                    try
                    {
                        object val = prop.GetValue(null);
                        if (val != null) return val;
                    }
                    catch { }
                }
            }

            // 2. Static field "_instance" (or "s_instance", "m_instance")
            foreach (var fieldName in new[] { "_instance", "s_instance", "m_instance", "_Instance" })
            {
                var field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (field != null)
                {
                    try
                    {
                        object val = field.GetValue(null);
                        if (val != null) return val;
                    }
                    catch { }
                }
            }

            // 3. FindObjectOfType for MonoBehaviour-derived types
            if (typeof(MonoBehaviour).IsAssignableFrom(type) ||
                typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                try
                {
                    var findMethod = typeof(UnityEngine.Object).GetMethod(
                        "FindObjectOfType",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Type) },
                        null);

                    if (findMethod != null)
                    {
                        object val = findMethod.Invoke(null, new object[] { type });
                        if (val != null) return val;
                    }
                }
                catch { }
            }

            // 4. Generic Singleton<T>.Instance — walk base types looking for Singleton<> parent
            Type current = type;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType &&
                    current.GetGenericTypeDefinition().Name.StartsWith("Singleton"))
                {
                    var instanceProp = current.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (instanceProp != null)
                    {
                        try
                        {
                            object val = instanceProp.GetValue(null);
                            if (val != null) return val;
                        }
                        catch { }
                    }
                }

                current = current.BaseType;
            }

            return null;
        }

        // ───────────────────────── Scene Object Lookup ─────────────────────────

        /// <summary>
        /// Find a UnityEngine.Object instance in the scene by type name.
        /// </summary>
        public static object FindObjectInstance(string typeName)
        {
            Type type = FindType(typeName);
            if (type == null)
                return null;

            if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                return null;

            try
            {
                var findMethod = typeof(UnityEngine.Object).GetMethod(
                    "FindObjectOfType",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Type) },
                    null);

                if (findMethod != null)
                {
                    return findMethod.Invoke(null, new object[] { type });
                }
            }
            catch { }

            return null;
        }

        // ───────────────────────── Field / Property Access ─────────────────────────

        /// <summary>
        /// Get a field or property value from an object instance.
        /// Searches both fields and properties, public and non-public.
        /// </summary>
        public static object GetFieldValue(object obj, string fieldName)
        {
            if (obj == null || string.IsNullOrEmpty(fieldName))
                return null;

            Type type = obj.GetType();

            // Try property first
            var prop = type.GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop != null)
            {
                try { return prop.GetValue(obj); }
                catch { }
            }

            // Try field
            var field = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (field != null)
            {
                try { return field.GetValue(obj); }
                catch { }
            }

            // Try nested member access (e.g. "a.b.c")
            int dotIndex = fieldName.IndexOf('.');
            if (dotIndex > 0)
            {
                string first = fieldName.Substring(0, dotIndex);
                string rest = fieldName.Substring(dotIndex + 1);
                object inner = GetFieldValue(obj, first);
                if (inner != null)
                    return GetFieldValue(inner, rest);
            }

            return null;
        }

        /// <summary>
        /// Get a static field or property value from a type.
        /// </summary>
        public static object GetStaticValue(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            var prop = type.GetProperty(name, flags);
            if (prop != null)
            {
                try { return prop.GetValue(null); }
                catch { }
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                try { return field.GetValue(null); }
                catch { }
            }

            return null;
        }

        // ───────────────────────── Method Invocation ─────────────────────────

        /// <summary>
        /// Invoke an instance method by name. Tries best-match by parameter count.
        /// </summary>
        public static object InvokeMethod(object obj, string methodName, object[] args)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
                return null;

            Type type = obj.GetType();
            int argCount = args != null ? args.Length : 0;

            // Find all methods with matching name and arg count
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(m => m.Name == methodName && m.GetParameters().Length == argCount)
                .ToArray();

            if (methods.Length == 0)
            {
                // Fallback: find methods with at least the requested arg count
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                    .Where(m => m.Name == methodName && m.GetParameters().Length >= argCount)
                    .ToArray();
            }

            if (methods.Length == 0)
            {
                throw new MissingMethodException(type.FullName, methodName +
                    " (no overload found with " + argCount + " or more parameters)");
            }

            try
            {
                return methods[0].Invoke(obj, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        /// <summary>
        /// Invoke a static method by name on a type.
        /// </summary>
        public static object InvokeStaticMethod(Type type, string methodName, object[] args)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            int argCount = args != null ? args.Length : 0;

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(m => m.Name == methodName && m.GetParameters().Length == argCount)
                .ToArray();

            if (methods.Length == 0)
            {
                methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(m => m.Name == methodName)
                    .ToArray();
            }

            if (methods.Length == 0)
                return null;

            try
            {
                return methods[0].Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        // ───────────────────────── Enum Helpers ─────────────────────────

        /// <summary>
        /// Parse an enum value from a type name and value name string.
        /// Returns null if type not found or value invalid.
        /// </summary>
        public static object ParseEnum(string typeName, string valueName)
        {
            Type type = FindType(typeName);
            if (type == null || !type.IsEnum)
                return null;

            try
            {
                return Enum.Parse(type, valueName, true);
            }
            catch
            {
                return null;
            }
        }

        // ───────────────────────── Introspection ─────────────────────────

        /// <summary>
        /// Get all public instance method signatures of a type (name + parameter types).
        /// </summary>
        public static List<string> ListMethods(Type type)
        {
            var result = new List<string>();
            if (type == null)
                return result;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
            {
                var paramTypes = method.GetParameters()
                    .Select(p => p.ParameterType.Name)
                    .ToArray();
                result.Add(string.Format("{0}({1})", method.Name, string.Join(", ", paramTypes)));
            }

            return result;
        }

        // ───────────────────────── Typed Args Parsing ─────────────────────────

        /// <summary>
        /// Parse typed arguments from a JSON-style list of dicts.
        /// Each element should be a Dictionary with "type" and "value" keys:
        ///   { "type": "int", "value": "5" }
        ///   { "type": "float", "value": "3.14" }
        ///   { "type": "string", "value": "hello" }
        ///   { "type": "bool", "value": "true" }
        ///   { "type": "enum", "value": "Faction.Index" } (format: "EnumType.Value")
        /// </summary>
        public static object[] ParseTypedArgs(List<object> args)
        {
            if (args == null || args.Count == 0)
                return new object[0];

            var result = new object[args.Count];

            for (int i = 0; i < args.Count; i++)
            {
                var item = args[i];

                if (item is Dictionary<string, object> dict)
                {
                    string typeName = GetDictString(dict, "type");
                    string value = GetDictString(dict, "value");

                    result[i] = ConvertArg(typeName, value);
                }
                else
                {
                    // Pass through raw values as-is
                    result[i] = item;
                }
            }

            return result;
        }

        private static object ConvertArg(string typeName, string value)
        {
            if (string.IsNullOrEmpty(typeName))
                return value;

            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "int32":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                        return iv;
                    return 0;

                case "long":
                case "int64":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv))
                        return lv;
                    return 0L;

                case "float":
                case "single":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                        return fv;
                    return 0f;

                case "double":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                        return dv;
                    return 0d;

                case "bool":
                case "boolean":
                    return value != null &&
                           (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

                case "string":
                    return value ?? "";

                case "enum":
                    // Format: "EnumTypeName.ValueName"
                    if (value != null)
                    {
                        int dotIdx = value.LastIndexOf('.');
                        if (dotIdx > 0)
                        {
                            string enumTypeName = value.Substring(0, dotIdx);
                            string enumValueName = value.Substring(dotIdx + 1);
                            return ParseEnum(enumTypeName, enumValueName);
                        }
                    }
                    return null;

                case "null":
                    return null;

                default:
                    return value;
            }
        }

        private static string GetDictString(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out object val))
                return val != null ? val.ToString() : null;
            return null;
        }
    }
}
