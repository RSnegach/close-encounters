using UnityEngine;

namespace CloseEncounters.VehiclePhysics
{
    /// <summary>
    /// Singleton wave height manager. All systems (buoyancy, water mesh, effects)
    /// query this for a consistent water surface height at any world position.
    /// Adapted from vlytsus/unity-3d-boat WaweManager.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }

        [Header("Primary Wave")]
        public float amplitude  = 1.2f;
        public float wavelength = 15f;
        public float speed      = 2.0f;

        [Header("Secondary Wave (cross-swell)")]
        public float amp2        = 0.6f;
        public float wavelength2 = 10f;
        public float speed2      = 1.5f;
        public float angle2      = 0.6f; // radians offset for Z-component

        [Header("Base")]
        [Tooltip("Y position of the still water surface.")]
        public float baseWaterY = 0f;

        private float _offset;
        private float _offset2;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            _offset  += speed  * Time.deltaTime;
            _offset2 += speed2 * Time.deltaTime;
        }

        /// <summary>
        /// Get the wave-displaced water surface height at a world XZ position.
        /// </summary>
        public float GetWaterHeight(float x, float z)
        {
            float h = baseWaterY;

            // Primary wave along X
            if (wavelength > 0.01f)
                h += amplitude * Mathf.Sin((x / wavelength) + _offset);

            // Secondary cross-swell along Z (gives more natural surface)
            if (wavelength2 > 0.01f)
                h += amp2 * Mathf.Sin((z / wavelength2) + _offset2 + angle2);

            return h;
        }

        /// <summary>
        /// Get the surface normal at a world XZ position (for tilting objects on waves).
        /// Computed via partial derivatives of the wave function.
        /// </summary>
        public Vector3 GetSurfaceNormal(float x, float z)
        {
            // dh/dx
            float dhdx = 0f;
            if (wavelength > 0.01f)
                dhdx = (amplitude / wavelength) * Mathf.Cos((x / wavelength) + _offset);

            // dh/dz
            float dhdz = 0f;
            if (wavelength2 > 0.01f)
                dhdz = (amp2 / wavelength2) * Mathf.Cos((z / wavelength2) + _offset2 + angle2);

            // Normal = cross product of tangent vectors (1,dhdx,0) and (0,dhdz,1)
            return new Vector3(-dhdx, 1f, -dhdz).normalized;
        }

        /// <summary>
        /// Convenience: get height at a Vector3 position.
        /// </summary>
        public float GetWaterHeight(Vector3 worldPos)
        {
            return GetWaterHeight(worldPos.x, worldPos.z);
        }
    }
}
