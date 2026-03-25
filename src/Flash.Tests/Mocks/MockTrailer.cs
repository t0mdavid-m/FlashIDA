using System.Collections.Generic;
// Try all plausible Thermo namespaces for IInformationSourceAccess
using Thermo.Interfaces.InstrumentAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using Thermo.Interfaces.FusionAccess_V1;
using Thermo.Interfaces.FusionAccess_V1.MsScanContainer;
using Thermo.TNG.Client.API;
using Thermo.TNG.Client.API.MsScanContainer;

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
