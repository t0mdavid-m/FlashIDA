using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash
{
    public class DataPipe
    {
        private BufferBlock<IMsScan> inputScans;
        private ActionBlock<IMsScan> processBlock;

        public DataPipe(IScanProcessor processor)
        {
            inputScans = new BufferBlock<IMsScan>();
            processBlock = new ActionBlock<IMsScan>(scan => processor.ProcessMS(scan));
            inputScans.LinkTo(processBlock,
                new DataflowLinkOptions { PropagateCompletion = true });
        }

        public void Push(IMsScan scan) => inputScans.Post(scan);
        public void Complete() => inputScans.Complete();
        public Task WaitForCompletion() => processBlock.Completion;
    }
}
