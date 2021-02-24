namespace CryptoPool.IO.Miner
{
    public static class Extensions
    {
        static readonly string[] suffixes = { "h", "Kh", "Mh", "Gh" };

        public static string ToHashPerSecond(this float value)
        {
            var magnitude = 0;
            while (value > 1000.0f && magnitude <= 3)
            {
                value /= 1000.0f;
                magnitude++;
            }
            return $"{value} {suffixes[magnitude]}";
        }
    }
}
