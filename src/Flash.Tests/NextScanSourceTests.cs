using System;
using System.Collections.Generic;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

namespace Flash.Tests
{
    /// <summary>
    /// Tests for <see cref="NextScanSource"/>, the armed-command buffer that keeps the engine drain
    /// off the instrument event thread (ADR-0024).
    ///
    /// Everything here is driven through the four injected seams - drain, build, filler and spawn -
    /// so no engine, no OpenMS.dll and no instrument are involved. <c>spawn</c> runs the refill
    /// INLINE, which is what makes the suite deterministic: there is no thread to wait on and no
    /// sleep anywhere in this file.
    /// </summary>
    [TestFixture]
    public class NextScanSourceTests
    {
        /// RunningNumber stamped on the fake filler so a test can tell it from an armed command
        private const long FillerMarker = -999L;

        private Queue<ScanCommand> toDrain;
        private int drainCalls;
        private int spawnCalls;
        private int fillerBuilds;
        private Func<ScanCommand, IFusionCustomScan> build;

        [OneTimeSetUp]
        public void ConfigureLogging()
        {
            //NextScanSource logs through the static "General" logger; an unconfigured repository is
            //what produced NullReferenceExceptions in the other fixtures.
            if (!log4net.LogManager.GetRepository().Configured)
            {
                log4net.Config.BasicConfigurator.Configure(
                    new log4net.Appender.ConsoleAppender { Threshold = log4net.Core.Level.Off });
            }
        }

        [SetUp]
        public void Reset()
        {
            toDrain = new Queue<ScanCommand>();
            drainCalls = 0;
            spawnCalls = 0;
            fillerBuilds = 0;
            build = cmd => new MockCustomScan { RunningNumber = cmd.ScanId };
        }

        private static ScanCommand Cmd(int id)
        {
            return new ScanCommand { ScanId = id };
        }

        /// spawn that runs the refill inline
        private void Spawn(Action a)
        {
            spawnCalls++;
            a();
        }

        /// spawn that only records the decision, so a test can observe it without side effects
        private void SpawnWithoutRunning(Action a)
        {
            spawnCalls++;
        }

        private bool Drain(out ScanCommand cmd)
        {
            drainCalls++;
            if (toDrain.Count == 0)
            {
                cmd = default(ScanCommand);
                return false;
            }
            cmd = toDrain.Dequeue();
            return true;
        }

        private IFusionCustomScan Filler()
        {
            fillerBuilds++;
            return new MockCustomScan { RunningNumber = FillerMarker };
        }

        private NextScanSource Make(Action<Action> spawn = null)
        {
            //`spawn ?? Spawn` does not compile - a method group has no type, so it cannot be the
            //right operand of ??. Convert first.
            Action<Action> effectiveSpawn = spawn ?? new Action<Action>(Spawn);

            //build is indirected through the field so a test can swap it after construction
            return new NextScanSource(Drain, c => build(c), Filler, effectiveSpawn);
        }

        /// <summary>
        /// Arming at handshake-send must be done by the time it returns. It is deliberately NOT routed
        /// through spawn: the handshake is a MaxIT = 1 Turbo IonTrap scan whose echo can come back in
        /// tens of milliseconds, and an asynchronous arm would be racing it for no benefit.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void Arm_FillsQueueSynchronously()
        {
            toDrain.Enqueue(Cmd(1));
            var src = Make();

            src.Arm();

            Assert.AreEqual(1L, src.Next().RunningNumber, "Arm should have left a command ready");
            Assert.AreEqual(0, fillerBuilds, "an armed command was ready, so no filler was needed");
            Assert.AreEqual(0, spawnCalls, "Arm must be synchronous, not routed through spawn");
        }

        [Test]
        [Category("Tier1")]
        public void OnScanArrived_Spawns_WhenEmpty()
        {
            var src = Make(SpawnWithoutRunning);

            src.OnScanArrived();

            Assert.AreEqual(1, spawnCalls);
        }

        /// <summary>
        /// The boundary that gives the whole design its head start. With one command already armed we
        /// still refill, so the NEXT scan is answered from the queue rather than from a fresh drain.
        /// A guard of `&lt; 1` instead of `&lt;= 1` would leave the queue empty after every send.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void OnScanArrived_Spawns_WhenCountIsOne()
        {
            toDrain.Enqueue(Cmd(1));
            var src = Make(SpawnWithoutRunning);
            src.Arm();

            src.OnScanArrived();

            Assert.AreEqual(1, spawnCalls, "count == 1 must still spawn - that is the head start");
        }

        [Test]
        [Category("Tier1")]
        public void OnScanArrived_DoesNotSpawn_WhenCountIsTwo()
        {
            toDrain.Enqueue(Cmd(1));
            toDrain.Enqueue(Cmd(2));
            var src = Make(SpawnWithoutRunning);
            src.Arm();
            src.Arm();

            src.OnScanArrived();

            Assert.AreEqual(0, spawnCalls, "depth must stay bounded - two armed commands is enough");
        }

        /// <summary>
        /// The instrument must never be left unfed. When nothing is armed, Next hands back a filler
        /// rather than null, and counts the dry run - filler scans carry no tracking id, so the engine
        /// rejects their echo before deconvolution and this counter is the only trace they leave.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void Next_ReturnsFiller_WhenEmpty()
        {
            var src = Make();

            var next = src.Next();

            Assert.AreEqual(FillerMarker, next.RunningNumber);
            Assert.AreEqual(1, fillerBuilds);
            Assert.AreEqual(1, src.DryRuns);
        }

        /// <summary>
        /// A drained command is never dropped or reordered. Sequential fills only - two refills racing
        /// in production enqueue in completion order, which ADR-0024 records as an accepted cost.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void Next_ReturnsArmedInFillOrder()
        {
            toDrain.Enqueue(Cmd(11));
            toDrain.Enqueue(Cmd(22));
            var src = Make();
            src.Arm();
            src.Arm();

            Assert.AreEqual(11L, src.Next().RunningNumber);
            Assert.AreEqual(22L, src.Next().RunningNumber);
            Assert.AreEqual(0, fillerBuilds, "neither take should have fallen through to a filler");
        }

        /// <summary>
        /// THE invariant. The consumer path exists precisely so that the instrument event thread never
        /// performs the drain - which flushes a TSV row to disk, writes to stdout, and can park behind
        /// a whole deconvolution holding analysis_mutex_. This test fails the moment anyone
        /// reintroduces a synchronous fallback drain, including on the dry path.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void Next_NeverDrains()
        {
            toDrain.Enqueue(Cmd(1));
            var src = Make();
            src.Arm();
            int drainsAfterArm = drainCalls;

            src.Next();     //takes the armed command
            src.Next();     //runs dry and falls back to a filler - still must not drain

            Assert.AreEqual(drainsAfterArm, drainCalls, "the consumer path must never drain");
        }

        /// <summary>
        /// BuildFromCommand refuses a command whose stage geometry is incomplete rather than
        /// zero-filling it (ADR-0010). The refusal is logged and the command dropped; nothing is
        /// enqueued and nothing escapes.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void FillOnce_BuildThrows_EnqueuesNothing_AndDoesNotPropagate()
        {
            toDrain.Enqueue(Cmd(1));
            build = _ => throw new InvalidOperationException("stage 0 has no isolation geometry");
            var src = Make();

            Assert.DoesNotThrow(() => src.Arm());

            Assert.AreEqual(FillerMarker, src.Next().RunningNumber, "nothing should have been armed");
        }

        /// <summary>
        /// The silent-death mode, and the reason FillOnce's catch is mandatory rather than stylistic.
        ///
        /// FillOnce is the whole body of a filler thread. If a refusal escaped it, that thread would
        /// die, the queue would never refill, and ProcessSpectrum would go on sending filler AGCs for
        /// the rest of the run - instrument busy, logs clean, nothing acquired. There is no other
        /// symptom, and no other test would catch it.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void FillOnce_AfterBuildThrew_StillFills()
        {
            toDrain.Enqueue(Cmd(1));
            toDrain.Enqueue(Cmd(2));
            bool firstCall = true;
            build = cmd =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    throw new InvalidOperationException("stage 0 has no isolation geometry");
                }
                return new MockCustomScan { RunningNumber = cmd.ScanId };
            };
            var src = Make();

            src.Arm();      //refused and dropped
            src.Arm();      //must still work

            Assert.AreEqual(2L, src.Next().RunningNumber, "the source must survive a refusal");
        }

        /// <summary>
        /// The engine never reports an empty queue - every path in getNextScanCommand returns 1, and an
        /// exhausted queue produces a fabricated idle AGC. So a false from the drain means the wrapper
        /// CAUGHT something, and must not be mistaken for a command.
        /// </summary>
        [Test]
        [Category("Tier1")]
        public void FillOnce_DrainReturnsFalse_EnqueuesNothing()
        {
            var src = Make();       //toDrain is empty, so Drain returns false

            src.Arm();

            Assert.AreEqual(1, drainCalls, "the drain should have been attempted once");
            Assert.AreEqual(FillerMarker, src.Next().RunningNumber, "nothing should have been armed");
        }
    }
}
