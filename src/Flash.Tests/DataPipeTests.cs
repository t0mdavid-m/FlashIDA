using System;
using Flash.Tests.Mocks;
using NUnit.Framework;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash.Tests
{
    [TestFixture]
    public class DataPipeTests
    {
        [Test]
        [Category("Tier1")]
        public void P5_U04_DataPipe_PropagatesCompletion()
        {
            int processedCount = 0;
            var mockProcessor = new CountingProcessor(() => processedCount++);
            var pipe = new DataPipe(mockProcessor);

            for (int i = 0; i < 5; i++)
                pipe.Push(MockMsScan.EmptyMS1());

            pipe.Complete();
            bool completed = pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(completed, "DataPipe should complete within 5 seconds");
            Assert.AreEqual(5, processedCount, "All 5 scans should be processed");
        }

        private class CountingProcessor : IScanProcessor
        {
            private Action onProcess;
            public CountingProcessor(Action onProcess) { this.onProcess = onProcess; }
            public void ProcessMS(IMsScan msScan) { onProcess(); }
        }
    }
}
