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

            //The pipeline is the LAST reader of the scan, so the pipeline disposes it. Letting the
            //exception escape this delegate would fault the block permanently and unobserved: the
            //link is severed, the buffer grows forever, and because GetNextScanCommand never returns
            //0 the instrument keeps running idle AGC scans for the rest of the run with no log line.
            processBlock = new ActionBlock<IMsScan>(scan =>
            {
                try
                {
                    processor.ProcessMS(scan);
                }
                catch (Exception ex)
                {
                    log.Fatal(String.Format("Scan processing failed: {0}\n{1}", ex.Message, ex.StackTrace));
                    onFailure(ex);
                }
                finally
                {
                    //free Thermo shared memory as soon as we are actually done reading it
                    scan.Dispose();
                }
            });

            inputScans.LinkTo(processBlock,
                new DataflowLinkOptions { PropagateCompletion = true });
        }

        /// <summary>
        /// Hand a scan to the pipeline. Returns true when the pipeline ACCEPTED it and therefore
        /// took ownership - the caller must not dispose an accepted scan. On false the caller still
        /// owns it and is responsible for disposal.
        /// </summary>
        public bool Push(IMsScan scan) => inputScans.Post(scan);
        public void Complete() => inputScans.Complete();
        public Task WaitForCompletion() => processBlock.Completion;
    }
}
