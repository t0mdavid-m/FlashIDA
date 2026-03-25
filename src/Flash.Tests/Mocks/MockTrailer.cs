using System.Collections.Generic;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using Thermo.TNG.Client.API.MsScanContainer;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IInformationSourceAccess for IMsScan.Trailer.
    /// IInformationSourceAccess is the return type of IMsScan.Trailer
    /// (discovered from CI error CS0738).
    ///
    /// CI FIXUP NOTE: If IInformationSourceAccess is not in
    /// Thermo.Interfaces.InstrumentAccess_V1, or has additional members
    /// beyond TryGetValue, compilation errors will indicate what to fix.
    /// </summary>
    public class MockTrailerAccess : IInformationSourceAccess
    {
        private readonly Dictionary<string, string> _data = new Dictionary<string, string>();

        /// <summary>
        /// TryGetValue matching IMsScan.Trailer.TryGetValue() usage pattern
        /// </summary>
        public bool TryGetValue(string name, out string value)
        {
            return _data.TryGetValue(name, out value);
        }

        /// <summary>
        /// Set a value for test setup
        /// </summary>
        public void Set(string name, string value)
        {
            _data[name] = value;
        }
    }
}
