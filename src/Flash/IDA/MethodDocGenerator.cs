using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace Flash.IDA
{
    /// <summary>
    /// Reflection utility that reads [Description] attributes from properties
    /// and formats them as documentation output.
    /// </summary>
    public static class MethodDocGenerator
    {
        public static string Generate(Type type)
        {
            var sb = new StringBuilder();
            foreach (PropertyInfo prop in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<DescriptionAttribute>();
                if (attr != null)
                    sb.AppendLine($"{prop.Name}: {attr.Description}");
            }
            return sb.ToString();
        }
    }
}
