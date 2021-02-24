namespace Crypto.IO
{
    /// <summary>
    /// EpochContext
    /// </summary>
    public class EpochContext
    {
        public int EpochNumber;
        public int LightNumItems;
        public int LightSize;
        public object LightCache;
        public int DagNumItems;
        public ulong DagSize;
    }
}
