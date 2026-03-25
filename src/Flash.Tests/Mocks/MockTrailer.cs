using System.Collections.Generic;
using Thermo.Interfaces.SpectrumFormat_V1;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IInformationSourceAccess for IMsScan.Trailer.
    /// </summary>
    public class MockTrailerAccess : IInformationSourceAccess
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        public bool TryGetValue(string name, out string value)
        {
            return _data.TryGetValue(name, out value);
        }

        public void Set(string name, string value)
        {
            _data[name] = value;
        }
    }
}
