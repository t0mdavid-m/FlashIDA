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
        /// Normalize a JSON key or struct field name for tolerant matching: strip underscores and
        /// lowercase, so bridge snake_case (first_mass) binds onto PascalCase Thermo fields
        /// (FirstMass). Also aliases the bridge "resolution" key onto the OrbitrapResolution field.
        /// </summary>
        private static string NormalizeFieldName(string name)
        {
            string n = name.Replace("_", "").ToLowerInvariant();
            if (n == "resolution")
                n = "orbitrapresolution";
            return n;
        }

        /// <summary>
        /// Populate a struct's public fields from a JSON dictionary, matching by field name
        /// (exact, then case-insensitive, then normalized snake_case).
        /// </summary>
        private static object PopulateStruct(Type structType, Dictionary<string, object> dict)
        {
            object boxed = Activator.CreateInstance(structType);

            foreach (FieldInfo field in structType.GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                // Try exact match, then case-insensitive, then normalized (strip underscores) so the
                // bridge snake_case ms_settings keys (first_mass) bind onto the PascalCase Thermo
                // struct fields (FirstMass); NormalizeFieldName also aliases "resolution"->OrbitrapResolution.
                object rawValue;
                if (!dict.TryGetValue(field.Name, out rawValue))
                {
                    bool found = false;
                    string fieldNorm = NormalizeFieldName(field.Name);
                    foreach (var kvp in dict)
                    {
                        if (string.Equals(kvp.Key, field.Name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(NormalizeFieldName(kvp.Key), fieldNorm, StringComparison.Ordinal))
                        {
                            rawValue = kvp.Value;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        continue;
                }

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

            // List<MS2Parameters> → array of dictionaries
            if (valueType == typeof(List<MS2Parameters>))
            {
                var list = (List<MS2Parameters>)value;
                var arr = new Dictionary<string, object>[list.Count];
                for (int i = 0; i < list.Count; i++)
                    arr[i] = SerializeStruct(list[i], typeof(MS2Parameters));
                return arr;
            }

            // List<MS3Parameters> → array of dictionaries
            if (valueType == typeof(List<MS3Parameters>))
            {
                var list = (List<MS3Parameters>)value;
                var arr = new Dictionary<string, object>[list.Count];
                for (int i = 0; i < list.Count; i++)
                    arr[i] = SerializeStruct(list[i], typeof(MS3Parameters));
                return arr;
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
                dict[field.Name] = field.GetValue(value);
            }
            return dict;
        }
    }
}
