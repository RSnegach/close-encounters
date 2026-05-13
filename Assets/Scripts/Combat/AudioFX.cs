using UnityEngine;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// One-shot 3D audio utility. Loads clips from Resources/Audio/*, plays at a
    /// world position via a short-lived AudioSource GameObject, and silently
    /// no-ops if a clip is missing so gameplay doesn't break when audio assets
    /// haven't been imported yet.
    /// </summary>
    public static class AudioFX
    {
        private static readonly System.Collections.Generic.Dictionary<string, AudioClip> _cache
            = new System.Collections.Generic.Dictionary<string, AudioClip>();

        public static void Play(string resourcePath, Vector3 worldPos, float volume = 1f, float pitch = 1f, float spatial = 1f, float maxDistance = 120f)
        {
            var clip = Get(resourcePath);
            if (clip == null) return;
            var go = new GameObject("AudioFX_" + resourcePath);
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = spatial;
            src.volume = volume;
            src.pitch = pitch;
            src.minDistance = 5f;
            src.maxDistance = maxDistance;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        public static AudioClip Get(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (_cache.TryGetValue(resourcePath, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(resourcePath);
            _cache[resourcePath] = clip; // cache null too so repeated misses don't re-load
            return clip;
        }

        public static GameObject CreateAmbientLoop(Transform parent, string resourcePath, float volume = 0.4f)
        {
            var clip = Get(resourcePath);
            if (clip == null) return null;
            var go = new GameObject("AmbientLoop_" + resourcePath);
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = volume;
            src.playOnAwake = true;
            src.Play();
            return go;
        }
    }
}
