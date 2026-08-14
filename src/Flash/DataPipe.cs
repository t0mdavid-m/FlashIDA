using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using log4net;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash
{
    /// <summary>
    /// An OWNED copy of everything the engine needs from one instrument scan.
    /// </summary>
    /// <remarks>
    /// The pipeline used to queue the <see cref="IMsScan"/> handle itself and let the consumer read
    /// Centroids/Header/Trailer through it at dequeue time. That is only safe while the queue is
    /// shallow: an IMsScan is a window onto FRAMEWORK-owned memory that the iAPI releases as soon as
    /// the next scan replaces it as the container's LastScan, so a scan still waiting in the queue at
    /// that moment is a handle to memory that may already be gone. Nothing signals it and nothing
    /// throws on the producer side - the scan is simply lost, silently and corruptly.
    ///
    /// It never bit because the queue was ~1 deep, and it was ~1 deep by accident: the command drain
    /// blocked behind the deconvolution, which coupled the instrument's scan rate to the processing
    /// rate. Removing that stall is what makes a deep queue reachable, so the queue stops holding
    /// handles.
    ///
    /// Bounding the queue and dropping the overflow was considered and rejected: a dropped scan is
    /// not recoverable. A dropped exploration variant wedges its group for the rest of the run -
    /// Exploration::active_groups_.erase is reachable only past the all_received gate and there is no
    /// timeout - and its pending-map entry leaks, since resolvePending is the only eraser and is
    /// reached only from processScan. Copying is cheap; losing a scan is not.
    ///
    /// Six fields, because six is exactly what crosses the bridge. Immutable, so a queued snapshot
    /// cannot be perturbed by anything that happens after it was taken.
    /// </remarks>
    public class ScanData
    {
        public readonly double[] Mzs;
        public readonly double[] Intensities;
        public readonly double RetentionTime;
        public readonly int MsLevel;
        public readonly string ScanDescription;
        public readonly double FaimsCv;

        private ScanData(double[] mzs, double[] intensities, double retentionTime,
            int msLevel, string scanDescription, double faimsCv)
        {
            Mzs = mzs;
            Intensities = intensities;
            RetentionTime = retentionTime;
            MsLevel = msLevel;
            ScanDescription = scanDescription;
            FaimsCv = faimsCv;
        }

        /// <summary>
        /// Snapshot a scan. MUST be called while the handle is still live - i.e. on the thread that
        /// received it, before returning from the arrival callback.
        /// </summary>
        /// <remarks>
        /// This is the ONLY place an IMsScan is read. Anything that needs a seventh value adds it
        /// here, not at the consumer - a field read lazily from the handle later would reintroduce
        /// exactly the defect this type exists to remove.
        ///
        /// One pass over Centroids, not two. The old consumer ran two separate Select().ToArray()
        /// projections; the values are identical either way, but this now runs on the instrument
        /// event thread, so halving the enumeration is worth having.
        /// </remarks>
        public static ScanData From(IMsScan msScan)
        {
            var mzs = new List<double>();
            var intensities = new List<double>();
            foreach (var centroid in msScan.Centroids)
            {
                mzs.Add(centroid.Mz);
                intensities.Add(centroid.Intensity);
            }

            string scanDescription;
            msScan.Trailer.TryGetValue("Scan Description", out scanDescription);

            double faimsCv = 0.0;
            string cvStr;
            if (msScan.Trailer.TryGetValue("FAIMS CV", out cvStr))
                double.TryParse(cvStr, out faimsCv);

            return new ScanData(
                mzs.ToArray(),
                intensities.ToArray(),
                double.Parse(msScan.Header["StartTime"]),
                int.Parse(msScan.Header["MSOrder"]),
                scanDescription ?? "",
                faimsCv);
        }
    }

    public class DataPipe
    {
        private static readonly ILog log = LogManager.GetLogger("General");

        private BufferBlock<ScanData> inputScans;
        private ActionBlock<ScanData> processBlock;

        //Held as a field, not just captured, because Push needs it too: a scan that cannot be
        //snapshotted must reach the same abort path as one that cannot be processed.
        private readonly Action<Exception> onFailure;

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
            this.onFailure = onFailure;

            inputScans = new BufferBlock<ScanData>();

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
            //No IMsScan reaches this block any more - Push snapshots it into a ScanData on the
            //arrival thread, so what is queued is owned memory and the framework handle never
            //outlives the callback that received it. The disposal rule still holds, it just applies
            //at the boundary now: nobody in this process disposes an IMsScan, because the iAPI
            //releases the content itself once its LastScan advances (dependencies/API-2.0.xml).
            //
            //MaxDegreeOfParallelism is stated rather than left to the TPL default. It was always 1,
            //but by accident of the default - and the engine relies on it: processScan is serialised
            //against itself by THIS block and by nothing else, which is why analysis_mutex_ only has
            //to defend against the drain.
            processBlock = new ActionBlock<ScanData>(scan =>
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
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

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
        /// Snapshot a scan and hand it to the pipeline. Returns whether the pipeline ACCEPTED it.
        /// </summary>
        /// <remarks>
        /// MUST be called on the thread that received the scan, while the handle is still live. The
        /// snapshot happens HERE, synchronously, and that placement is the whole point: after this
        /// returns, the queue holds owned memory and the iAPI may release the original whenever it
        /// likes.
        ///
        /// Still not an ownership transfer - nobody disposes an <see cref="IMsScan"/>.
        ///
        /// A <c>false</c> means the scan will never be processed, and there are now two ways to get
        /// one. The block refusing it (completed only by tests) is the old one. The new one is a scan
        /// we could not read: a malformed header, or a handle already released before we got to it.
        /// That path takes the same FATAL-plus-onFailure route as a processing failure, because it
        /// used to BE one - the parsing ran inside the consumer's try/catch - and quietly downgrading
        /// an abort to a skip is not this change's business. It is caught rather than thrown because
        /// the caller is the instrument event thread, where an unhandled exception does not fail
        /// loudly, it leaves the API in a strange state.
        /// </remarks>
        public bool Push(IMsScan scan)
        {
            ScanData snapshot;
            try
            {
                snapshot = ScanData.From(scan);
            }
            catch (Exception ex)
            {
                //guarded exactly like the consumer's abort path, and for the same reason: a throwing
                //logger or onFailure must not do what the exception it reports was prevented from doing
                try
                {
                    log.Fatal(String.Format("Could not read an arriving scan: {0}\n{1}", ex.Message, ex.StackTrace));
                    onFailure(ex);
                }
                catch
                {
                }
                return false;
            }

            return inputScans.Post(snapshot);
        }
        public void Complete() => inputScans.Complete();
        public Task WaitForCompletion() => processBlock.Completion;
    }
}
