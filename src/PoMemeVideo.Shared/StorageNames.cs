namespace PoMemeVideo.Shared;

/// <summary>
/// Canonical Azure Storage table and container names. Single source of truth so a rename cannot
/// silently diverge between a repository, an admin wipe, a health probe and the seeding scripts.
/// </summary>
public static class StorageNames
{
    public static class Tables
    {
        public const string UserIdentities = "UserIdentities";
        public const string VideoSessions = "VideoSessions";
        public const string SoundAssets = "SoundAssets";
        public const string DirectorScripts = "DirectorScripts";

        /// <summary>Probe-only table; never written to.</summary>
        public const string HealthCheck = "HealthCheck";
    }

    public static class Containers
    {
        public const string Sessions = "sessions";
        public const string Sounds = "sounds";
        public const string DataProtection = "dataprotection";
    }
}
