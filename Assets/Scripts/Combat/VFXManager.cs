using UnityEngine;

namespace CloseEncounters.Combat
{
    /// <summary>
    /// Singleton VFX manager. Loads ParticlePack prefabs from Resources/VFX/
    /// and provides static methods to spawn effects at world positions.
    /// All effects auto-destroy after their particle systems finish.
    /// </summary>
    public class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        // ── Cached prefabs ──
        // Cinematic Explosions (Mirza Beig pack) -- used for all explosion effects
        private GameObject _cinematicExplosion1;
        private GameObject _cinematicExplosion2;
        private GameObject _cinematicExplosion3;
        private GameObject _cinematicExplosion4;
        private GameObject _cinematicSmoke;

        // Legacy explosions (fallback if cinematic pack missing)
        private GameObject _bigExplosion;
        private GameObject _smallExplosion;
        private GameObject _tinyExplosion;
        private GameObject _dustExplosion;
        private GameObject _energyExplosion;
        private GameObject _fireBall;
        private GameObject _plasmaExplosion;

        // Fire
        private GameObject _largeFlames;
        private GameObject _mediumFlames;
        private GameObject _tinyFlames;
        private GameObject _wildFire;

        // Weapons
        private GameObject _muzzleFlash;
        private GameObject _metalImpact;
        private GameObject _stoneImpact;
        private GameObject _sandImpact;
        private GameObject _woodImpact;
        private GameObject _electricalSparks;
        private GameObject _sparks;

        // Smoke
        private GameObject _smoke;
        private GameObject _rocketTrail;
        private GameObject _steam;
        private GameObject _pressureSteam;

        // Water
        private GameObject _bigSplash;
        private GameObject _waterLeak;

        // Ambient
        private GameObject _groundFog;
        private GameObject _dustStorm;
        private GameObject _rain;
        private GameObject _fireFlies;
        private GameObject _dustMotes;
        private GameObject _sandSwirls;
        private GameObject _heatDistortion;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadAll();
        }

        private void LoadAll()
        {
            // Cinematic Explosions (primary)
            _cinematicExplosion1 = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicExplosion_1");
            _cinematicExplosion2 = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicExplosion_2");
            _cinematicExplosion3 = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicExplosion_3");
            _cinematicExplosion4 = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicExplosion_4");
            _cinematicSmoke      = Resources.Load<GameObject>("VFX/CinematicExplosions/CinematicSmoke");

            // Legacy fallbacks
            _bigExplosion     = Resources.Load<GameObject>("VFX/Explosions/BigExplosion");
            _smallExplosion   = Resources.Load<GameObject>("VFX/Explosions/SmallExplosion");
            _tinyExplosion    = Resources.Load<GameObject>("VFX/Explosions/TinyExplosion");
            _dustExplosion    = Resources.Load<GameObject>("VFX/Explosions/DustExplosion");
            _energyExplosion  = Resources.Load<GameObject>("VFX/Explosions/EnergyExplosion");
            _fireBall         = Resources.Load<GameObject>("VFX/Explosions/FireBall");
            _plasmaExplosion  = Resources.Load<GameObject>("VFX/Explosions/PlasmaExplosionEffect");

            _largeFlames      = Resources.Load<GameObject>("VFX/Fire/LargeFlames");
            _mediumFlames     = Resources.Load<GameObject>("VFX/Fire/MediumFlames");
            _tinyFlames       = Resources.Load<GameObject>("VFX/Fire/TinyFlames");
            _wildFire         = Resources.Load<GameObject>("VFX/Fire/WildFire");

            _muzzleFlash      = Resources.Load<GameObject>("VFX/Weapons/MuzzleFlash");
            _metalImpact      = Resources.Load<GameObject>("VFX/Weapons/MetalImpacts");
            _stoneImpact      = Resources.Load<GameObject>("VFX/Weapons/StoneImpacts");
            _sandImpact       = Resources.Load<GameObject>("VFX/Weapons/SandImpacts");
            _woodImpact       = Resources.Load<GameObject>("VFX/Weapons/WoodImpacts");
            _electricalSparks = Resources.Load<GameObject>("VFX/Weapons/ElectricalSparksEffect");
            _sparks           = Resources.Load<GameObject>("VFX/Weapons/SparksEffect");

            _smoke            = Resources.Load<GameObject>("VFX/Smoke/SmokeEffect");
            _rocketTrail      = Resources.Load<GameObject>("VFX/Smoke/RocketTrail");
            _steam            = Resources.Load<GameObject>("VFX/Smoke/Steam");
            _pressureSteam    = Resources.Load<GameObject>("VFX/Smoke/PressurisedSteam");

            _bigSplash        = Resources.Load<GameObject>("VFX/Water/BigSplash");
            _waterLeak        = Resources.Load<GameObject>("VFX/Water/WaterLeak");

            _groundFog        = Resources.Load<GameObject>("VFX/Smoke/GroundFog");
            _dustStorm        = Resources.Load<GameObject>("VFX/Smoke/DustStorm");
            _rain             = Resources.Load<GameObject>("VFX/Ambient/RainEffect");
            _fireFlies        = Resources.Load<GameObject>("VFX/Ambient/FireFlies");
            _dustMotes        = Resources.Load<GameObject>("VFX/Ambient/DustMotesEffect");
            _sandSwirls       = Resources.Load<GameObject>("VFX/Ambient/SandSwirlsEffect");
            _heatDistortion   = Resources.Load<GameObject>("VFX/Ambient/HeatDistortion");

            int loaded = 0;
            var fields = GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].FieldType == typeof(GameObject) && fields[i].GetValue(this) != null)
                    loaded++;
            Debug.Log($"[VFXManager] Loaded {loaded} VFX prefabs.");
        }

        // =================================================================
        // Spawn helpers — instantiate, auto-destroy
        // =================================================================

        private static GameObject Spawn(GameObject prefab, Vector3 pos, float scale = 1f, float lifetime = 5f)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (scale != 1f) go.transform.localScale = Vector3.one * scale;
            Destroy(go, lifetime);
            return go;
        }

        private static GameObject SpawnAttached(GameObject prefab, Transform parent, Vector3 localPos, float scale = 1f)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent);
            go.transform.localPosition = localPos;
            if (scale != 1f) go.transform.localScale = Vector3.one * scale;
            return go; // no auto-destroy — dies with parent
        }

        // =================================================================
        // Public API — Explosions
        // =================================================================

        /// <summary>Pick a random cinematic explosion prefab.</summary>
        private GameObject RandomCinematicExplosion()
        {
            GameObject[] pool = { _cinematicExplosion1, _cinematicExplosion2,
                                  _cinematicExplosion3, _cinematicExplosion4 };
            // Filter out nulls
            var valid = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null) valid.Add(pool[i]);
            return valid.Count > 0 ? valid[UnityEngine.Random.Range(0, valid.Count)] : null;
        }

        /// <summary>Big explosion for vehicle death (cinematic).</summary>
        public static void BigExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._bigExplosion;
            Spawn(prefab, pos, scale * 1.5f, 6f);
            // Also spawn cinematic smoke
            if (Instance._cinematicSmoke != null)
                Spawn(Instance._cinematicSmoke, pos + Vector3.up, scale, 8f);
            AudioFX.Play("Audio/Explosions/BigExplosion", pos, 0.9f, Random.Range(0.9f, 1.1f));
        }

        /// <summary>Small explosion for part destruction (cinematic).</summary>
        public static void SmallExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._smallExplosion;
            Spawn(prefab, pos, scale * 0.6f, 4f);
            AudioFX.Play("Audio/Explosions/SmallExplosion", pos, 0.7f, Random.Range(0.95f, 1.15f));
        }

        /// <summary>Tiny explosion for projectile impact (cinematic).</summary>
        public static void TinyExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._tinyExplosion;
            Spawn(prefab, pos, scale * 0.3f, 3f);
            AudioFX.Play("Audio/Explosions/TinyExplosion", pos, 0.5f, Random.Range(1.0f, 1.3f));
        }

        /// <summary>Dust puff for ground impact (cinematic).</summary>
        public static void DustExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance._cinematicSmoke ?? Instance._dustExplosion;
            Spawn(prefab, pos, scale * 0.5f, 4f);
        }

        /// <summary>Energy/plasma explosion for railgun/laser (cinematic).</summary>
        public static void EnergyExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._energyExplosion;
            Spawn(prefab, pos, scale * 0.5f, 4f);
        }

        /// <summary>Plasma explosion for railgun impacts (cinematic).</summary>
        public static void PlasmaExplosion(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._plasmaExplosion;
            Spawn(prefab, pos, scale * 0.7f, 4f);
        }

        /// <summary>Fireball for fuel tank detonation (cinematic).</summary>
        public static void FireBall(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            var prefab = Instance.RandomCinematicExplosion() ?? Instance._fireBall;
            Spawn(prefab, pos, scale * 1.2f, 5f);
        }

        // =================================================================
        // Public API — Fire (persistent)
        // =================================================================

        /// <summary>Large fire at vehicle death site.</summary>
        public static GameObject LargeFlames(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._largeFlames, pos, scale, 12f);
        }

        /// <summary>Medium fire for damaged vehicle.</summary>
        public static GameObject MediumFlames(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._mediumFlames, pos, scale, 10f);
        }

        /// <summary>Small fire on destroyed part.</summary>
        public static GameObject TinyFlames(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._tinyFlames, pos, scale, 8f);
        }

        // =================================================================
        // Public API — Weapon effects
        // =================================================================

        /// <summary>Muzzle flash at weapon fire point.</summary>
        public static void MuzzleFlash(Vector3 pos, Vector3 forward, float scale = 1f)
        {
            if (Instance == null || Instance._muzzleFlash == null) return;
            var go = Instantiate(Instance._muzzleFlash, pos, Quaternion.LookRotation(forward));
            if (scale != 1f) go.transform.localScale = Vector3.one * scale;
            Destroy(go, 1f);
            AudioFX.Play("Audio/Weapons/Fire", pos, 0.6f, Random.Range(0.95f, 1.1f));
        }

        /// <summary>Metal sparks impact on vehicle.</summary>
        public static void MetalImpact(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._metalImpact, pos, scale, 2f);
        }

        /// <summary>Stone/terrain impact.</summary>
        public static void StoneImpact(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._stoneImpact, pos, scale, 2f);
        }

        /// <summary>Sand impact for desert arenas.</summary>
        public static void SandImpact(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._sandImpact, pos, scale, 2f);
        }

        /// <summary>Wood impact for boat hits.</summary>
        public static void WoodImpact(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._woodImpact, pos, scale, 2f);
        }

        /// <summary>Electrical sparks for laser hit.</summary>
        public static void ElectricalSparks(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._electricalSparks, pos, scale, 2f);
        }

        /// <summary>Generic sparks.</summary>
        public static void Sparks(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._sparks, pos, scale, 2f);
        }

        // =================================================================
        // Public API — Smoke/trails
        // =================================================================

        /// <summary>Attach a smoke trail to a damaged vehicle.</summary>
        public static GameObject AttachSmoke(Transform parent, float scale = 1f)
        {
            if (Instance == null) return null;
            return SpawnAttached(Instance._smoke, parent, Vector3.up * 1.5f, scale);
        }

        /// <summary>Attach a rocket/missile trail to a projectile.</summary>
        public static GameObject AttachRocketTrail(Transform parent, float scale = 0.5f)
        {
            if (Instance == null) return null;
            return SpawnAttached(Instance._rocketTrail, parent, Vector3.zero, scale);
        }

        /// <summary>Boost steam effect attached to vehicle.</summary>
        public static GameObject AttachBoostSteam(Transform parent, float scale = 0.5f)
        {
            if (Instance == null) return null;
            return SpawnAttached(Instance._pressureSteam, parent, -Vector3.forward * 2f, scale);
        }

        // =================================================================
        // Public API — Water effects
        // =================================================================

        /// <summary>Big water splash.</summary>
        public static void BigSplash(Vector3 pos, float scale = 1f)
        {
            if (Instance == null) return;
            Spawn(Instance._bigSplash, pos, scale, 4f);
        }

        /// <summary>Water leak on damaged boat.</summary>
        public static GameObject AttachWaterLeak(Transform parent, float scale = 1f)
        {
            if (Instance == null) return null;
            return SpawnAttached(Instance._waterLeak, parent, Vector3.down * 0.5f, scale);
        }

        // =================================================================
        // Public API — Ambient (long-lived, attached to arena)
        // =================================================================

        /// <summary>Ground fog covering an area.</summary>
        public static GameObject GroundFog(Vector3 pos, float scale = 5f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._groundFog, pos, scale, 999f);
        }

        /// <summary>Desert dust storm.</summary>
        public static GameObject DustStorm(Vector3 pos, float scale = 5f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._dustStorm, pos, scale, 999f);
        }

        /// <summary>Rain effect.</summary>
        public static GameObject Rain(Vector3 pos, float scale = 3f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._rain, pos, scale, 999f);
        }

        /// <summary>Fireflies for tropical/forest arenas.</summary>
        public static GameObject Fireflies(Vector3 pos, float scale = 3f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._fireFlies, pos, scale, 999f);
        }

        /// <summary>Dust motes for indoor/cave areas.</summary>
        public static GameObject DustMotes(Vector3 pos, float scale = 3f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._dustMotes, pos, scale, 999f);
        }

        /// <summary>Sand swirls for desert arenas.</summary>
        public static GameObject SandSwirls(Vector3 pos, float scale = 3f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._sandSwirls, pos, scale, 999f);
        }

        /// <summary>Heat distortion for volcanic arenas.</summary>
        public static GameObject HeatDistortion(Vector3 pos, float scale = 3f)
        {
            if (Instance == null) return null;
            return Spawn(Instance._heatDistortion, pos, scale, 999f);
        }
    }
}
