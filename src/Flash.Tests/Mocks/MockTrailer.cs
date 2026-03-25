using System.Collections.Generic;
using System.Linq;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Dictionary wrapper that serves as both Header and Trailer for MockMsScan.
    ///
    /// CI FIXUP NOTE: This class likely needs to implement a Thermo interface
    /// (e.g. IInfoContainer from Thermo.Interfaces.InstrumentAccess_V1).
    /// If MockMsScan fails to compile because Header/Trailer return types don't match,
    /// add the correct interface here and implement any missing members.
    /// </summary>
    public class MockInfoContainer
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        /// <summary>
        /// String indexer matching IMsScan.Header["key"] usage pattern
        /// </summary>
        public string this[string name]
        {
            get => _data[name]; // Throws KeyNotFoundException for missing keys (matches Thermo behavior)
            set => _data[name] = value;
        }

        /// <summary>
        /// TryGetValue matching IMsScan.Trailer.TryGetValue() usage pattern
        /// </summary>
        public bool TryGetValue(string name, out string value)
        {
            return _data.TryGetValue(name, out value);
        }

        /// <summary>
        /// Enumeration of all key names (potential IInfoContainer member)
        /// </summary>
        public IEnumerable<string> Names => _data.Keys;

        /// <summary>
        /// Count of entries
        /// </summary>
        public int Count => _data.Count;

        /// <summary>
        /// Set a value for test setup
        /// </summary>
        public void Set(string name, string value)
        {
            _data[name] = value;
        }

        /// <summary>
        /// Check if a key exists
        /// </summary>
        public bool ContainsKey(string name)
        {
            return _data.ContainsKey(name);
        }
    }
}
