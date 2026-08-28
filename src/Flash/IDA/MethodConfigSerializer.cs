using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;
using Flash.IDA;

namespace Flash
{
    /// <summary>
    /// Serializes and deserializes <see cref="MethodConfig"/> to/from user-facing JSON.
    /// Properties marked with <see cref="DeveloperAttribute"/> are routed to/from a
    /// top-level "developer" section in the JSON, keyed by their containing class's
    /// <see cref="JsonKeyAttribute"/>.
    /// </summary>
    public static class MethodConfigSerializer
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        /// <summary>
        /// Deserialize a JSON string into a <see cref="MethodConfig"/>.
        /// </summary>
        public static MethodConfig Deserialize(string json)
        {
            var raw = Serializer.Deserialize<Dictionary<string, object>>(json);

            // Strict schema: reject any key with no home in the model (mistyped, PascalCase,
            // legacy 'developer', dropped 'IsolationMode', …) before populating.
            ValidateNoUnknownKeys(raw);

            var config = new MethodConfig();

            // Get the developer section (if present)
            Dictionary<string, object> devSection = null;
            object devObj;
            if (raw.TryGetValue("developer", out devObj))
                devSection = devObj as Dictionary<string, object>;

            // Iterate over each property on MethodConfig (e.g., Global, Deconvolution, ...)
            foreach (PropertyInfo rootProp in typeof(MethodConfig).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = rootProp.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;

                string sectionKey = keyAttr.Key;
                Type sectionType = rootProp.PropertyType;

                // Get or create the section object
                object sectionObj = rootProp.GetValue(config);
                if (sectionObj == null)
                {
                    sectionObj = Activator.CreateInstance(sectionType);
                    rootProp.SetValue(config, sectionObj);
                }

                // Get the raw JSON dictionary for this section
                Dictionary<string, object> sectionDict = null;
                object rawSection;
                if (raw.TryGetValue(sectionKey, out rawSection))
                    sectionDict = rawSection as Dictionary<string, object>;

                // Get the developer sub-section for this class
                Dictionary<string, object> devSubSection = null;
                string classSectionKey = GetClassJsonKey(sectionType);
                if (devSection != null && classSectionKey != null)
                {
                    object devSub;
                    if (devSection.TryGetValue(classSectionKey, out devSub))
                        devSubSection = devSub as Dictionary<string, object>;
                }

                // Populate the section object
                PopulateObject(sectionObj, sectionType, sectionDict, devSubSection);

                rootProp.SetValue(config, sectionObj);
            }

            // conditional_ms2 lives at the TOP level of the bridge schema (not under a section);
            // map it onto Tagging.ConditionalMS2 (fall back to a nested tagging.conditional_ms2).
            object condObj;
            if (raw.TryGetValue("conditional_ms2", out condObj) && condObj != null)
                config.Tagging.ConditionalMS2 = Convert.ToBoolean(condObj);

            return config;
        }

        /// <summary>
        /// Serialize a <see cref="MethodConfig"/> to a JSON string.
        /// </summary>
        public static string Serialize(MethodConfig config)
        {
            var root = new Dictionary<string, object>();
            var developer = new Dictionary<string, object>();

            foreach (PropertyInfo rootProp in typeof(MethodConfig).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = rootProp.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;

                string sectionKey = keyAttr.Key;
                object sectionObj = rootProp.GetValue(config);
                if (sectionObj == null)
                    continue;

                Type sectionType = rootProp.PropertyType;
                string classSectionKey = GetClassJsonKey(sectionType);

                var mainDict = new Dictionary<string, object>();
                var devDict = new Dictionary<string, object>();

                SerializeObject(sectionObj, sectionType, mainDict, devDict);

                if (mainDict.Count > 0)
                    root[sectionKey] = mainDict;

                if (devDict.Count > 0 && classSectionKey != null)
                    developer[classSectionKey] = devDict;
            }

            if (developer.Count > 0)
                root["developer"] = developer;

            return Serializer.Serialize(root);
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Get the class-level [JsonKey] value for a type.
        /// </summary>
        private static string GetClassJsonKey(Type type)
        {
            var attr = type.GetCustomAttribute<JsonKeyAttribute>();
            return attr != null ? attr.Key : null;
        }

        // ----------------------------------------------------------------
        // Strict schema validation — reject unknown keys
        // ----------------------------------------------------------------

        /// <summary>
        /// Reject any key that has no home in the model tree. Walks the raw JSON against the
        /// <see cref="MethodConfig"/> shape and throws (naming every offending dotted path) if a
        /// key matches no [JsonKey] property/field. Keys are case-sensitive snake_case. The
        /// dynamic <c>selection_strategy.*.exploration.overrides</c> dictionary is exempt.
        /// </summary>
        private static void ValidateNoUnknownKeys(Dictionary<string, object> raw)
        {
            var problems = new List<string>();
            var topAllowed = BuildAllowedKeyMap(typeof(MethodConfig));

            foreach (var kv in raw)
            {
                if (kv.Key == "conditional_ms2")   // top-level bool handled specially by Deserialize
                    continue;

                Type memberType;
                if (!topAllowed.TryGetValue(kv.Key, out memberType))
                {
                    problems.Add(kv.Key);
                    continue;
                }
                CollectUnknownKeys(kv.Value, memberType, kv.Key, problems);
            }

            if (problems.Count > 0)
            {
                string message =
                    "Unknown config key(s) not in the FLASHIda schema: " + string.Join(", ", problems)
                    + ". Keys are case-sensitive snake_case; see FlashIDA/test-data/config_schema_reference.json.";
                foreach (string p in problems)
                {
                    string hint;
                    if (RetiredKeyHints.TryGetValue(p, out hint))
                        message += Environment.NewLine + "  " + hint;
                }
                throw new ArgumentException(message);
            }
        }

        /// <summary>
        /// Retired keys that earn a specific message on top of the bare unknown-key one.
        /// <para>
        /// This lives on the C# side because C# validates <c>method.json</c> FIRST: a user who still
        /// has a retired key never reaches the C++ loader, so a migration message that exists only in
        /// <c>Config.cpp</c> is unreachable from the normal path.
        /// </para>
        /// <para>
        /// Worth the mechanism because "unknown key" invites deleting the key, which is the wrong fix
        /// whenever the key was doing something the reader still wants.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> RetiredKeyHints =
            new Dictionary<string, string>
            {
                {
                    "precursor_selection.charge_based_exclusion",
                    "precursor_selection.charge_based_exclusion was removed (ADR-0021). It keyed exclusion "
                    + "per (mass, charge), and as a side effect it was the only thing that made "
                    + "precursor_charges: \"separate\" fan out. To acquire several charge states of one "
                    + "species, ask for it directly: precursor_charges: \"separate\" (one MS2 per charge "
                    + "state) or \"multiplexed\" (one MS2 co-isolating them). Exclusion is now always "
                    + "mass-keyed; re-selecting one mass at a different charge on a LATER survey has no "
                    + "replacement."
                },
                {
                    "quantification.follow_up_scan",
                    "quantification.follow_up_scan was removed (ADR-0038). It named the scan a "
                    + "differential verdict BOUGHT -- which the engine then never measured -- while the "
                    + "scan it DID measure was the base MS2, whose activation could not release the "
                    + "reporter ion. The two roles are now explicit slots: ms_settings.ms2_quant is the "
                    + "quantification scan (rostered once per precursor, and the only scan measured), "
                    + "and ms_settings.ms2 is the identification scan a differential verdict buys. Move "
                    + "the block you referenced here into ms_settings.ms2_quant."
                },
                {
                    "quantification.only_one_condition",
                    "quantification.only_one_condition was removed (ADR-0038). It was never reachable -- "
                    + "no emit DTO ever carried it, so the C++ branch behind it could not run -- and its "
                    + "intent is now unconditional: a condition whose channels are ALL empty reports "
                    + "\"differential\", because a species present in one condition and absent in the "
                    + "other is the strongest result the experiment can produce. Delete the key."
                },
                {
                    // Renamed in FlashIDA 79caf4b and landed without a hint, so a config older than
                    // that got a bare "unknown key" with nothing pointing at the replacement.
                    "quantification.active",
                    "quantification.active was renamed to quantification.enabled."
                },
            };

        /// <summary>Recurse a raw JSON node against its model type, collecting unknown keys.</summary>
        private static void CollectUnknownKeys(object rawNode, Type modelType, string path, List<string> problems)
        {
            modelType = Nullable.GetUnderlyingType(modelType) ?? modelType;

            // Arrays / lists: validate each element against the element type.
            var list = rawNode as ArrayList;
            if (list != null)
            {
                Type elemType = GetElementType(modelType);
                if (elemType != null)
                    for (int i = 0; i < list.Count; i++)
                        CollectUnknownKeys(list[i], elemType, path + "[" + i + "]", problems);
                return;
            }

            var dict = rawNode as Dictionary<string, object>;
            if (dict == null)
                return;   // primitive leaf

            // Dictionaries have user-authored KEYS, so the keys cannot be allowlisted -- but their
            // VALUES still can be, and must be. Returning outright here (the old behaviour) is right
            // for exploration.overrides, whose values are plain strings, and wrong for
            // ms_settings.additional_ms2, whose values are full 17-key scan objects: it would let
            // `{"etd": {"IsolationMode": "Quad"}}` load clean and then silently drop the key.
            //
            // So: keys stay free, values recurse. A Dictionary<string,string> recursion bottoms out
            // immediately at the "primitive leaf" return above, which reproduces the old behaviour
            // for overrides exactly.
            if (modelType.IsGenericType && modelType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type valueType = modelType.GetGenericArguments()[1];
                foreach (var entry in dict)
                    CollectUnknownKeys(entry.Value, valueType, path + "." + entry.Key, problems);
                return;
            }

            var allowed = BuildAllowedKeyMap(modelType);
            foreach (var kv in dict)
            {
                string childPath = path + "." + kv.Key;
                Type memberType;
                if (!allowed.TryGetValue(kv.Key, out memberType))
                {
                    problems.Add(childPath);
                    continue;
                }
                CollectUnknownKeys(kv.Value, memberType, childPath, problems);
            }
        }

        /// <summary>Map a model type's allowed JSON keys to their member types: struct [JsonKey]
        /// fields, or class [JsonKey] properties.</summary>
        private static Dictionary<string, Type> BuildAllowedKeyMap(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var map = new Dictionary<string, Type>();

            bool isStruct = type.IsValueType && !type.IsPrimitive && !type.IsEnum
                && type != typeof(decimal) && type != typeof(double)
                && type != typeof(int) && type != typeof(bool);

            if (isStruct)
            {
                foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var a = f.GetCustomAttribute<JsonKeyAttribute>();
                    if (a != null) map[a.Key] = f.FieldType;
                }
            }
            else
            {
                foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var a = p.GetCustomAttribute<JsonKeyAttribute>();
                    if (a != null) map[a.Key] = p.PropertyType;
                }
            }
            return map;
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return type.GetGenericArguments()[0];
            return null;
        }

        /// <summary>
        /// Populate an object's properties from JSON dictionaries, routing
        /// [Developer] properties from the devDict and normal properties from mainDict.
        /// </summary>
        private static void PopulateObject(
            object target, Type targetType,
            Dictionary<string, object> mainDict,
            Dictionary<string, object> devDict)
        {
            foreach (PropertyInfo prop in targetType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = prop.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;

                string jsonKey = keyAttr.Key;
                bool isDeveloper = prop.GetCustomAttribute<DeveloperAttribute>() != null;

                // Pick the source dictionary
                Dictionary<string, object> source = isDeveloper ? devDict : mainDict;
                if (source == null)
                    continue;

                object rawValue;
                if (!source.TryGetValue(jsonKey, out rawValue))
                    continue;

                if (rawValue == null)
                {
                    if (!prop.PropertyType.IsValueType)
                        prop.SetValue(target, null);
                    continue;
                }

                object converted = ConvertValue(rawValue, prop.PropertyType);
                prop.SetValue(target, converted);
            }
        }

        /// <summary>
        /// Convert a raw deserialized value to the target CLR type.
        /// Handles primitives, arrays, lists, structs (field-based), and nested config objects.
        /// </summary>
        private static object ConvertValue(object rawValue, Type targetType)
        {
            if (rawValue == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            // Nullable<T> — unwrap to underlying type T
            Type nullableUnderlying = Nullable.GetUnderlyingType(targetType);
            if (nullableUnderlying != null)
                return ConvertValue(rawValue, nullableUnderlying);

            // double[] — from ArrayList
            if (targetType == typeof(double[]))
            {
                var list = rawValue as ArrayList;
                if (list != null)
                {
                    var arr = new double[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        arr[i] = Convert.ToDouble(list[i]);
                    return arr;
                }
                return new double[0];
            }

            // List<string> — from ArrayList
            if (targetType == typeof(List<string>))
            {
                var list = rawValue as ArrayList;
                if (list != null)
                {
                    var result = new List<string>(list.Count);
                    foreach (object item in list)
                        result.Add(item != null ? item.ToString() : "");
                    return result;
                }
                return new List<string>();
            }

            // List<MS2Parameters> — from ArrayList of Dictionaries
            if (targetType == typeof(List<MS2Parameters>))
            {
                var list = rawValue as ArrayList;
                if (list != null)
                {
                    var result = new List<MS2Parameters>(list.Count);
                    foreach (object item in list)
                    {
                        var dict = item as Dictionary<string, object>;
                        if (dict != null)
                            result.Add((MS2Parameters)PopulateStruct(typeof(MS2Parameters), dict));
                    }
                    return result;
                }
                return new List<MS2Parameters>();
            }

            // List<MS3Parameters> — from ArrayList of Dictionaries
            if (targetType == typeof(List<MS3Parameters>))
            {
                var list = rawValue as ArrayList;
                if (list != null)
                {
                    var result = new List<MS3Parameters>(list.Count);
                    foreach (object item in list)
                    {
                        var dict = item as Dictionary<string, object>;
                        if (dict != null)
                            result.Add((MS3Parameters)PopulateStruct(typeof(MS3Parameters), dict));
                    }
                    return result;
                }
                return new List<MS3Parameters>();
            }

            // Value-type structs with public fields (MS1Parameters, etc.)
            if (targetType.IsValueType && !targetType.IsPrimitive && !targetType.IsEnum
                && targetType != typeof(decimal) && targetType != typeof(double)
                && targetType != typeof(int) && targetType != typeof(bool))
            {
                var dict = rawValue as Dictionary<string, object>;
                if (dict != null)
                    return PopulateStruct(targetType, dict);
                return Activator.CreateInstance(targetType);
            }

            if (targetType.IsGenericType &&
                targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] args = targetType.GetGenericArguments();
                Type keyType = args[0];
                Type valueType = args[1];

                // JavaScriptSerializer JSON object -> Dictionary<string, object>
                var rawDict = rawValue as Dictionary<string, object>;
                if (rawDict == null)
                    return Activator.CreateInstance(targetType);

                var result = (IDictionary)Activator.CreateInstance(targetType);
                foreach (var kv in rawDict)
                {
                    object key = keyType == typeof(string) ? kv.Key : ConvertValue(kv.Key, keyType);
                    object val = ConvertValue(kv.Value, valueType);
                    result.Add(key, val);
                }
                return result;
            }

            // Nested config class (has [JsonKey] on the class)
            if (targetType.IsClass && !targetType.IsPrimitive
                && targetType != typeof(string)
                && targetType.GetCustomAttribute<JsonKeyAttribute>() != null)
            {
                var dict = rawValue as Dictionary<string, object>;
                if (dict != null)
                {
                    object nested = Activator.CreateInstance(targetType);
                    PopulateObject(nested, targetType, dict, null);
                    return nested;
                }
                return Activator.CreateInstance(targetType);
            }

            // Primitive conversions
            if (targetType == typeof(double))
                return Convert.ToDouble(rawValue);
            if (targetType == typeof(int))
                return Convert.ToInt32(rawValue);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(rawValue);
            if (targetType == typeof(string))
                return rawValue.ToString();

            return Convert.ChangeType(rawValue, targetType);
        }

        /// <summary>
        /// Populate a struct's public fields from a JSON dictionary, matching each field to its
        /// explicit <see cref="JsonKeyAttribute"/> snake_case key (exact match only). Fields with no
        /// [JsonKey] are ignored; absent keys leave the field at its default (missing keys are allowed).
        /// </summary>
        private static object PopulateStruct(Type structType, Dictionary<string, object> dict)
        {
            object boxed = Activator.CreateInstance(structType);

            foreach (FieldInfo field in structType.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = field.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;

                object rawValue;
                if (!dict.TryGetValue(keyAttr.Key, out rawValue))
                    continue;

                if (rawValue == null)
                    continue;

                if (field.FieldType == typeof(string))
                    field.SetValue(boxed, rawValue.ToString());
                else if (field.FieldType == typeof(double))
                    field.SetValue(boxed, Convert.ToDouble(rawValue));
                else if (field.FieldType == typeof(int))
                    field.SetValue(boxed, Convert.ToInt32(rawValue));
                else if (field.FieldType == typeof(bool))
                    field.SetValue(boxed, Convert.ToBoolean(rawValue));
                else
                    field.SetValue(boxed, Convert.ChangeType(rawValue, field.FieldType));
            }

            return boxed;
        }

        /// <summary>
        /// Serialize an object's properties into mainDict (normal) and devDict ([Developer]),
        /// using [JsonKey] for key names.
        /// </summary>
        private static void SerializeObject(
            object source, Type sourceType,
            Dictionary<string, object> mainDict,
            Dictionary<string, object> devDict)
        {
            foreach (PropertyInfo prop in sourceType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = prop.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;

                string jsonKey = keyAttr.Key;
                bool isDeveloper = prop.GetCustomAttribute<DeveloperAttribute>() != null;
                object value = prop.GetValue(source);

                object serialized = SerializeValue(value, prop.PropertyType);
                if (serialized == null)
                    continue;

                if (isDeveloper)
                    devDict[jsonKey] = serialized;
                else
                    mainDict[jsonKey] = serialized;
            }
        }

        /// <summary>
        /// Convert a CLR value to a serialization-friendly form (dictionaries, arrays, primitives).
        /// </summary>
        private static object SerializeValue(object value, Type valueType)
        {
            if (value == null)
                return null;

            // Nullable<T> — unwrap to underlying type T
            Type nullableUnderlying = Nullable.GetUnderlyingType(valueType);
            if (nullableUnderlying != null)
                return SerializeValue(value, nullableUnderlying);

            // Primitives and string pass through
            if (valueType == typeof(string) || valueType == typeof(double)
                || valueType == typeof(int) || valueType == typeof(bool))
                return value;

            // double[] → array
            if (valueType == typeof(double[]))
                return value;

            // List<string> → string[]
            if (valueType == typeof(List<string>))
                return ((List<string>)value).ToArray();

            // Dictionary<string, T> -> name-keyed object. Needed for ms_settings.additional_ms2.
            // Without this branch a Dictionary falls through to `return value` at the bottom and
            // JavaScriptSerializer reflects the raw CLR struct, emitting PascalCase FIELD names
            // instead of the [JsonKey] wire names -- a config C++ would hard-reject.
            if (valueType.IsGenericType
                && valueType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && valueType.GetGenericArguments()[0] == typeof(string))
            {
                Type vt = valueType.GetGenericArguments()[1];
                var outDict = new Dictionary<string, object>();
                foreach (DictionaryEntry e in (IDictionary)value)
                    outDict[(string)e.Key] = SerializeValue(e.Value, vt);
                return outDict;
            }

            // Value-type structs (MS1Parameters, etc.)
            if (valueType.IsValueType && !valueType.IsPrimitive && !valueType.IsEnum
                && valueType != typeof(decimal) && valueType != typeof(double)
                && valueType != typeof(int) && valueType != typeof(bool))
                return SerializeStruct(value, valueType);

            // Nested config class with [JsonKey]
            if (valueType.IsClass && valueType != typeof(string)
                && valueType.GetCustomAttribute<JsonKeyAttribute>() != null)
            {
                var dict = new Dictionary<string, object>();
                var devDummy = new Dictionary<string, object>();
                SerializeObject(value, valueType, dict, devDummy);
                // Merge any developer properties into the dict for nested objects
                // (developer routing only applies at the top level)
                foreach (var kvp in devDummy)
                    dict[kvp.Key] = kvp.Value;
                return dict;
            }

            return value;
        }

        /// <summary>
        /// Serialize a struct's public fields to a dictionary.
        /// </summary>
        private static Dictionary<string, object> SerializeStruct(object value, Type structType)
        {
            var dict = new Dictionary<string, object>();
            foreach (FieldInfo field in structType.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = field.GetCustomAttribute<JsonKeyAttribute>();
                if (keyAttr == null)
                    continue;
                dict[keyAttr.Key] = field.GetValue(value);
            }
            return dict;
        }
    }
}
