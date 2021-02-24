using System;

namespace Crypto.IO.Miner
{
    /// <summary>
    /// Pause mining
    /// </summary>
    [Flags]
    public enum MinerPauseTo
    {
        Overheating = 0x1,
        Api_Request = 0x2,
        Farm_Paused = 0x4,
        Insufficient_GPU_Memory = 0x8,
        Epoch_Initialization_Error = 0x10,
    }
}
