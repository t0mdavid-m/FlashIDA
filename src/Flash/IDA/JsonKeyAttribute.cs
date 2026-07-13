using System;

namespace Flash.IDA
{
    /// <summary>
    /// Specifies the JSON key name for a property, class, or struct field in the user-facing
    /// config file. On struct fields it gives the scan-config (ms_settings) structs an explicit
    /// snake_case JSON key, so load and serialize bind by exact key — no name-normalization heuristic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class JsonKeyAttribute : Attribute
    {
        public string Key { get; }

        public JsonKeyAttribute(string key)
        {
            Key = key;
        }
    }
}
