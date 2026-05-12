#nullable enable
using Firebase.Firestore;

namespace Pose.Net
{
    /// <summary>
    /// Cloud-Firestore-backed user profile. Stored at <c>/users/{uid}</c>;
    /// client may read and write its own document only (enforced by
    /// firestore.rules). Wallet, ELO, stats, and entitlement data are NOT on
    /// this document — those live in separate Cloud-Function-only collections
    /// per the trust model in <c>docs/ARCHITECTURE.md §4</c>.
    /// </summary>
    [FirestoreData]
    public sealed class UserProfile
    {
        [FirestoreProperty("uid")]
        public string Uid { get; set; } = string.Empty;

        [FirestoreProperty("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [FirestoreProperty("locale")]
        public string Locale { get; set; } = "en";

        [FirestoreProperty("countryCode")]
        public string? CountryCode { get; set; }

        [FirestoreProperty("createdAt")]
        public Timestamp CreatedAt { get; set; }

        [FirestoreProperty("lastSeenAt")]
        public Timestamp LastSeenAt { get; set; }
    }
}
