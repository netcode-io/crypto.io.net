namespace Crypto.IO.Miner
{
    /// <summary>
    /// HwSensorsType
    /// </summary>
    public class HwSensorsType
    {
        public int TempC = 0;
        public int FanP = 0;
        public double PowerW = 0.0;
        public override string ToString() =>
            $"{TempC}C {FanP}%{(PowerW != 0.0 ? $" {PowerW,2}W" : null)}";
    }
}
