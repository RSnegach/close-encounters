using UnityEngine;
using UnityEngine.UI;
using CloseEncounters.Combat;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Health system for the dragon boss. Tracks HP, detects projectile hits
    /// via a solid BoxCollider (so projectile trigger colliders and raycasts
    /// register against it), displays a floating world-space healthbar,
    /// spawns hit VFX, and triggers death via DragonBoss when HP reaches zero.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class DragonHealth : MonoBehaviour
    {
        // =====================================================================
        // Configuration
        // =====================================================================

        [Header("Health")]
        [SerializeField] private int maxHP = 500;

        [Header("Healthbar")]
        [Tooltip("Vertical offset above the dragon's pivot for the healthbar canvas.")]
        [SerializeField] private float healthbarHeight = 8f;
        [Tooltip("World-space scale of the healthbar canvas.")]
        [SerializeField] private float healthbarScale = 0.02f;

        [Header("Hit Cooldown")]
        [Tooltip("Minimum seconds between registering hits from the same projectile.")]
        [SerializeField] private float hitCooldown = 0.05f;

        // =====================================================================
        // Runtime State
        // =====================================================================

        public int CurrentHP { get; private set; }
        public int MaxHP => maxHP;
        public bool IsDead { get; private set; }

        private float _lastHitTime = -1f;
        private BoxCollider _collider;

        // --- Healthbar refs ---
        private Canvas _healthCanvas;
        private RectTransform _fillRT;
        private Image _fillImage;
        private Text _hpText;

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void Awake()
        {
            CurrentHP = maxHP;
            IsDead = false;

            EnsureCollider();
            CreateHealthBar();
        }

        private void LateUpdate()
        {
            if (_healthCanvas == null) return;

            // Billboard: always face the main camera
            Camera cam = Camera.main;
            if (cam != null)
            {
                _healthCanvas.transform.rotation =
                    Quaternion.LookRotation(_healthCanvas.transform.position - cam.transform.position);
            }
        }

        // =====================================================================
        // Collider Setup
        // =====================================================================

        /// <summary>
        /// Ensures there is a solid (non-trigger) BoxCollider on this object
        /// so that projectile triggers and raycasts register hits.
        /// If one already exists and is a trigger, it is left alone and a
        /// second solid collider is added.
        /// </summary>
        private void EnsureCollider()
        {
            _collider = GetComponent<BoxCollider>();

            if (_collider != null && !_collider.isTrigger)
            {
                // Already have a solid BoxCollider -- good to go.
                return;
            }

            if (_collider != null && _collider.isTrigger)
            {
                // Existing collider is a trigger. Add a second solid one.
                _collider = gameObject.AddComponent<BoxCollider>();
            }

            // If no BoxCollider existed at all, RequireComponent adds one.
            // Make sure it is NOT a trigger.
            if (_collider == null)
                _collider = GetComponent<BoxCollider>();

            _collider.isTrigger = false;

            // Default generous size; override in Inspector or prefab.
            if (_collider.size == Vector3.one)
            {
                _collider.size = new Vector3(6f, 4f, 10f);
                _collider.center = Vector3.up * 2f;
            }
        }

        // =====================================================================
        // Floating Healthbar (World-Space Canvas)
        // =====================================================================

        private void CreateHealthBar()
        {
            // --- Canvas ---
            var canvasObj = new GameObject("DragonHealthCanvas");
            _healthCanvas = canvasObj.AddComponent<Canvas>();
            _healthCanvas.renderMode = RenderMode.WorldSpace;
            canvasObj.transform.SetParent(transform, false);
            canvasObj.transform.localPosition = Vector3.up * healthbarHeight;
            canvasObj.transform.localScale = Vector3.one * healthbarScale;

            // Size the canvas rect
            var canvasRT = canvasObj.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(300f, 40f);

            // --- Background bar (dark) ---
            var bgObj = new GameObject("Background", typeof(RectTransform));
            bgObj.transform.SetParent(canvasObj.transform, false);
            var bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);

            // --- Fill bar (green, shrinks from right) ---
            var fillObj = new GameObject("Fill", typeof(RectTransform));
            fillObj.transform.SetParent(canvasObj.transform, false);
            _fillRT = fillObj.GetComponent<RectTransform>();
            _fillRT.anchorMin = Vector2.zero;
            _fillRT.anchorMax = Vector2.one;
            _fillRT.offsetMin = Vector2.zero;
            _fillRT.offsetMax = Vector2.zero;
            _fillImage = fillObj.AddComponent<Image>();
            _fillImage.color = new Color(0.3f, 0.8f, 0.64f); // green

            // --- HP text overlay ---
            var textObj = new GameObject("HPText", typeof(RectTransform));
            textObj.transform.SetParent(canvasObj.transform, false);
            var textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            _hpText = textObj.AddComponent<Text>();
            _hpText.text = $"{maxHP} / {maxHP}";
            _hpText.fontSize = 18;
            _hpText.color = Color.white;
            _hpText.alignment = TextAnchor.MiddleCenter;
            _hpText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hpText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hpText.verticalOverflow = VerticalWrapMode.Overflow;

            // --- "DRAGON" label above bar ---
            var labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(canvasObj.transform, false);
            var labelRT = labelObj.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 1f);
            labelRT.anchorMax = new Vector2(1f, 1f);
            labelRT.pivot = new Vector2(0.5f, 0f);
            labelRT.anchoredPosition = new Vector2(0f, 4f);
            labelRT.sizeDelta = new Vector2(300f, 30f);
            var labelText = labelObj.AddComponent<Text>();
            labelText.text = "DRAGON";
            labelText.fontSize = 22;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = new Color(1f, 0.35f, 0.2f); // fiery orange-red
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>
        /// Refreshes the healthbar visuals to match current HP.
        /// </summary>
        private void RefreshHealthBar()
        {
            if (_fillRT == null) return;

            float pct = Mathf.Clamp01((float)CurrentHP / maxHP);

            // Shrink fill bar from the right
            _fillRT.anchorMax = new Vector2(pct, 1f);

            // Color ramp: green > 50%, yellow 25-50%, red < 25%
            if (pct > 0.5f)
                _fillImage.color = new Color(0.3f, 0.8f, 0.64f);   // green
            else if (pct > 0.25f)
                _fillImage.color = new Color(0.94f, 0.75f, 0.25f);  // yellow
            else
                _fillImage.color = new Color(0.91f, 0.27f, 0.38f);  // red

            // Update text
            if (_hpText != null)
                _hpText.text = $"{Mathf.Max(0, CurrentHP)} / {maxHP}";
        }

        // =====================================================================
        // Damage
        // =====================================================================

        /// <summary>
        /// Apply damage to the dragon. Clamps to zero, updates the healthbar,
        /// spawns hit VFX, and triggers death when HP is depleted.
        /// Can be called by any system (projectile collision, area damage, etc.).
        /// </summary>
        /// <param name="amount">Damage amount (positive integer).</param>
        /// <param name="hitPoint">World-space point of impact for VFX. If default,
        /// uses the dragon's position.</param>
        public void TakeDamage(int amount, Vector3 hitPoint = default)
        {
            if (IsDead) return;
            if (amount <= 0) return;

            CurrentHP -= amount;
            if (CurrentHP < 0) CurrentHP = 0;

            // VFX at impact point
            Vector3 fxPos = (hitPoint == default) ? transform.position : hitPoint;
            SpawnHitVFX(fxPos);

            RefreshHealthBar();

            // Forward damage to DragonBoss so it tracks HP and triggers death animation
            var boss = GetComponent<DragonBoss>();
            if (boss != null)
                boss.TakeDamage(amount);

            Debug.Log($"[DragonHealth] Took {amount} damage. HP: {CurrentHP}/{maxHP}");

            if (CurrentHP <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Overload without a hit point -- uses the dragon's center.
        /// </summary>
        public void TakeDamage(int amount)
        {
            TakeDamage(amount, transform.position);
        }

        // =====================================================================
        // Projectile Collision Detection
        // =====================================================================

        /// <summary>
        /// Detects projectile trigger colliders entering this solid collider.
        /// Projectile has a SphereCollider (trigger). This object has a solid
        /// BoxCollider, so OnTriggerEnter fires on the non-trigger side.
        /// Also catches projectiles via RaycastSweep which calls HandleHit
        /// when the ray hits this collider -- but since HandleHit doesn't know
        /// about DragonHealth, we rely on OnTriggerEnter for primary detection.
        /// For raycasts, we also listen via OnCollisionEnter as a fallback.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (IsDead) return;

            // Debounce rapid multi-hits
            if (Time.time - _lastHitTime < hitCooldown) return;

            Projectile proj = other.GetComponent<Projectile>();
            if (proj == null) proj = other.GetComponentInParent<Projectile>();
            if (proj == null) return;

            _lastHitTime = Time.time;
            TakeDamage(proj.damage, other.ClosestPoint(transform.position));

            // Destroy the projectile on hit so it doesn't pass through
            Destroy(proj.gameObject);
        }

        /// <summary>
        /// Fallback for non-trigger projectile collisions (e.g., if a
        /// projectile somehow has a solid collider).
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (IsDead) return;
            if (Time.time - _lastHitTime < hitCooldown) return;

            Projectile proj = collision.collider.GetComponent<Projectile>();
            if (proj == null) proj = collision.collider.GetComponentInParent<Projectile>();
            if (proj == null) return;

            Vector3 contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;

            _lastHitTime = Time.time;
            TakeDamage(proj.damage, contactPoint);

            Destroy(proj.gameObject);
        }

        // =====================================================================
        // Hit VFX
        // =====================================================================

        private void SpawnHitVFX(Vector3 position)
        {
            // Alternate between metal impact and sparks for variety
            if (Random.value > 0.5f)
                VFXManager.MetalImpact(position, 1.2f);
            else
                VFXManager.Sparks(position, 1.0f);
        }

        // =====================================================================
        // Death
        // =====================================================================

        private void Die()
        {
            if (IsDead) return;
            IsDead = true;

            Debug.Log("[DragonHealth] Dragon is dead!");

            // Force healthbar to zero
            if (_fillRT != null)
            {
                _fillRT.anchorMax = new Vector2(0f, 1f);
                _fillImage.color = new Color(0.91f, 0.27f, 0.38f);
            }
            if (_hpText != null)
                _hpText.text = $"0 / {maxHP}";

            // Notify DragonBoss (if present) to handle death animation and state
            var boss = GetComponent<MonoBehaviour>();
            SendMessage("OnDragonDeath", SendMessageOptions.DontRequireReceiver);

            // Also try the strongly-typed approach if DragonBoss exists in the assembly
            TryNotifyDragonBoss();

            // VFX: large explosion at death
            VFXManager.BigExplosion(transform.position, 3f);
            VFXManager.LargeFlames(transform.position, 2f);

            // Hide the healthbar after a short delay
            if (_healthCanvas != null)
                Destroy(_healthCanvas.gameObject, 3f);

            // Disable the collider so no more hits register
            if (_collider != null)
                _collider.enabled = false;
        }

        /// <summary>
        /// Attempts to find and call OnDragonDeath() on a DragonBoss component.
        /// Uses reflection-free SendMessage as primary mechanism; this method
        /// also tries GetComponent with the type name as a fallback so it works
        /// even if DragonBoss is added to the project later.
        /// </summary>
        private void TryNotifyDragonBoss()
        {
            // Look for a component named "DragonBoss" by type name.
            // This avoids a hard compile-time dependency on a class that
            // may not exist yet.
            var components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null || components[i] == this) continue;
                string typeName = components[i].GetType().Name;
                if (typeName == "DragonBoss")
                {
                    // Invoke OnDragonDeath via reflection
                    var method = components[i].GetType().GetMethod("OnDragonDeath");
                    if (method != null)
                    {
                        method.Invoke(components[i], null);
                        Debug.Log("[DragonHealth] DragonBoss.OnDragonDeath() invoked.");
                    }
                    return;
                }
            }

            Debug.LogWarning("[DragonHealth] No DragonBoss component found. " +
                "Death triggered but no handler received it.");
        }

        // =====================================================================
        // Public Utility
        // =====================================================================

        /// <summary>
        /// Heal the dragon by the given amount, clamped to maxHP.
        /// </summary>
        public void Heal(int amount)
        {
            if (IsDead) return;
            if (amount <= 0) return;

            CurrentHP = Mathf.Min(CurrentHP + amount, maxHP);
            RefreshHealthBar();
        }

        /// <summary>
        /// Reset HP to max and clear death state. Useful for phase transitions.
        /// </summary>
        public void ResetHealth()
        {
            CurrentHP = maxHP;
            IsDead = false;

            if (_collider != null)
                _collider.enabled = true;

            RefreshHealthBar();
        }

        /// <summary>
        /// Returns the current HP as a normalized value (0..1).
        /// </summary>
        public float GetHealthNormalized()
        {
            return Mathf.Clamp01((float)CurrentHP / maxHP);
        }
    }
}
