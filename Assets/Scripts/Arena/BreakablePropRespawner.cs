using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Scene-wide tracker that re-spawns broken props at their original transform
    /// after a cooldown. Holds an inactive cloned "template" per record so we
    /// don't need asset-level prefab references.
    /// </summary>
    public class BreakablePropRespawner : MonoBehaviour
    {
        private const int MaxPendingRecords = 500;

        // Access via BreakableProp.Initialize — no singleton magic beyond this.
        private static BreakablePropRespawner _instance;
        public static BreakablePropRespawner GetOrCreate()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[BreakablePropRespawner]");
            _instance = go.AddComponent<BreakablePropRespawner>();
            return _instance;
        }

        private struct Record
        {
            public int id;
            public GameObject template;     // inactive clone under the respawner
            public Transform originalParent; // may become null if arena unloads
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public float respawnAt;         // Time.time when eligible; 0 = not yet scheduled
        }

        private readonly List<Record> _records = new List<Record>(256);
        private int _nextId = 1;
        private bool _shuttingDown;

        private void Awake()
        {
            _instance = this;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            _shuttingDown = true;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (_instance == this) _instance = null;
            // Templates are children of this GO, Unity destroys them with us.
            _records.Clear();
        }

        private void OnSceneUnloaded(Scene s)
        {
            // Cancel all pending respawns — the arena they belong to is gone.
            _records.Clear();
        }

        /// <summary>
        /// Register a freshly-placed prop. Captures its current transform + a
        /// clone template so we can recreate an equivalent prop later.
        /// Returns a record ID the prop should hand back on BreakFree.
        /// </summary>
        public int Register(GameObject instance)
        {
            if (_shuttingDown || instance == null) return 0;

            // Evict oldest if we're over the cap (dropped props won't respawn).
            if (_records.Count >= MaxPendingRecords)
            {
                var dead = _records[0];
                if (dead.template != null) Object.Destroy(dead.template);
                _records.RemoveAt(0);
            }

            // Clone BEFORE any breakable state is mutated. Inactive parent the
            // clone under us so nothing ticks on it.
            var template = Object.Instantiate(instance, transform);
            template.name = instance.name + "__template";
            template.SetActive(false);
            StripRuntimeOnlyComponents(template);

            var rec = new Record
            {
                id = _nextId++,
                template = template,
                originalParent = instance.transform.parent,
                localPosition = instance.transform.localPosition,
                localRotation = instance.transform.localRotation,
                localScale = instance.transform.localScale,
                respawnAt = 0f,
            };
            _records.Add(rec);
            return rec.id;
        }

        /// <summary>
        /// Schedule a respawn for a previously-registered prop. Called by
        /// BreakableProp when it breaks free. cooldown measured from now.
        /// </summary>
        public void ScheduleRespawn(int recordId, float cooldown)
        {
            if (_shuttingDown || recordId <= 0) return;

            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].id != recordId) continue;
                var rec = _records[i];
                if (rec.respawnAt > 0f) return; // already scheduled
                rec.respawnAt = Time.time + cooldown;
                _records[i] = rec;
                return;
            }
        }

        private void Update()
        {
            if (_shuttingDown || _records.Count == 0) return;

            float now = Time.time;
            // Iterate backwards so we can swap-remove without shifting.
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                var rec = _records[i];
                if (rec.respawnAt <= 0f || now < rec.respawnAt) continue;

                // Parent might have been destroyed with its arena. Skip silently.
                if (rec.originalParent == null || rec.template == null)
                {
                    SwapRemoveAt(i);
                    continue;
                }

                Respawn(ref rec);
                SwapRemoveAt(i);
            }
        }

        private void Respawn(ref Record rec)
        {
            // Activate the template briefly, clone it into the arena, then
            // deactivate template again so it stays dormant... wait, template
            // is destroyed once used — it's a one-shot.
            rec.template.SetActive(true);
            var fresh = Object.Instantiate(rec.template, rec.originalParent);
            rec.template.SetActive(false);
            Object.Destroy(rec.template);

            fresh.name = fresh.name.Replace("(Clone)", "").Replace("__template", "");
            var t = fresh.transform;
            t.localPosition = rec.localPosition;
            t.localRotation = rec.localRotation;
            t.localScale = rec.localScale;

            // Ensure the fresh copy has working kinematic Rigidbody + BreakableProp.
            var rb = fresh.GetComponent<Rigidbody>();
            if (rb == null) rb = fresh.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            var bp = fresh.GetComponent<BreakableProp>();
            if (bp == null) bp = fresh.AddComponent<BreakableProp>();

            // Re-register so the new one can break and respawn again.
            int newId = Register(fresh);
            bp.AttachRespawner(this, newId);
        }

        private void SwapRemoveAt(int i)
        {
            int last = _records.Count - 1;
            if (i != last) _records[i] = _records[last];
            _records.RemoveAt(last);
        }

        // Templates shouldn't run game logic while dormant. We disable the
        // GameObject (SetActive(false)) which halts MonoBehaviours; strip the
        // few things that could still cause trouble if someone re-enables.
        private static void StripRuntimeOnlyComponents(GameObject template)
        {
            var bp = template.GetComponent<BreakableProp>();
            if (bp != null) Object.Destroy(bp);
            var rb = template.GetComponent<Rigidbody>();
            if (rb != null) Object.Destroy(rb);
        }
    }
}
