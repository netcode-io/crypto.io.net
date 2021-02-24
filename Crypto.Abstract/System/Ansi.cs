namespace System
{
    /// <summary>
    /// Controls colored console output
    /// </summary>
    public static class Ansi
    {
        public const string Reset = "\x1b[0m";      // Reset

        // Regular
        public const string Black = "\x1b[30m";     // Black
        public const string Coal = "\x1b[90m";      // Black
        public const string Gray = "\x1b[37m";      // White
        public const string White = "\x1b[97m";     // White
        public const string Maroon = "\x1b[31m";    // Red
        public const string Red = "\x1b[91m";       // Red
        public const string Green = "\x1b[32m";     // Green
        public const string Lime = "\x1b[92m";      // Green
        public const string Orange = "\x1b[33m";    // Yellow
        public const string Yellow = "\x1b[93m";    // Yellow
        public const string Navy = "\x1b[34m";      // Blue
        public const string Blue = "\x1b[94m";      // Blue
        public const string Violet = "\x1b[35m";    // Purple
        public const string Purple = "\x1b[95m";    // Purple
        public const string Teal = "\x1b[36m";      // Cyan
        public const string Cyan = "\x1b[96m";      // Cyan

        // Bold
        public const string BlackBold = "\x1b[1;30m";   // Black
        public const string CoalBold = "\x1b[1;90m";    // Black
        public const string GrayBold = "\x1b[1;37m";    // White
        public const string WhiteBold = "\x1b[1;97m";   // White
        public const string MaroonBold = "\x1b[1;31m";  // Red
        public const string RedBold = "\x1b[1;91m";     // Red
        public const string GreenBold = "\x1b[1;32m";   // Green
        public const string LimeBold = "\x1b[1;92m";    // Green
        public const string OrangeBold = "\x1b[1;33m";  // Yellow
        public const string YellowBold = "\x1b[1;93m";  // Yellow
        public const string NavyBold = "\x1b[1;34m";    // Blue
        public const string BlueBold = "\x1b[1;94m";    // Blue
        public const string VioletBold = "\x1b[1;35m";  // Purple
        public const string PurpleBold = "\x1b[1;95m";  // Purple
        public const string TealBold = "\x1b[1;36m";    // Cyan
        public const string CyanBold = "\x1b[1;96m";    // Cyan

        // Background
        public const string OnBlack = "\x1b[40m";   // Black
        public const string OnCoal = "\x1b[100m";   // Black
        public const string OnGray = "\x1b[47m";    // White
        public const string OnWhite = "\x1b[107m";  // White
        public const string OnMaroon = "\x1b[41m";  // Red
        public const string OnRed = "\x1b[101m";    // Red
        public const string OnGreen = "\x1b[42m";   // Green
        public const string OnLime = "\x1b[102m";   // Green
        public const string OnOrange = "\x1b[43m";  // Yellow
        public const string OnYellow = "\x1b[103m"; // Yellow
        public const string OnNavy = "\x1b[44m";    // Blue
        public const string OnBlue = "\x1b[104m";   // Blue
        public const string OnViolet = "\x1b[45m";  // Purple
        public const string OnPurple = "\x1b[105m"; // Purple
        public const string OnTeal = "\x1b[46m";    // Cyan
        public const string OnCyan = "\x1b[106m";   // Cyan

        // Underline
        public const string BlackUnder = "\x1b[4;30m";  // Black
        public const string GrayUnder = "\x1b[4;37m";   // White
        public const string MaroonUnder = "\x1b[4;31m"; // Red
        public const string GreenUnder = "\x1b[4;32m";  // Green
        public const string OrangeUnder = "\x1b[4;33m"; // Yellow
        public const string NavyUnder = "\x1b[4;34m";   // Blue
        public const string VioletUnder = "\x1b[4;35m"; // Purple
        public const string TealUnder = "\x1b[4;36m";   // Cyan
    }
}
