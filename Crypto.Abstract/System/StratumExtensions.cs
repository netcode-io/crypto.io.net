using System.Collections.Generic;

namespace System
{
    /// <summary>
    /// ProtocolFamily
    /// </summary>
    public enum UriFamily
    {
        GetWork = 0,
        Stratum,
        Simulation
    }

    /// <summary>
    /// UriSecurity
    /// </summary>
    public enum UriSecurity
    {
        None = 0,
        Tls12,
        Tls
    }

    /// <summary>
    /// StratumExtensions
    /// </summary>
    public static class StratumExtensions
    {
        static readonly Dictionary<string, (UriFamily family, UriSecurity security, int version)> _schemes = new Dictionary<string, (UriFamily, UriSecurity, int)> {
            /*
            This schemes are kept for backwards compatibility.
            Ethminer do perform stratum autodetection
            */
            {"stratum+tcp", (UriFamily.Stratum, UriSecurity.None, 0)},
            {"stratum1+tcp", (UriFamily.Stratum, UriSecurity.None, 1)},
            {"stratum2+tcp", (UriFamily.Stratum, UriSecurity.None, 2)},
            {"stratum3+tcp", (UriFamily.Stratum, UriSecurity.None, 3)},
            {"stratum+tls", (UriFamily.Stratum, UriSecurity.Tls, 0)},
            {"stratum1+tls", (UriFamily.Stratum, UriSecurity.Tls, 1)},
            {"stratum2+tls", (UriFamily.Stratum, UriSecurity.Tls, 2)},
            {"stratum3+tls", (UriFamily.Stratum, UriSecurity.Tls, 3)},
            {"stratum+tls12", (UriFamily.Stratum, UriSecurity.Tls12, 0)},
            {"stratum1+tls12", (UriFamily.Stratum, UriSecurity.Tls12, 1)},
            {"stratum2+tls12", (UriFamily.Stratum, UriSecurity.Tls12, 2)},
            {"stratum3+tls12", (UriFamily.Stratum, UriSecurity.Tls12, 3)},
            {"stratum+ssl", (UriFamily.Stratum, UriSecurity.Tls12, 0)},
            {"stratum1+ssl", (UriFamily.Stratum, UriSecurity.Tls12, 1)},
            {"stratum2+ssl", (UriFamily.Stratum, UriSecurity.Tls12, 2)},
            {"stratum3+ssl", (UriFamily.Stratum, UriSecurity.Tls12, 3)},
            {"http", (UriFamily.GetWork, UriSecurity.None, 0)},
            {"getwork", (UriFamily.GetWork, UriSecurity.None, 0)},

            /*
            Any TCP scheme has, at the moment, only STRATUM protocol thus reiterating "stratum" word would be pleonastic
            Version 9 means auto-detect stratum mode
            */
            {"stratum", (UriFamily.Stratum, UriSecurity.None, 999)},
            {"stratums", (UriFamily.Stratum, UriSecurity.Tls, 999)},
            {"stratumss", (UriFamily.Stratum, UriSecurity.Tls12, 999)},

            /*
            The following scheme is only meant for simulation operations
            It's not meant to be used with -P arguments
            */
            {"simulation", (UriFamily.Simulation, UriSecurity.None, 999)}
        };

        class UriState
        {
            public int StratumMode;
            public bool StratumModeConfirmed;
            public bool Unrecoverable;
            public double TotalDuration;
        }

        static UriState State(this Uri source, Action<UriState> action = null) => new UriState();

        public static void SetStratumMode(this Uri source, int mode, bool confirmed) =>
            State(source, s =>
            {
                s.StratumMode = mode;
                s.StratumModeConfirmed = confirmed;
            });
        public static void SetStratumMode(this Uri source, int mode) => State(source).StratumMode = mode;
        public static int StratumMode(this Uri source) => State(source).StratumMode;
        public static bool StratumModeConfirmed(this Uri source) => State(source).StratumModeConfirmed;
        public static bool IsUnrecoverable(this Uri source) => State(source).Unrecoverable;
        public static void MarkUnrecoverable(this Uri source) => State(source).Unrecoverable = true;
        public static UriFamily Family(this Uri source) => _schemes[source.Scheme].family;
        public static UriSecurity Security(this Uri source) => _schemes[source.Scheme].security;
        public static int Version(this Uri source) => _schemes[source.Scheme].version;

        public static void AddDuration(this Uri source, double minutes) => State(source, s => s.TotalDuration += minutes);
        public static double Duration(this Uri source) => State(source).TotalDuration;
    }
}
