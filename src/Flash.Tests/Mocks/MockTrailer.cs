using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.SpectrumFormat_V1;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IInformationSourceAccess (from SpectrumFormat_V1)
    /// for IMsScan.Trailer, IMsScan.TuneData, and IMsScan.StatusLog.
    /// </summary>
    public class MockTrailerAccess : IInformationSourceAccess
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        // Used by Flash code (IMsScan.Trailer.TryGetValue)
        public bool TryGetValue(string name, out string value)
        {
            return _data.TryGetValue(name, out value);
        }

        // IInformationSourceAccess members
        public bool TryGetRawValue(string name, out object value)
        {
            if (_data.TryGetValue(name, out string strValue))
            {
                value = strValue;
                return true;
            }
            value = null;
            return false;
        }

        public IEnumerable<string> ItemNames => _data.Keys;

        public bool Available => true;

        public bool Valid => true;

        // Test setup helper
        public void Set(string name, string value)
        {
            _data[name] = value;
        }
    }
}
