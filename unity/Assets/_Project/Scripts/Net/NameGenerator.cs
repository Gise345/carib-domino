#nullable enable

namespace Pose.Net
{
    /// <summary>
    /// Produces friendly adjective-noun display names like "Cunning Marlin" or
    /// "Lucky Domino" deterministically from a player's uid. Same uid always
    /// yields the same name, so logs and Firestore docs stay debuggable.
    /// 25 × 25 = 625 combinations — sparse enough for collisions to be rare in
    /// the M1-era player count, easy to add a numeric suffix later when a real
    /// rename UI lands.
    /// </summary>
    public static class NameGenerator
    {
        private static readonly string[] Adjectives =
        {
            "Brave", "Swift", "Clever", "Lucky", "Bold",
            "Wise", "Sharp", "Quick", "Witty", "Cunning",
            "Daring", "Sly", "Crafty", "Mighty", "Noble",
            "Loyal", "Steady", "Sunny", "Breezy", "Calm",
            "Spry", "Gallant", "Plucky", "Stout", "Hardy",
        };

        private static readonly string[] Nouns =
        {
            "Tiger", "Eagle", "Falcon", "Heron", "Hawk",
            "Iguana", "Mongoose", "Parrot", "Pelican", "Marlin",
            "Dolphin", "Reef", "Palm", "Sun", "Wave",
            "Star", "Domino", "Tile", "Pip", "Champion",
            "Hibiscus", "Coconut", "Mango", "Calypso", "Trade",
        };

        public static string GenerateFromUid(string uid)
        {
            uint h1 = StableHash(uid, 0x12345678u);
            uint h2 = StableHash(uid, 0x9ABCDEF0u);
            string adjective = Adjectives[h1 % (uint)Adjectives.Length];
            string noun = Nouns[h2 % (uint)Nouns.Length];
            return $"{adjective} {noun}";
        }

        // Salted FNV-style hash. Unsigned so we don't worry about negative
        // modulo. Same uid + same salt always yields the same hash.
        private static uint StableHash(string s, uint salt)
        {
            uint hash = salt;
            for (int i = 0; i < s.Length; i++)
            {
                hash = unchecked(hash * 31u + s[i]);
            }
            return hash;
        }
    }
}
