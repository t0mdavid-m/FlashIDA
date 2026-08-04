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
            var pipe = new DataPipe(mockProcessor, _ => { });

            for (int i = 0; i < 5; i++)
                pipe.Push(MockMsScan.EmptyMS1());

            pipe.Complete();
            bool completed = pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(completed, "DataPipe should complete within 5 seconds");
            Assert.AreEqual(5, processedCount, "All 5 scans should be processed");
        }

        /// <summary>
        /// The pipeline - not the producer - owns the scan, and frees it only AFTER reading it.
        ///
        /// Before the ownership-transfer fix, Flash.ProcessSpectrum disposed the scan on the
        /// instrument event thread immediately after Push, while UnifiedScanProcessor was still
        /// lazily enumerating Centroids/Header/Trailer on the pool thread - a use-after-free of
        /// Thermo shared memory whose window is the entire queue depth.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void DataPipe_DisposesScanAfterProcessing()
        {
            var scan = MockMsScan.EmptyMS1();
            bool disposedDuringProcessing = true;

            var processor = new CountingProcessor(() => disposedDuringProcessing = scan.IsDisposed);
            var pipe = new DataPipe(processor, _ => { });

            pipe.Push(scan);
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            Assert.IsFalse(disposedDuringProcessing, "Scan must NOT be disposed while ProcessMS is reading it");
            Assert.IsTrue(scan.IsDisposed, "Pipeline must dispose the scan once processing is done");
        }

        /// <summary>
        /// A throwing processor must not kill the pipeline, must still release the scan, and must
        /// signal the abort.
        ///
        /// Before the fix the exception escaped the ActionBlock delegate and faulted the block
        /// PERMANENTLY. Nothing observed it (Push discarded Post's result and Completion is only
        /// awaited by tests), so the link was severed, the buffer grew unbounded, and - because
        /// GetNextScanCommand never returns 0 - the instrument ran idle AGC scans for the remainder
        /// of the run while acquiring nothing, with a normal-looking log.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void DataPipe_ProcessorException_DoesNotFaultBlock_AndSignalsAbort()
        {
            int abortSignals = 0;
            var scan1 = MockMsScan.EmptyMS1();
            var scan2 = MockMsScan.EmptyMS1();

            var processor = new ThrowingProcessor();
            var pipe = new DataPipe(processor, _ => abortSignals++);

            pipe.Push(scan1);
            pipe.Push(scan2);   // the second scan proves the block still accepts work after the first throw

            pipe.Complete();
            bool completed = pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(completed, "A processor exception must not fault the block - it should complete cleanly");
            Assert.AreEqual(2, processor.CallCount, "Both scans should reach the processor");
            Assert.AreEqual(2, abortSignals, "Each failure must signal the abort callback");
            Assert.IsTrue(scan1.IsDisposed && scan2.IsDisposed, "Scans must be released even on the throwing path");
        }

        private class CountingProcessor : IScanProcessor
        {
            private Action onProcess;
            public CountingProcessor(Action onProcess) { this.onProcess = onProcess; }
            public void ProcessMS(IMsScan msScan) { onProcess(); }
        }

        private class ThrowingProcessor : IScanProcessor
        {
            public int CallCount { get; private set; }
            public void ProcessMS(IMsScan msScan)
            {
                CallCount++;
                throw new InvalidOperationException("simulated scan processing failure");
            }
        }
    }
}
