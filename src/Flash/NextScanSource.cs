using System;
using System.Collections.Concurrent;
using Flash.IDA;
using log4net;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

namespace Flash
{
    /// <summary>
    /// Drains one <see cref="ScanCommand"/> out of the engine.
    /// </summary>
    /// <param name="cmd">Receives the dequeued command when the call returns true.</param>
    /// <returns>true if <paramref name="cmd"/> was filled.</returns>
    /// <remarks>
    /// Mirrors <c>FLASHIdaWrapper.GetNextScanCommand(ref ScanCommand) == 1</c>. Note that the engine
    /// never reports an empty queue - every path in <c>FLASHIda::getNextScanCommand</c> returns 1, and
    /// an exhausted queue produces a fabricated idle AGC. A <c>false</c> here therefore means the
    /// wrapper CAUGHT something, never "nothing to do".
    /// </remarks>
    public delegate bool TryDrainCommand(out ScanCommand cmd);

    /// <summary>
    /// Holds the next instrument request ready so the instrument event thread only has to submit it.
    /// </summary>
    /// <remarks>
    /// The acquisition loop used to drain the engine and build the request inline in
    /// <c>ProcessSpectrum</c>, on the instrument event thread, between the scan arriving and the next
    /// command going out. That drain is not cheap: <c>getNextScanCommand</c> flushes a TSV row to disk
    /// on every call and writes to stdout on the AGC and idle paths, and it can additionally park
    /// behind a whole deconvolution because <c>processScan</c> holds <c>analysis_mutex_</c> for its
    /// entire body. The instrument waits for all of it.
    ///
    /// So an ARMED COMMAND - drained and built ahead of time - is held here, and the event handler
    /// does nothing but hand it over. See docs/adr/0024-scan-commands-are-armed-off-the-event-thread.md
    /// for why the two obvious alternatives (SingleProcessingDelay, CanAcceptNextCustomScan) are
    /// unavailable, and for the costs this trade accepts.
    ///
    /// Threading: <see cref="OnScanArrived"/> and <see cref="Next"/> are called only from the
    /// serialized instrument event thread, which is why the counters below need no synchronization.
    /// <see cref="FillOnce"/> runs on whatever thread <c>spawn</c> supplies and touches only the
    /// concurrent queue.
    /// </remarks>
    public sealed class NextScanSource
    {
        private static readonly ILog log = LogManager.GetLogger("General");

        /// <summary>One armed command: the request to submit, plus the struct it was built from.</summary>
        /// <remarks>
        /// The raw <see cref="ScanCommand"/> rides along for diagnostics only - nothing submits it.
        /// </remarks>
        private struct Armed
        {
            public ScanCommand Cmd;
            public IFusionCustomScan Built;
        }

        private readonly ConcurrentQueue<Armed> queue = new ConcurrentQueue<Armed>();

        private readonly TryDrainCommand drain;
        private readonly Func<ScanCommand, IFusionCustomScan> build;
        private readonly Func<IFusionCustomScan> buildFiller;
        private readonly Action<Action> spawn;

        //filler scans sent since the last armed command was taken; 0 while not dry
        private int dryRunFillers;

        /// <summary>How many times over the run no armed command was ready when one was needed.</summary>
        /// <remarks>
        /// Counts dry RUNS, not filler scans - a single stall emits one run and however many fillers it
        /// took to ride it out. Filler scans carry no tracking id, so the engine rejects their echo
        /// before deconvolution and they appear in no engine log; this counter and the warnings beside
        /// it are the only trace they leave.
        /// </remarks>
        public int DryRuns { get; private set; }

        /// <summary>
        /// Construct a source over the four collaborators it needs. All are injected so the buffer can
        /// be exercised without an engine, a ScanFactory or an instrument.
        /// </summary>
        /// <param name="drain">Pulls the next command out of the engine.</param>
        /// <param name="build">Turns a command into an instrument request.</param>
        /// <param name="buildFiller">Builds the cheap scan sent when nothing is armed.</param>
        /// <param name="spawn">Runs the refill. Production hands this to a background thread; tests
        /// invoke it inline, which is what keeps the suite free of sleeps.</param>
        public NextScanSource(TryDrainCommand drain,
                              Func<ScanCommand, IFusionCustomScan> build,
                              Func<IFusionCustomScan> buildFiller,
                              Action<Action> spawn)
        {
            if (drain == null) throw new ArgumentNullException(nameof(drain));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (buildFiller == null) throw new ArgumentNullException(nameof(buildFiller));
            if (spawn == null) throw new ArgumentNullException(nameof(spawn));

            this.drain = drain;
            this.build = build;
            this.buildFiller = buildFiller;
            this.spawn = spawn;
        }

        /// <summary>
        /// Fill the queue once, synchronously. Called at handshake-send, before acquisition starts.
        /// </summary>
        /// <remarks>
        /// Deliberately synchronous rather than routed through <c>spawn</c>. The handshake is a
        /// MaxIT = 1 Turbo IonTrap scan whose echo can return in tens of milliseconds, so an
        /// asynchronous arm would be racing it for no benefit - and at this point nothing is acquiring
        /// and no <c>processScan</c> has ever run, so the drain is uncontended however long it takes.
        /// </remarks>
        public void Arm()
        {
            FillOnce();
        }

        /// <summary>
        /// Top up the queue if it is running low. Non-blocking - the refill happens on another thread.
        /// </summary>
        /// <remarks>
        /// The guard is on queue depth, not on whether a refill is already in flight, so two refills
        /// can overlap while the drain is slow. Accepted: each one independently drains and enqueues,
        /// so nothing is lost, and they recover a stall faster than a single-occupancy filler would.
        /// The costs are recorded in ADR-0024 - a duplicate idle AGC, and completion-ordered rather
        /// than drain-ordered enqueue.
        /// </remarks>
        public void OnScanArrived()
        {
            if (queue.Count <= 1) spawn(FillOnce);
        }

        /// <summary>
        /// The request to submit for this scan: the armed command, or a filler if none is ready.
        /// </summary>
        /// <returns>
        /// Null only if the filler itself could not be built, which <c>SendCustomScan</c> already
        /// handles. This method never throws - an escaping exception here would reach the instrument
        /// event thread, where it does not crash the process the usual way but leaves the API in a
        /// weird state.
        /// </returns>
        public IFusionCustomScan Next()
        {
            if (queue.TryDequeue(out var armed))
            {
                if (dryRunFillers > 0)
                {
                    log.Warn(String.Format("Armed command available again after {0} filler scan(s)", dryRunFillers));
                    dryRunFillers = 0;
                }
                return armed.Built;
            }

            //warn once per dry RUN, not per filler: a long deconvolution would otherwise emit dozens
            if (dryRunFillers++ == 0)
            {
                DryRuns++;
                log.Warn("No armed command ready - sending a filler AGC scan");
            }

            try
            {
                return buildFiller();
            }
            catch (Exception ex)
            {
                log.Fatal(String.Format("Could not build a filler scan: {0}\n{1}", ex.Message, ex.StackTrace));
                return null;
            }
        }

        /// <summary>
        /// Drain one command, build it, and hand it to the queue.
        /// </summary>
        /// <remarks>
        /// MUST NOT throw, whatever the caller does with it. This runs as the whole body of a filler
        /// thread: an escaping exception kills that thread, the queue never refills again, and
        /// <c>ProcessSpectrum</c> goes on sending filler AGCs for the rest of the run. The instrument
        /// stays busy and the logs stay clean while nothing at all is acquired. That is the same
        /// failure shape <c>DataPipe</c>'s ActionBlock documents, and it is invisible from every side.
        ///
        /// On a refusal the command is DROPPED and not retried. It has already been registered pending
        /// and already has its scan_commands.tsv row, so it is a scan that will never happen - but a
        /// refusal usually means a whole class of commands is malformed, and re-draining would discard
        /// a burst of them. The empty queue is handled by <see cref="Next"/> instead.
        /// </remarks>
        internal void FillOnce()
        {
            //declared outside the try: an `out var` scoped to the try block is not visible in the catch
            ScanCommand cmd = default(ScanCommand);
            try
            {
                if (!drain(out cmd)) return;
                queue.Enqueue(new Armed { Cmd = cmd, Built = build(cmd) });
            }
            catch (Exception ex)
            {
                //BuildFromCommand refuses a command whose stage geometry is incomplete rather than
                //zero-filling it, because m/z 0 is malformed and not "unused" (ADR-0010).
                log.Fatal(String.Format("Refused to arm scan {0}: {1}", cmd.ScanId, ex.Message));
            }
        }
    }
}
