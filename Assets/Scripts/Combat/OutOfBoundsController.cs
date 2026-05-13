using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Monitors an air-domain vehicle's position against the arena's invisible
    /// wall bounds and fires a countdown when it strays outside. If the vehicle
    /// fails to return before the timeout expires, raises
    /// <see cref="onOutOfBoundsExpired"/> so the owning Vehicle can self-destruct.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutOfBoundsController : MonoBehaviour
    {
        // --- Inspector tuning ---
        [Tooltip("Seconds allowed outside arena bounds before onOutOfBoundsExpired fires.")]
        public float returnTimeout = 10f;
        [Tooltip("Seconds between bounds checks. 10Hz is plenty for a 10s timer.")]
        public float checkInterval = 0.1f;
        [Tooltip("Reset the timer when the vehicle re-enters bounds (otherwise cancel without reset).")]
        public bool resetOnReturn = true;

        // --- Public state ---
        public bool IsOutOfBounds { get; private set; }
        public float TimeRemaining { get; private set; }
        public bool HasBounds => _hasBounds;

        // --- Public events ---
        public event Action onOutOfBoundsEntered;
        public event Action onOutOfBoundsCleared;
        public event Action onOutOfBoundsExpired;

        // --- Cached bounds (world-space AABB from arena invisible walls) ---
        private Bounds _worldBounds;
        private bool _hasBounds;
        private float _nextCheckTime;
        private float _nextRecacheAttempt;

        // why: defer bounds lookup if arena isn't built yet at Start
        private const float BoundsRetryInterval = 1f;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Start()
        {
            TimeRemaining = returnTimeout;
            TryCacheBounds();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _hasBounds = false;
            _nextRecacheAttempt = 0f;
            TryCacheBounds();
        }

        private void Update()
        {
            if (!_hasBounds)
            {
                if (Time.unscaledTime >= _nextRecacheAttempt)
                {
                    _nextRecacheAttempt = Time.unscaledTime + BoundsRetryInterval;
                    TryCacheBounds();
                }
                return;
            }

            if (Time.unscaledTime >= _nextCheckTime)
            {
                _nextCheckTime = Time.unscaledTime + checkInterval;
                EvaluateBounds();
            }

            if (IsOutOfBounds)
            {
                // why: Time.deltaTime respects timeScale so pause freezes the countdown
                TimeRemaining -= Time.deltaTime;
                if (TimeRemaining <= 0f)
                {
                    TimeRemaining = 0f;
                    IsOutOfBounds = false;
                    onOutOfBoundsExpired?.Invoke();
                    enabled = false;
                }
            }
        }

        private void EvaluateBounds()
        {
            bool outside = !_worldBounds.Contains(transform.position);
            if (outside && !IsOutOfBounds)
            {
                IsOutOfBounds = true;
                TimeRemaining = returnTimeout;
                onOutOfBoundsEntered?.Invoke();
            }
            else if (!outside && IsOutOfBounds)
            {
                IsOutOfBounds = false;
                if (resetOnReturn) TimeRemaining = returnTimeout;
                onOutOfBoundsCleared?.Invoke();
            }
        }

        private static readonly List<BoxCollider> s_wallBuffer = new List<BoxCollider>(16);

        private void TryCacheBounds()
        {
            s_wallBuffer.Clear();

            // why: ArenaBase names walls "ArenaWall_0..3"; some older code uses "InvisibleWall*"
            var allColliders = FindObjectsByType<BoxCollider>(FindObjectsSortMode.None);
            for (int i = 0; i < allColliders.Length; i++)
            {
                var c = allColliders[i];
                if (c == null) continue;
                string n = c.gameObject.name;
                if (n.StartsWith("ArenaWall", StringComparison.Ordinal) ||
                    n.StartsWith("InvisibleWall", StringComparison.Ordinal))
                {
                    s_wallBuffer.Add(c);
                }
            }

            if (s_wallBuffer.Count == 0)
            {
                _hasBounds = false;
                return;
            }

            Bounds b = s_wallBuffer[0].bounds;
            for (int i = 1; i < s_wallBuffer.Count; i++)
                b.Encapsulate(s_wallBuffer[i].bounds);

            // why: AABB including walls contains the arena floor extent; expand vertically
            // so that "above ceiling" is captured (walls are tall enough but we also clamp
            // floor to 0 so a vehicle that dips below ground still counts as in-bounds
            // horizontally — the height check becomes the true ceiling limit)
            Vector3 center = b.center;
            Vector3 size = b.size;
            _worldBounds = new Bounds(center, size);
            _hasBounds = true;
            s_wallBuffer.Clear();
        }

        /// <summary>Force a bounds re-cache (e.g. after an arena swap).</summary>
        public void RecacheBounds()
        {
            _hasBounds = false;
            _nextRecacheAttempt = 0f;
            TryCacheBounds();
        }
    }
}
