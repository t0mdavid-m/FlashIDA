using System;

namespace Flash.IDA
{
    /// <summary>
    /// Specifies the JSON key name for a property in the user-facing config file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class JsonKeyAttribute : Attribute
    {
        public string Key { get; }

        public JsonKeyAttribute(string key)
        {
            Key = key;
        }
    }
}
