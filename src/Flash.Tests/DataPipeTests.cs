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
        /// NOBODY disposes the scan - not the producer, not the pipeline.
        ///
        /// An IMsScan is a handle to FRAMEWORK-owned shared memory that the iAPI releases by itself
        /// once the next scan replaces it as the container's LastScan (dependencies/API-2.0.xml,
        /// IMsScan). Both halves of the lifetime dispute that produced this test were wrong:
        /// disposing on the producer side freed memory the pool thread was still lazily enumerating,
        /// and disposing on the pool side instead - late, out of arrival order, from a thread that
        /// never acquired the handle - threw out of a `finally` that sat OUTSIDE the delegate's
        /// try/catch. That faulted the ActionBlock permanently on the very first scan of a real run,
        /// invisibly (Post keeps returning true, Completion is observed by nobody, and
        /// GetNextScanCommand never returns 0), so the instrument ran fabricated idle AGC for the
        /// rest of the gradient while the engine saw nothing at all.
        ///
        /// This test fails the moment anyone reintroduces a Dispose in the pipeline.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void DataPipe_DoesNotDisposeScan()
        {
            var scan = MockMsScan.EmptyMS1();
            int processedCount = 0;
            bool disposedDuringProcessing = true;

            var processor = new CountingProcessor(() =>
            {
                processedCount++;
                disposedDuringProcessing = scan.IsDisposed;
            });
            var pipe = new DataPipe(processor, _ => { });

            pipe.Push(scan);
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            Assert.AreEqual(1, processedCount, "The scan must still be processed");
            Assert.IsFalse(disposedDuringProcessing, "Scan must NOT be disposed while ProcessMS is reading it");
            Assert.IsFalse(scan.IsDisposed, "The pipeline must NOT dispose the scan - the iAPI owns its lifetime");
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
            Assert.IsFalse(scan1.IsDisposed || scan2.IsDisposed,
                "The throwing path must not dispose either - that is where the old `finally` fired");
        }

        /// <summary>
        /// A throwing onFailure - or a dead logger - must not fault the block either.
        ///
        /// The abort callback runs INSIDE the ActionBlock delegate, so without its own guard an
        /// exception from it escapes exactly like the one it is reporting, and permanently kills
        /// acquisition while in the act of reporting that acquisition has a problem.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void DataPipe_OnFailureThrows_DoesNotFaultBlock()
        {
            var processor = new ThrowingProcessor();
            var pipe = new DataPipe(processor,
                _ => { throw new InvalidOperationException("abort handler exploded"); });

            pipe.Push(MockMsScan.EmptyMS1());
            pipe.Push(MockMsScan.EmptyMS1());   // the second scan proves the block survived the first

            pipe.Complete();

            //AsyncWaitHandle rather than Task.Wait: Wait() THROWS on a faulted task, which would
            //report this as an error rather than as the assertion failure it is.
            var completion = pipe.WaitForCompletion();
            bool finished = ((IAsyncResult)completion).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

            Assert.IsTrue(finished, "DataPipe should finish within 5 seconds");
            Assert.IsFalse(completion.IsFaulted,
                "A throwing onFailure must NOT fault the block: " + completion.Exception);
            Assert.AreEqual(2, processor.CallCount, "Both scans must reach the processor");
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
