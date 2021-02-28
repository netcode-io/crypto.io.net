using System.Collections.Generic;

namespace System
{
    /// <summary>
    /// StratumFamily
    /// </summary>
    public enum StratumFamily
    {
        GetWork = 0,
        Stratum,
        Simulation
    }

    /// <summary>
    /// StratumSecLevel
    /// </summary>
    public enum StratumSecLevel
    {
        None = 0,
        Tls12,
        Tls
    }

    /// <summary>
    /// StratumVersion
    /// </summary>
    public enum StratumVersion : int
    {
        Stratum = 0,
        EthProxy = 1,
        EthereumStratum = 2,
        EthereumStratum2 = 3,
        AutoDetect = 999
    }

    /// <summary>
    /// StratumExtensions
    /// </summary>
    public static class StratumExtensions
    {
        static readonly Dictionary<string, (StratumFamily family, StratumSecLevel level, StratumVersion version)> _schemes = new Dictionary<string, (StratumFamily, StratumSecLevel, StratumVersion)> {
            /*
            This schemes are kept for backwards compatibility.
            Ethminer do perform stratum autodetection
            */
            {"stratum+tcp", (StratumFamily.Stratum, StratumSecLevel.None, StratumVersion.Stratum)},
            {"stratum1+tcp", (StratumFamily.Stratum, StratumSecLevel.None, StratumVersion.EthProxy)},
            {"stratum2+tcp", (StratumFamily.Stratum, StratumSecLevel.None, StratumVersion.EthereumStratum)},
            {"stratum3+tcp", (StratumFamily.Stratum, StratumSecLevel.None, StratumVersion.EthereumStratum2)},
            {"stratum+tls", (StratumFamily.Stratum, StratumSecLevel.Tls, StratumVersion.Stratum)},
            {"stratum1+tls", (StratumFamily.Stratum, StratumSecLevel.Tls, StratumVersion.EthProxy)},
            {"stratum2+tls", (StratumFamily.Stratum, StratumSecLevel.Tls, StratumVersion.EthereumStratum)},
            {"stratum3+tls", (StratumFamily.Stratum, StratumSecLevel.Tls, StratumVersion.EthereumStratum2)},
            {"stratum+tls12", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.Stratum)},
            {"stratum1+tls12", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthProxy)},
            {"stratum2+tls12", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthereumStratum)},
            {"stratum3+tls12", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthereumStratum2)},
            {"stratum+ssl", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.Stratum)},
            {"stratum1+ssl", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthProxy)},
            {"stratum2+ssl", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthereumStratum)},
            {"stratum3+ssl", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.EthereumStratum2)},
            {"http", (StratumFamily.GetWork, StratumSecLevel.None, StratumVersion.Stratum)},
            {"getwork", (StratumFamily.GetWork, StratumSecLevel.None, StratumVersion.Stratum)},

            /*
            Any TCP scheme has, at the moment, only STRATUM protocol thus reiterating "stratum" word would be pleonastic
            Version 999 means auto-detect stratum mode
            */
            {"stratum", (StratumFamily.Stratum, StratumSecLevel.None, StratumVersion.AutoDetect)},
            {"stratums", (StratumFamily.Stratum, StratumSecLevel.Tls, StratumVersion.AutoDetect)},
            {"stratumss", (StratumFamily.Stratum, StratumSecLevel.Tls12, StratumVersion.AutoDetect)},

            /*
            The following scheme is only meant for simulation operations
            It's not meant to be used with -P arguments
            */
            {"simulation", (StratumFamily.Simulation, StratumSecLevel.None, StratumVersion.AutoDetect)}
        };

        class UriState
        {
            public StratumVersion StratumMode;
            public bool StratumModeConfirmed;
            public bool Unrecoverable;
            public bool Responds;
            public double TotalDuration;
        }

        static UriState State(this Uri source, Action<UriState> action = null) => new UriState();

        public static void SetStratumMode(this Uri source, StratumVersion mode, bool confirmed) =>
            State(source, s =>
            {
                s.StratumMode = mode;
                s.StratumModeConfirmed = confirmed;
            });
        public static void SetStratumMode(this Uri source, StratumVersion mode) => State(source).StratumMode = mode;
        public static StratumVersion GetStratumMode(this Uri source) => State(source).StratumMode;
        public static bool StratumModeConfirmed(this Uri source) => State(source).StratumModeConfirmed;
        public static bool Responds(this Uri source) => State(source).Unrecoverable;
        public static bool Responds(this Uri source, bool value) => State(source).Unrecoverable = value;
        public static bool IsUnrecoverable(this Uri source) => State(source).Unrecoverable;
        public static void MarkUnrecoverable(this Uri source) => State(source).Unrecoverable = true;
        public static StratumFamily GetStratumFamily(this Uri source) => _schemes[source.Scheme].family;
        public static StratumSecLevel GetStratumSecLevel(this Uri source) => _schemes[source.Scheme].level;
        public static StratumVersion GetStratumVersion(this Uri source) => _schemes[source.Scheme].version;

        public static void AddDuration(this Uri source, double minutes) => State(source, s => s.TotalDuration += minutes);
        public static double GetDuration(this Uri source) => State(source).TotalDuration;

        public static (string user, string worker, string password) StratumUserInfo(this Uri source)
        {
            var userPass = source.UserInfo.Split(new[] { ':' }, 2);
            var userWorker = userPass[0].Split(new[] { '.' }, 2);
            var user = userWorker[0];
            var worker = userWorker.Length > 1 ? userWorker[1] : null;
            var pass = userPass.Length > 1 ? userPass[1] : null;
            return (user, worker, pass);
        }
    }
}
