#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Pose.Net
{
    /// <summary>
    /// Downloads remote avatar images (Facebook profile pictures) and turns them
    /// into sprites, cached by URL so each image is fetched at most once and shared
    /// across the profile card, leaderboard and friends list (M7). Not Addressables
    /// — these are per-user remote images resolved at runtime, not shipped assets.
    /// </summary>
    public static class AvatarLoader
    {
        private static readonly Dictionary<string, Sprite> Cache = new();
        private static readonly Dictionary<string, Task<Sprite?>> InFlight = new();

        /// <summary>
        /// Returns the avatar sprite for <paramref name="url"/>, or null if the url
        /// is empty or the download fails. Cached; concurrent requests for the same
        /// url share a single download.
        /// </summary>
        public static Task<Sprite?> LoadAsync(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return Task.FromResult<Sprite?>(null);
            }
            if (Cache.TryGetValue(url, out Sprite cached))
            {
                return Task.FromResult<Sprite?>(cached);
            }
            if (InFlight.TryGetValue(url, out Task<Sprite?> pending))
            {
                return pending;
            }
            Task<Sprite?> task = DownloadAsync(url);
            InFlight[url] = task;
            return task;
        }

        private static async Task<Sprite?> DownloadAsync(string url)
        {
            try
            {
                using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                TaskCompletionSource<bool> tcs = new();
                op.completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[AvatarLoader] failed to load {url}: {request.error}");
                    return null;
                }

                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                Cache[url] = sprite;
                return sprite;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AvatarLoader] error loading {url}: {ex.Message}");
                return null;
            }
            finally
            {
                InFlight.Remove(url);
            }
        }
    }
}
