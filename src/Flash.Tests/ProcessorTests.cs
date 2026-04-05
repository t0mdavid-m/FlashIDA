using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    [TestFixture]
    public class ProcessorTests
    {
        [Test]
        [Category("Tier1")]
        public void P5_U01_UnifiedScanProcessor_Constructs()
        {
            var processor = new UnifiedScanProcessor(null);
            Assert.IsNotNull(processor);
        }
    }
}
