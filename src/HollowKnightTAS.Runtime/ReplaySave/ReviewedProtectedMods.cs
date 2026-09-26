using System;

namespace HollowKnightTAS.Runtime.ReplaySave
{
    /// <summary>Exact external binaries reviewed for use with the shadow-save redirector.</summary>
    public static class ReviewedProtectedMods
    {
        // EnviousMarmu source 0821a1df: battle/FSM/death/localization only, no save or native IO.
        public const string EnviousMarmuSha256 = "3b55e3e198113fd00c8bc3b0bdbf9c4206b206c5cd94afb8abe8e79eec671673";

        public static bool Allows(string modName, string assemblySha256) =>
            string.Equals(modName, "EnviousMarmu", StringComparison.Ordinal)
            && string.Equals(assemblySha256, EnviousMarmuSha256, StringComparison.Ordinal);
    }
}
