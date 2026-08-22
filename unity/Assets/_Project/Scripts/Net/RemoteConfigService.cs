#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.RemoteConfig;
using Pose.Core.Config;
using UnityEngine;

namespace Pose.Net
{
    /// <summary>
    /// Reads Firebase Remote Config so flags, a maintenance kill-switch, a
    /// force-update gate and client URLs can change without a store build (M7 OTA,
    /// ADR 0021). Applies in-app defaults from <see cref="RemoteConfigKeys"/>, then
    /// fetches + activates at launch. Getters fall back to those defaults before the
    /// fetch completes (or if it fails offline). Created by
    /// <see cref="FirebaseBootstrap"/> after Firebase dependencies resolve.
    /// </summary>
    public sealed class RemoteConfigService : MonoBehaviour
    {
        // How long a fetched config is treated as fresh. Shorter = faster
        // propagation but more network + Firebase throttling risk; 1h is the launch
        // default. A running app picks up changes on its next cold start past this.
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);

        public static RemoteConfigService? Instance { get; private set; }

        /// <summary>True once a fetch+activate cycle has completed (values may still be defaults).</summary>
        public bool IsReady { get; private set; }

        /// <summary>Fires after activate, so UI can re-read values that changed.</summary>
        public event Action? Updated;

        private FirebaseRemoteConfig? _rc;

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

        /// <summary>
        /// Applies in-app defaults, then fetches + activates remote values. Safe to
        /// fire-and-forget — failures degrade to defaults/cache and are logged, never
        /// thrown. Await it (with a timeout) once launch decisions depend on it
        /// (kill-switch / force-update gating).
        /// </summary>
        public async Task InitAsync()
        {
            _rc = FirebaseRemoteConfig.DefaultInstance;

            Dictionary<string, object> defaults = new();
            foreach (KeyValuePair<string, object> kv in RemoteConfigKeys.Defaults)
            {
                defaults[kv.Key] = kv.Value;
            }
            await _rc.SetDefaultsAsync(defaults);

            try
            {
                await _rc.FetchAsync(CacheExpiry);
                await _rc.ActivateAsync();
            }
            catch (Exception ex)
            {
                // Offline or throttled — keep the last activated values (or defaults).
                Debug.LogWarning($"[RemoteConfigService] fetch failed, using cached/defaults: {ex.Message}");
            }

            IsReady = true;
            Updated?.Invoke();
        }

        public bool GetBool(string key) =>
            _rc != null ? _rc.GetValue(key).BooleanValue : DefaultBool(key);

        public long GetLong(string key) =>
            _rc != null ? _rc.GetValue(key).LongValue : DefaultLong(key);

        public int GetInt(string key) => (int)GetLong(key);

        public string GetString(string key) =>
            _rc != null ? _rc.GetValue(key).StringValue : DefaultString(key);

        private static bool DefaultBool(string key) =>
            RemoteConfigKeys.Defaults.TryGetValue(key, out object value) && value is bool b && b;

        private static long DefaultLong(string key) =>
            RemoteConfigKeys.Defaults.TryGetValue(key, out object value) && value is long l ? l : 0L;

        private static string DefaultString(string key) =>
            RemoteConfigKeys.Defaults.TryGetValue(key, out object value) && value is string s ? s : string.Empty;
    }
}
