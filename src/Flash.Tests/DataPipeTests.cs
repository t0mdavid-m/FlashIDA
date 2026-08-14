using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        /// <summary>
        /// A scan that has been queued must still be readable after the iAPI has released its
        /// source handle.
        ///
        /// The pipeline queues the IMsScan HANDLE and the consumer reads Centroids/Header/Trailer
        /// through it at DEQUEUE time (UnifiedScanProcessor.cs:20-25). An IMsScan is a window onto
        /// framework-owned memory that the iAPI releases as soon as the next scan replaces it as the
        /// container's LastScan - so anything still sitting in the queue at that moment is a handle
        /// to memory that may already be gone. Nothing signals it and nothing throws on the producer
        /// side; the scan is simply lost, silently and corruptly.
        ///
        /// Today this never bites because the queue is only ever ~1 deep: the command drain blocks
        /// behind the deconvolution, which couples the instrument's scan rate to the processing
        /// rate. That coupling is accidental, and removing it is exactly what the engine-side lock
        /// split does - so this latent defect becomes reachable the moment the drain stops blocking.
        ///
        /// The fix is for the queue to hold a COPY of the six values the engine needs rather than a
        /// handle. This test is red until it does.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void DataPipe_QueuedScan_SurvivesSourceHandleInvalidation()
        {
            var consumerEntered = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            var captured = new List<CapturedScan>();
            int abortSignals = 0;

            var processor = new CapturingProcessor(consumerEntered, release, captured);
            var pipe = new DataPipe(processor, _ => abortSignals++);

            //The first scan PARKS the consumer, which is what guarantees the second one is sitting
            //in the queue rather than already in flight. Latched, not raced - there is no timing
            //assumption here and nothing for a loaded CI runner to perturb.
            pipe.Push(MockMsScan.EmptyMS1(rt: 1.0, scanNumber: "1"));
            Assert.IsTrue(consumerEntered.Wait(TimeSpan.FromSeconds(5)),
                "The consumer should have picked up the first scan and parked");

            var queued = MockMsScan.WithPeaks(2.0, "2", (700.5, 1234.0), (701.0, 5678.0));
            pipe.Push(queued);

            //The next scan arriving is what releases this one's content on a real instrument. We
            //never get told; the handle just stops being readable.
            queued.Invalidate();

            release.Set();
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            Assert.AreEqual(0, abortSignals,
                "Reading a queued scan must not fail. A non-zero count means the consumer read through "
                + "a released handle and the ActionBlock caught the exception - i.e. the scan was lost.");
            Assert.AreEqual(2, captured.Count, "Both scans must be processed");

            var second = captured[1];
            CollectionAssert.AreEqual(new[] { 700.5, 701.0 }, second.Mzs,
                "The queued scan's peaks must survive its source handle being released");
            CollectionAssert.AreEqual(new[] { 1234.0, 5678.0 }, second.Intensities);
            Assert.AreEqual(2.0, second.Rt, 1e-9);
            Assert.AreEqual(1, second.MsLevel);
            Assert.AreEqual(MockMsScan.Ms1ScanDescription, second.Description);
        }

        private class CapturedScan
        {
            public double[] Mzs;
            public double[] Intensities;
            public double Rt;
            public int MsLevel;
            public string Description;
        }

        /// <summary>
        /// Reads exactly the six values UnifiedScanProcessor reads, in the same order, so this test
        /// reproduces the production access pattern rather than a convenient approximation of it.
        /// Parks on the first scan so a later one can be observed sitting in the queue.
        /// </summary>
        private class CapturingProcessor : IScanProcessor
        {
            private readonly ManualResetEventSlim entered;
            private readonly ManualResetEventSlim release;
            private readonly List<CapturedScan> captured;
            private bool parked;

            public CapturingProcessor(ManualResetEventSlim entered, ManualResetEventSlim release,
                List<CapturedScan> captured)
            {
                this.entered = entered;
                this.release = release;
                this.captured = captured;
            }

            public void ProcessMS(IMsScan msScan)
            {
                if (!parked)
                {
                    parked = true;
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }

                string desc;
                msScan.Trailer.TryGetValue("Scan Description", out desc);
                captured.Add(new CapturedScan
                {
                    Mzs = msScan.Centroids.Select(c => c.Mz).ToArray(),
                    Intensities = msScan.Centroids.Select(c => c.Intensity).ToArray(),
                    Rt = double.Parse(msScan.Header["StartTime"]),
                    MsLevel = int.Parse(msScan.Header["MSOrder"]),
                    Description = desc
                });
            }
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
