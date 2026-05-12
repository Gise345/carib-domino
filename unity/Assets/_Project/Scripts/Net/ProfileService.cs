#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Firebase.Firestore;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Singleton MonoBehaviour that loads (or creates on first run) the user's
    /// profile document at <c>/users/{uid}</c>. Call <see cref="LoadOrCreate"/>
    /// after <see cref="FirebaseBootstrap"/> reports Ready. Survives scene
    /// loads via <c>DontDestroyOnLoad</c>.
    ///
    /// New-user path: generate a friendly display name from the uid via
    /// <see cref="NameGenerator"/>, capture the system locale, leave country
    /// code null (we don't have a geo-IP source yet — that's a later slice).
    /// Existing-user path: read the doc, touch <c>lastSeenAt</c> with a
    /// server-time write.
    /// </summary>
    public sealed class ProfileService : MonoBehaviour
    {
        public static ProfileService? Instance { get; private set; }

        public UserProfile? Profile { get; private set; }
        public bool IsReady { get; private set; }
        public bool HasFailed { get; private set; }
        public string? ErrorMessage { get; private set; }
        public bool IsNewProfile { get; private set; }

        public event Action? Ready;
        public event Action<string>? Failed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async void LoadOrCreate(string uid)
        {
            try
            {
                FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
                DocumentReference docRef = db.Collection("users").Document(uid);
                DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

                if (snapshot.Exists)
                {
                    Profile = snapshot.ConvertTo<UserProfile>();
                    IsNewProfile = false;
                    // Touch lastSeen with server time. Fire-and-forget — the
                    // in-memory Profile keeps the just-read value; the next
                    // session will read back the updated lastSeen.
                    await docRef.UpdateAsync("lastSeenAt", FieldValue.ServerTimestamp);
                    Debug.Log(
                        $"[ProfileService] Loaded existing profile for " +
                        $"\"{Profile.DisplayName}\" ({Profile.Locale})");
                }
                else
                {
                    string displayName = NameGenerator.GenerateFromUid(uid);
                    string locale = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;

                    // Build the write payload as a dictionary so we can use
                    // FieldValue.ServerTimestamp for createdAt/lastSeenAt
                    // (object-based writes would serialize whatever Timestamp
                    // value is on the object — fine for local accuracy, but
                    // server-side comparison is the right pattern).
                    Dictionary<string, object?> data = new()
                    {
                        ["uid"] = uid,
                        ["displayName"] = displayName,
                        ["locale"] = locale,
                        ["countryCode"] = null,
                        ["createdAt"] = FieldValue.ServerTimestamp,
                        ["lastSeenAt"] = FieldValue.ServerTimestamp,
                    };
                    await docRef.SetAsync(data);

                    // In-memory mirror — uses local Timestamp for the time
                    // fields, off by milliseconds from the server. Acceptable
                    // for the spike; M2.3 can read back if exact equality
                    // becomes important.
                    Profile = new UserProfile
                    {
                        Uid = uid,
                        DisplayName = displayName,
                        Locale = locale,
                        CountryCode = null,
                        CreatedAt = Timestamp.GetCurrentTimestamp(),
                        LastSeenAt = Timestamp.GetCurrentTimestamp(),
                    };
                    IsNewProfile = true;
                    Debug.Log(
                        $"[ProfileService] Created new profile for " +
                        $"\"{Profile.DisplayName}\" ({Profile.Locale})");
                }

                IsReady = true;
                Ready?.Invoke();
            }
            catch (Exception ex)
            {
                Fail($"Profile load/create failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Fail(string message)
        {
            HasFailed = true;
            ErrorMessage = message;
            Debug.LogError($"[ProfileService] {message}");
            Failed?.Invoke(message);
        }
    }
}
