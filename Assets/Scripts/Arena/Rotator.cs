using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Continuously rotates a transform about a local axis at a fixed rate.
    /// Used for ambient motion (windmill blades, fans, signage). Self-contained.
    /// </summary>
    public class Rotator : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 30f;

        private void Update()
        {
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
