using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using log4net;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash
{
    public class DataPipe
    {
        private static readonly ILog log = LogManager.GetLogger("General");

        private BufferBlock<IMsScan> inputScans;
        private ActionBlock<IMsScan> processBlock;

        /// <summary>
        /// Two-stage scan processing pipeline.
        /// </summary>
        /// <param name="processor">Consumer invoked for each pushed scan.</param>
        /// <param name="onFailure">
        /// Invoked when <paramref name="processor"/> throws. Required (not optional) so the compiler
        /// enforces the wiring - an unwired callback would log the failure and silently never abort.
        /// </param>
        public DataPipe(IScanProcessor processor, Action<Exception> onFailure)
        {
            if (processor == null) throw new ArgumentNullException("processor");
            if (onFailure == null) throw new ArgumentNullException("onFailure");

            inputScans = new BufferBlock<IMsScan>();

            //NOTHING may run outside the try/catch below. An exception escaping this delegate faults
            //the block PERMANENTLY: the link is severed, but BufferBlock.Post keeps returning true so
            //the producer never learns, Completion is observed by nobody, and because
            //GetNextScanCommand never returns 0 the instrument keeps running fabricated idle AGC
            //scans for the rest of the gradient while the engine sees no scan at all. A total, silent
            //loss of acquisition.
            //
            //That is not hypothetical: a `finally { scan.Dispose(); }` used to sit here, OUTSIDE the
            //catch, and it killed real runs after the very first scan.
            //
            //We do NOT dispose the scan. An IMsScan is a handle to FRAMEWORK-owned shared memory that
            //the iAPI releases by itself - "the content will be released [when] the IMsScanContainer's
            //LastScan property has changed" (dependencies/API-2.0.xml, IMsScan) - i.e. as soon as the
            //next scan arrives. Disposing it here is disposal late, out of arrival order, from a pool
            //thread that never acquired the handle. Nobody in this process disposes an IMsScan.
            processBlock = new ActionBlock<IMsScan>(scan =>
            {
                try
                {
                    processor.ProcessMS(scan);
                }
                catch (Exception ex)
                {
                    //the abort path is itself guarded: a throwing logger or onFailure must not be
                    //able to do what the exception it is reporting was already prevented from doing
                    try
                    {
                        log.Fatal(String.Format("Scan processing failed: {0}\n{1}", ex.Message, ex.StackTrace));
                        onFailure(ex);
                    }
                    catch
                    {
                    }
                }
            });

            inputScans.LinkTo(processBlock,
                new DataflowLinkOptions { PropagateCompletion = true });

            //Last-resort visibility. After the guards above the delegate cannot fault through any
            //exception `catch (Exception)` can hold - but StackOverflow, OOM and (on .NET Framework)
            //a re-raised ThreadAbort are not among those. A faulted block is the one failure mode
            //that costs an entire gradient without producing a single log line, and production never
            //awaits Completion (only tests do), so it gets an explicit observer instead of being
            //left to nobody. Ending the run beats acquiring nothing for the next several hours.
            processBlock.Completion.ContinueWith(t =>
            {
                if (!t.IsFaulted) return;
                try
                {
                    log.Fatal(String.Format("Scan pipeline faulted - acquisition is dead: {0}", t.Exception));
                    onFailure(t.Exception);
                }
                catch
                {
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Hand a scan to the pipeline. Returns whether the pipeline ACCEPTED it.
        /// </summary>
        /// <remarks>
        /// This is not an ownership transfer: nobody disposes an <see cref="IMsScan"/> (see the
        /// constructor). A <c>false</c> means the scan was DROPPED and will never be processed, which
        /// production has no legitimate reason to see - the block is completed only by tests - so the
        /// caller should log it rather than ignore it.
        /// </remarks>
        public bool Push(IMsScan scan) => inputScans.Post(scan);
        public void Complete() => inputScans.Complete();
        public Task WaitForCompletion() => processBlock.Completion;
    }
}
