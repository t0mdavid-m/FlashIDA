using System;

namespace Flash.IDA
{
    /// <summary>
    /// Marks a config property as a developer/advanced setting.
    /// Developer-tagged properties are serialized into a separate "developer"
    /// section in the user-facing JSON config file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DeveloperAttribute : Attribute
    {
    }
}
