using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace Flash.IDA
{
    /// <summary>
    /// Reflection utility that reads [Description] and [JsonKey] attributes from
    /// MethodConfig and its nested config classes, formatting them as documentation.
    /// </summary>
    public static class MethodDocGenerator
    {
        public static string Generate(Type type)
        {
            var sb = new StringBuilder();
            GenerateForType(type, sb, "");
            return sb.ToString();
        }

        private static void GenerateForType(Type type, StringBuilder sb, string prefix)
        {
            foreach (PropertyInfo prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var keyAttr = prop.GetCustomAttribute<JsonKeyAttribute>();
                string key = keyAttr != null ? keyAttr.Key : prop.Name;
                string fullKey = string.IsNullOrEmpty(prefix) ? key : prefix + "." + key;

                var descAttr = prop.GetCustomAttribute<DescriptionAttribute>();

                // If the property type has [JsonKey] on the class, recurse into it
                Type propType = prop.PropertyType;
                if (propType.IsClass && propType != typeof(string)
                    && propType.GetCustomAttribute<JsonKeyAttribute>() != null)
                {
                    GenerateForType(propType, sb, fullKey);
                }
                else if (descAttr != null)
                {
                    sb.AppendLine(string.Format("{0}: {1}", fullKey, descAttr.Description));
                }
            }
        }
    }
}
