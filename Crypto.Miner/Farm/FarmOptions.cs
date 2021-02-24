namespace Crypto.IO
{
    /// <summary>
    /// FarmOptions
    /// </summary>
    public class FarmOptions
    {
        /// <summary>
        /// 0 = Parallel; 1 = Serialized
        /// </summary>
        public int DagLoadMode = 0;
        /// <summary>
        /// Whether or not to re-evaluate solutions
        /// </summary>
        public bool NoEval = false;
        /// <summary>
        /// 0 - No monitor; 1 - Temp and Fan; 2 - Temp Fan Power
        /// </summary>
        public int HwMon = 0;
        /// <summary>
        /// 0=default, 1=per session, 2=per job
        /// </summary>
        public int Ergodicity = 0;
        /// <summary>
        /// Temperature threshold to restart mining (if paused)
        /// </summary>
        public int TempStart = 40;
        /// <summary>
        /// Temperature threshold to pause mining (overheating)
        /// </summary>
        public int TempStop = 0;
    }
}
