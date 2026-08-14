using NUnit.Framework;

namespace Flash.Tests
{
    [TestFixture]
    public class InterfaceShapeTests
    {
        /// <summary>
        /// IScanProcessor stays a one-method interface taking an owned snapshot.
        /// </summary>
        /// <remarks>
        /// The parameter type is the part worth pinning. It used to be IMsScan, which meant the
        /// consumer - running on a pool thread, arbitrarily long after the scan arrived - was reading
        /// through a handle to framework-owned memory the iAPI may already have released. Taking a
        /// ScanData instead is what makes a deep queue safe, and this assertion is what makes going
        /// back a deliberate act rather than a plausible-looking refactor.
        /// </remarks>
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
            Assert.AreEqual(typeof(ScanData), parameters[0].ParameterType,
                "ProcessMS must take an owned snapshot, never a live IMsScan handle");
        }
    }
}
