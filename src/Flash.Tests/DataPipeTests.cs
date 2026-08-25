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
        ///
        /// It guards a NEWER temptation too. Push now snapshots the scan into a ScanData before
        /// queueing it, which makes "we have copied everything we need, so release the handle" look
        /// reasonable. It is not: the iAPI owns that lifetime and frees the content itself. The
        /// consumer no longer touches the handle at all, so the only remaining way for this
        /// assertion to fail is someone disposing on the producer side - the exact half of the old
        /// lifetime dispute that freed memory the pool thread was still reading.
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
            var captured = new List<ScanData>();
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
            Assert.AreEqual(2.0, second.RetentionTime, 1e-9);
            Assert.AreEqual(1, second.MsLevel);
            Assert.AreEqual(MockMsScan.Ms1ScanDescription, second.ScanDescription);
        }

        /// <summary>
        /// An AGC prescan still reaches the engine, but WITHOUT its peaks.
        ///
        /// Two halves, and both are the point. The engine identifies a prescan by the 4th character
        /// of the description and returns before touching the spectrum
        /// (FLASHIda.cpp:92-97) - so carrying the peaks there is pure waste. But it calls
        /// resolvePending() on the way out, and that is the ONLY eraser of the pending-map entry,
        /// reachable only from processScan - so a prescan that never arrives leaks its entry.
        /// Skip the payload, keep the delivery.
        ///
        /// The waste is not theoretical: prescans are IonTrap/Profile, so Centroids is ~12 000
        /// points (measured on the 2026-08-25 Eclipse run), enumerated across the iAPI boundary on
        /// the instrument event thread, ahead of the command drain, once a second at the production
        /// default of agc_interval_seconds: 1.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void ScanData_SkipsCentroidsForAgcDescription()
        {
            var peaks = new (double mz, double intensity)[12000];
            for (int i = 0; i < peaks.Length; i++) peaks[i] = (500.0 + i * 0.1, 100.0 + i);

            //"!!\"A" is a REAL engine id ("!!\"" decodes to 1) with the 'A' suffix makeAGC stamps.
            var scan = MockMsScan.WithPeaks(11.21, "9246", "!!\"A", peaks);

            int abortSignals = 0;
            var captured = new List<ScanData>();
            var pipe = new DataPipe(new RecordingProcessor(captured), _ => abortSignals++);

            Assert.IsTrue(pipe.Push(scan), "The prescan must still be accepted by the pipeline");
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            Assert.AreEqual(0, abortSignals, "Skipping the payload must not look like a failure");
            Assert.AreEqual(1, captured.Count,
                "The prescan must STILL reach the engine - resolvePending is reached only from processScan, "
                + "so dropping it here leaks the pending-map entry");

            var got = captured[0];
            Assert.AreEqual(0, got.Mzs.Length,
                "12 000 peaks the engine discards unread must not be enumerated on the event thread");
            Assert.AreEqual(0, got.Intensities.Length, "Both arrays, or neither");
            Assert.AreEqual("!!\"A", got.ScanDescription,
                "The identity token must survive - it is what resolvePending keys on");
            Assert.AreEqual(1, got.MsLevel, "Everything except the payload is unchanged");
            Assert.AreEqual(11.21, got.RetentionTime, 1e-9, "Everything except the payload is unchanged");
        }

        /// <summary>
        /// A real scan keeps every peak. Guards the prescan predicate against over-matching: an
        /// engine-minted MS1 description is "&lt;3-char id&gt;S", one character away from the "A"
        /// this skips on, and silently emptying a survey would deconvolve nothing for a whole run.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void ScanData_KeepsCentroidsForNonAgcDescription()
        {
            var scan = MockMsScan.WithPeaks(11.22, "9247", "!!#S",
                (700.5, 1234.0), (701.0, 5678.0), (702.5, 91.0), (703.0, 42.0), (704.5, 7.0));

            int abortSignals = 0;
            var captured = new List<ScanData>();
            var pipe = new DataPipe(new RecordingProcessor(captured), _ => abortSignals++);

            pipe.Push(scan);
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            //asserted HERE, not inside onFailure: that callback runs on the pool thread inside
            //DataPipe's swallowing guard, so an Assert.Fail there could never fail the test.
            Assert.AreEqual(0, abortSignals, "Reading this scan must not fail");
            Assert.AreEqual(1, captured.Count);
            CollectionAssert.AreEqual(new[] { 700.5, 701.0, 702.5, 703.0, 704.5 }, captured[0].Mzs,
                "An 'S'-suffixed survey must keep its peaks");
            CollectionAssert.AreEqual(new[] { 1234.0, 5678.0, 91.0, 42.0, 7.0 }, captured[0].Intensities);
        }

        /// <summary>
        /// Every way of failing to read the description keeps the peaks.
        ///
        /// This pins the FAIL-OPEN direction, which is the whole safety argument for the skip: a
        /// null, empty or too-short description must degrade to the old behaviour (enumerate
        /// everything), never to "assume prescan and discard the spectrum". The same reasoning
        /// ADR-0032 applies to the answer half - a misread must cost throughput, never data.
        /// </summary>
        [Category("Tier1")]
        [TestCase(null, TestName = "ScanData_KeepsCentroids_WhenDescriptionMissing")]
        [TestCase("", TestName = "ScanData_KeepsCentroids_WhenDescriptionEmpty")]
        [TestCase("ab", TestName = "ScanData_KeepsCentroids_WhenDescriptionTooShort")]
        [TestCase("!!#", TestName = "ScanData_KeepsCentroids_WhenDescriptionHasNoSuffix")]
        [TestCase("!!#A!", TestName = "ScanData_KeepsCentroids_WhenSuffixIsNotAtIndex3")]
        public void ScanData_KeepsCentroidsWhenDescriptionUnreadable(string description)
        {
            var scan = MockMsScan.WithPeaks(11.23, "9248", description, (900.25, 111.0), (901.75, 222.0));

            int abortSignals = 0;
            var captured = new List<ScanData>();
            var pipe = new DataPipe(new RecordingProcessor(captured), _ => abortSignals++);

            pipe.Push(scan);
            pipe.Complete();
            Assert.IsTrue(pipe.WaitForCompletion().Wait(TimeSpan.FromSeconds(5)), "DataPipe should complete");

            //asserted HERE, not inside onFailure: that callback runs on the pool thread inside
            //DataPipe's swallowing guard, so an Assert.Fail there could never fail the test.
            Assert.AreEqual(0, abortSignals, "Reading this scan must not fail");
            Assert.AreEqual(1, captured.Count);
            CollectionAssert.AreEqual(new[] { 900.25, 901.75 }, captured[0].Mzs,
                "An unreadable description must fall back to enumerating, not to discarding");
            CollectionAssert.AreEqual(new[] { 111.0, 222.0 }, captured[0].Intensities);
        }

        /// <summary>Captures every ScanData the consumer was handed, in order.</summary>
        private class RecordingProcessor : IScanProcessor
        {
            private readonly List<ScanData> captured;
            public RecordingProcessor(List<ScanData> captured) { this.captured = captured; }
            public void ProcessMS(ScanData scan) { captured.Add(scan); }
        }

        /// <summary>
        /// Records what the consumer actually received. Parks on the first scan so a later one can
        /// be observed sitting in the queue rather than in flight.
        /// </summary>
        private class CapturingProcessor : IScanProcessor
        {
            private readonly ManualResetEventSlim entered;
            private readonly ManualResetEventSlim release;
            private readonly List<ScanData> captured;
            private bool parked;

            public CapturingProcessor(ManualResetEventSlim entered, ManualResetEventSlim release,
                List<ScanData> captured)
            {
                this.entered = entered;
                this.release = release;
                this.captured = captured;
            }

            public void ProcessMS(ScanData scan)
            {
                if (!parked)
                {
                    parked = true;
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }

                captured.Add(scan);
            }
        }

        private class CountingProcessor : IScanProcessor
        {
            private Action onProcess;
            public CountingProcessor(Action onProcess) { this.onProcess = onProcess; }
            public void ProcessMS(ScanData scan) { onProcess(); }
        }

        private class ThrowingProcessor : IScanProcessor
        {
            public int CallCount { get; private set; }
            public void ProcessMS(ScanData scan)
            {
                CallCount++;
                throw new InvalidOperationException("simulated scan processing failure");
            }
        }
    }
}
