using System.Globalization;
using System.Numerics;

namespace Crypto.IO
{
    public static class Extensions
    {
        static readonly string[] HashSuffixes = { "h", "Kh", "Mh", "Gh", "Th" };
        static readonly string[] MemorySuffixes = { "B", "KB", "MB", "GB", "TB" };
        static readonly BigInteger Hash_Dividend = new BigInteger(
            new byte[] { 0xff, 0xff, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00 });
        static readonly BigInteger Diff_Base = new BigInteger(
            new byte[] { 00, 00, 00, 00, 0xff, 0xff, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00, 00 });
        static readonly BigInteger Diff_ZeroProduct = new BigInteger(
            new byte[] { 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff });

        public static byte[] ToTargetFromDiff(this double diff)
        {
            BigInteger product;
            if (diff == 0)
                product = Diff_ZeroProduct;
            else
            {
                diff = 1 / diff;
                var idiff = new BigInteger(diff);
                product = Diff_Base * idiff;
                var sdiff = product.ToString();
                var ldiff = sdiff.Length;
                var offset = sdiff.IndexOf('.');
                if (offset != -1)
                {
                    // Number of decimal places
                    var precision = (ldiff - 1) - offset;

                    // Effective sequence of decimal places
                    var decimals = sdiff.Substring(offset + 1);

                    // Strip leading zeroes. If a string begins with 0 or 0x boost parser considers it hex
                    //decimals = decimals.Remove(0, decimals.IndexOf(x => x != '0'));

                    // Build up the divisor as string - just in case parser does some implicit conversion with 10^precision
                    //string decimalDivisor = "1";
                    //decimalDivisor.resize(precision + 1, '0');

                    //// This is the multiplier for the decimal part
                    //var multiplier = new BigInteger(decimals);

                    //// This is the divisor for the decimal part
                    //var divisor = new BigInteger(decimalDivisor);

                    //var decimalproduct = (Diff_Base * multiplier) / divisor;

                    //// Add the computed decimal part to product
                    //product += decimalproduct;
                }
            }
            return product.ToByteArray();
        }

        public static double ToTargetFromHash(this byte[] target)
        {
            var divisor = new BigInteger(target);
            return (double)(Hash_Dividend / divisor);
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
