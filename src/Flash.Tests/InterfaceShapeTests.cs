using NUnit.Framework;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash.Tests
{
    [TestFixture]
    public class InterfaceShapeTests
    {
        [Test]
        [Category("Tier1")]
        public void P5_U02_IScanProcessor_HasExactlyOneMethod()
        {
            var methods = typeof(IScanProcessor).GetMethods();
            Assert.AreEqual(1, methods.Length,
                "IScanProcessor should have exactly 1 method");
            Assert.AreEqual("ProcessMS", methods[0].Name);
            Assert.AreEqual(typeof(void), methods[0].ReturnType);
            var parameters = methods[0].GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(IMsScan), parameters[0].ParameterType);
        }
    }
}
