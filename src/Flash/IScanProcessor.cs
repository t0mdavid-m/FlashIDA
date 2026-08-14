namespace Flash
{
    public interface IScanProcessor
    {
        /// <summary>
        /// Consume one scan.
        /// </summary>
        /// <remarks>
        /// Takes a <see cref="ScanData"/> snapshot, not an IMsScan. The consumer runs on a pool
        /// thread, arbitrarily long after the scan arrived, and an IMsScan is only readable until
        /// the iAPI releases its content - so a consumer holding one is reading memory that may
        /// already be gone. The snapshot is taken by <see cref="DataPipe.Push"/> on the arrival
        /// thread.
        ///
        /// InterfaceShapeTests pins this signature deliberately, so changing it is a decision rather
        /// than a drift.
        /// </remarks>
        void ProcessMS(ScanData scan);
    }
}
