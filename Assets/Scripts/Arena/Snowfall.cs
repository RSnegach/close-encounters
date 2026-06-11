using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Driving snowfall built from a code-configured ParticleSystem: a wide curtain of
    /// slow, wind-drifted flakes emitted high above the play area. Follows the active
    /// camera so snow surrounds the player everywhere. No colliders — pure ambiance.
    /// Build with Snowfall.Create().
    /// </summary>
    public class Snowfall : MonoBehaviour
    {
        private float _height;

        public static Snowfall Create(Transform parent, float emitHeight, float areaSize, float rate,
            Vector3 wind, Color? color = null)
        {
            var go = new GameObject("Snowfall");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, emitHeight, 0f);
            var s = go.AddComponent<Snowfall>();
            s._height = emitHeight;
            s.Build(areaSize, rate, wind, color ?? new Color(1f, 1f, 1f, 0.85f));
            return s;
        }

        private void Build(float areaSize, float rate, Vector3 wind, Color color)
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.startLifetime = 14f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startColor = color;
            main.gravityModifier = 0.05f;          // slow, floaty descent
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(areaSize, 1f, areaSize);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x);
            vel.y = new ParticleSystem.MinMaxCurve(-4f);   // downward drift
            vel.z = new ParticleSystem.MinMaxCurve(wind.z);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-30f * Mathf.Deg2Rad, 30f * Mathf.Deg2Rad);

            var pr = GetComponent<ParticleSystemRenderer>();
            if (pr != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    Color tint = new Color(color.r, color.g, color.b, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    mat.color = tint;
                    pr.material = mat;
                }
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.alignment = ParticleSystemRenderSpace.View;
            }

            ps.Play();
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            transform.position = new Vector3(p.x, _height, p.z);
        }
    }
}
