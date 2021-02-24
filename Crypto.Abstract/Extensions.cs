using System.Globalization;
using System.Numerics;

namespace Crypto.IO
{
    public static class Extensions
    {
        static readonly string[] HashSuffixes = { "h", "Kh", "Mh", "Gh", "Th" };
        static readonly string[] MemorySuffixes = { "B", "KB", "MB", "GB", "TB" };
        static readonly BigInteger dividend = new BigInteger(
            new byte[] { 0xff, 0xff, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00 });

        public static double FromHashToTarget(this byte[] target)
        {
            var divisor = new BigInteger(target);
            return (double)(dividend / divisor);
        }

        public static string ToScaledSize(this float value, float divisor, int precision, string[] suffixes, bool addSuffix)
        {
            var magnitude = 0;
            var magnitudeMax = suffixes.Length - 1;
            while (value > divisor && magnitude <= magnitudeMax)
            {
                value /= divisor;
                magnitude++;
            }
            return $"{value.ToString("N", new NumberFormatInfo { NumberDecimalDigits = precision })}{(addSuffix ? $" {suffixes[magnitude]}" : null)}";
        }

        public static string ToFormattedHashes(this float hr, int precision = 2, bool addSuffix = true) =>
            ToScaledSize(hr, 1000f, precision, HashSuffixes, addSuffix);

        public static string ToFormattedMemory(this float mem, int precision = 2, bool addSuffix = true) =>
            ToScaledSize(mem, 1024f, precision, MemorySuffixes, addSuffix);
    }
}
