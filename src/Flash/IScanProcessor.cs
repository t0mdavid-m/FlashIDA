using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash
{
    public interface IScanProcessor
    {
        void ProcessMS(IMsScan msScan);
    }
}
