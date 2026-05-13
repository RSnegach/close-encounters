using System.Collections.Generic;
using UnityEngine;

namespace CloseEncounters.Arena
{
    // why: procedural set-dressing for Strait of Hormuz arena — no prefabs required
    public static class StraitOfHormuzDecor
    {
        // ----- material cache ---------------------------------------------------
        private static readonly Dictionary<int, Material> _matCache = new Dictionary<int, Material>(64);
        private static readonly Dictionary<int, Material> _emisCache = new Dictionary<int, Material>(32);
        private static Shader _lit;

        private static Shader Lit
        {
            get
            {
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
                return _lit;
            }
        }

        private static Material Mat(Color c)
        {
            int k = ColorKey(c, false);
            if (_matCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Lit) { color = c };
            _matCache[k] = m;
            return m;
        }

        private static Material Emissive(Color c, float intensity = 3f)
        {
            int k = ColorKey(c, true) ^ Mathf.RoundToInt(intensity * 31);
            if (_emisCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Lit) { color = c };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            _emisCache[k] = m;
            return m;
        }

        private static int ColorKey(Color c, bool emis)
        {
            int r = Mathf.RoundToInt(c.r * 255f);
            int g = Mathf.RoundToInt(c.g * 255f);
            int b = Mathf.RoundToInt(c.b * 255f);
            int a = Mathf.RoundToInt(c.a * 255f);
            return (r << 24) ^ (g << 16) ^ (b << 8) ^ a ^ (emis ? 0x7F000000 : 0);
        }

        // ----- primitive helper -------------------------------------------------
        private static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos,
            Vector3 localScale, Color color, bool keepCollider = false, bool emissive = false, float emisIntensity = 3f)
        {
            var go = GameObject.CreatePrimitive(t);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = emissive ? Emissive(color, emisIntensity) : Mat(color);
            if (!keepCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            go.isStatic = true;
            return go;
        }

        private static void MarkTree(GameObject root, bool staticFlag)
        {
            root.isStatic = staticFlag;
            var xs = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < xs.Length; i++) xs[i].gameObject.isStatic = staticFlag;
        }

        // ========================================================================
        // TANKER — ~80 long x 12 wide x 18 tall
        // ========================================================================
        public static GameObject AddTanker(Transform parent, Vector3 pos, float yRotation, float scale)
        {
            var root = new GameObject("Tanker");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
            root.transform.localScale = Vector3.one * scale;

            // hull (keep collider — vehicles should not phase through)
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 4, 0), new Vector3(12, 8, 80),
                new Color(0.22f, 0.23f, 0.25f), keepCollider: true);
            // waterline stripe
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 1.2f, 0), new Vector3(12.1f, 1.2f, 80.1f),
                new Color(0.55f, 0.12f, 0.10f));

            // stern superstructure — 3 stacked levels, whitish
            var white = new Color(0.92f, 0.92f, 0.88f);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 10, -28), new Vector3(10, 4, 10), white, true);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 13.5f, -28), new Vector3(8, 3, 8), white, true);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 16.5f, -28), new Vector3(6, 2.5f, 6), white, true);
            // bridge windows (dark stripe)
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 17.5f, -24.8f), new Vector3(5.5f, 1.0f, 0.2f),
                new Color(0.08f, 0.12f, 0.18f), emissive: true, emisIntensity: 0.6f);
            // antennas
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(-2, 20.5f, -28), new Vector3(0.15f, 2.5f, 0.15f), Color.gray);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(2, 20.5f, -28), new Vector3(0.15f, 2.5f, 0.15f), Color.gray);

            // deck cargo pipes — lengthwise cylinders on main deck
            for (int i = -2; i <= 2; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = new Vector3(i * 2.0f, 8.6f, 8);
                c.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                c.transform.localScale = new Vector3(0.6f, 25f, 0.6f); // length=50
                c.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.35f, 0.35f, 0.37f));
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.isStatic = true;
            }

            // amidships stacked containers (mixed colors)
            Color[] cContainers = { new Color(0.75f, 0.12f, 0.12f), new Color(0.12f, 0.35f, 0.75f),
                                    new Color(0.15f, 0.55f, 0.22f), new Color(0.85f, 0.75f, 0.15f) };
            int ci = 0;
            for (int row = -3; row <= 3; row++)
                for (int layer = 0; layer < 2; layer++)
                {
                    Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(-3.5f, 9.5f + layer * 2.8f, row * 3.1f),
                        new Vector3(2.8f, 2.6f, 2.8f), cContainers[ci++ % 4]);
                    Prim(PrimitiveType.Cube, root.transform,
                        new Vector3(3.5f, 9.5f + layer * 2.8f, row * 3.1f),
                        new Vector3(2.8f, 2.6f, 2.8f), cContainers[ci++ % 4]);
                }

            // mast + nav lights
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 12, 35), new Vector3(0.3f, 4, 0.3f), Color.gray);
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(-0.6f, 16, 35), new Vector3(0.4f, 0.4f, 0.4f),
                Color.red, emissive: true, emisIntensity: 5f);
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0.6f, 16, 35), new Vector3(0.4f, 0.4f, 0.4f),
                Color.green, emissive: true, emisIntensity: 5f);

            MarkTree(root, true);
            return root;
        }

        // ========================================================================
        // OIL RIG — 30 base, 40+ tall
        // ========================================================================
        public static GameObject AddOilRig(Transform parent, Vector3 pos, float scale)
        {
            var root = new GameObject("OilRig");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            root.transform.localScale = Vector3.one * scale;

            var legColor = new Color(0.45f, 0.35f, 0.18f);
            // 4 thick legs sunk into water (keep colliders)
            float[] lx = { -12, 12, -12, 12 };
            float[] lz = { -12, -12, 12, 12 };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, root.transform, new Vector3(lx[i], 6, lz[i]),
                    new Vector3(1.6f, 18, 1.6f), legColor, keepCollider: true);

            // platform base (keep collider)
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 20, 0), new Vector3(30, 1.2f, 30),
                new Color(0.30f, 0.28f, 0.25f), keepCollider: true);
            // mid deck
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 26, 0), new Vector3(22, 0.8f, 22),
                new Color(0.50f, 0.45f, 0.25f));
            // upper deck module (living quarters, yellowish)
            Prim(PrimitiveType.Cube, root.transform, new Vector3(-6, 30, -6), new Vector3(12, 6, 10),
                new Color(0.90f, 0.82f, 0.20f), keepCollider: true);

            // central derrick (lattice approximated as tall segmented block + cross braces)
            Prim(PrimitiveType.Cube, root.transform, new Vector3(6, 38, 6), new Vector3(4, 24, 4),
                new Color(0.55f, 0.45f, 0.15f));
            // segment separators
            for (int s = 0; s < 5; s++)
                Prim(PrimitiveType.Cube, root.transform, new Vector3(6, 28 + s * 5, 6), new Vector3(4.4f, 0.3f, 4.4f),
                    new Color(0.25f, 0.22f, 0.15f));

            // gas flare at top (emissive orange) + optional VFX
            var flareTip = new GameObject("FlareTip");
            flareTip.transform.SetParent(root.transform, false);
            flareTip.transform.localPosition = new Vector3(6, 52, 6);
            Prim(PrimitiveType.Sphere, flareTip.transform, Vector3.zero, new Vector3(1.6f, 2.0f, 1.6f),
                new Color(1f, 0.55f, 0.10f), emissive: true, emisIntensity: 8f);
            var flarePrefab = Resources.Load<GameObject>("VFX/Fire/LargeFlames");
            if (flarePrefab != null) // graceful fallback if not in Resources
            {
                var fx = Object.Instantiate(flarePrefab, flareTip.transform);
                fx.transform.localPosition = Vector3.zero;
            }

            // helipad — flat circular block with H
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(-10, 26.6f, 10), new Vector3(8, 0.2f, 8),
                new Color(0.15f, 0.15f, 0.17f));
            // H mark (two vertical bars + crossbar, all white)
            var hWhite = new Color(0.95f, 0.95f, 0.95f);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(-11.2f, 26.72f, 10), new Vector3(0.5f, 0.05f, 3.5f), hWhite);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(-8.8f, 26.72f, 10), new Vector3(0.5f, 0.05f, 3.5f), hWhite);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(-10, 26.72f, 10), new Vector3(2.4f, 0.05f, 0.5f), hWhite);

            // red nav lights on corners of upper deck
            float[] nx = { -14, 14, -14, 14 };
            float[] nz = { -14, -14, 14, 14 };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, root.transform, new Vector3(nx[i], 20.8f, nz[i]),
                    new Vector3(0.5f, 0.5f, 0.5f), Color.red, emissive: true, emisIntensity: 5f);

            MarkTree(root, true);
            return root;
        }

        // ========================================================================
        // HELICOPTER (with HelicopterLoop behavior — NOT static)
        // ========================================================================
        public static GameObject AddHelicopter(Transform parent, Vector3 startPos, Vector3 endPos, float speed)
        {
            var root = new GameObject("Helicopter");
            root.transform.SetParent(parent, false);
            root.transform.position = startPos;

            var khaki = new Color(0.38f, 0.40f, 0.25f);
            // body — flattened sphere (stretched ellipsoid)
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 0, 0), new Vector3(2.2f, 1.6f, 4.5f), khaki);
            // cockpit window
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 0.3f, 1.6f), new Vector3(1.6f, 1.0f, 1.8f),
                new Color(0.10f, 0.18f, 0.22f), emissive: true, emisIntensity: 0.3f);
            // tail boom
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.2f, -3.8f),
                new Vector3(0.28f, 1.8f, 0.28f), khaki).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // tail fin
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.9f, -5.5f), new Vector3(0.12f, 1.0f, 0.6f), khaki);

            // main rotor hub + 2 perpendicular blades
            var mainRotor = new GameObject("MainRotor");
            mainRotor.transform.SetParent(root.transform, false);
            mainRotor.transform.localPosition = new Vector3(0, 1.1f, 0);
            Prim(PrimitiveType.Cube, mainRotor.transform, Vector3.zero, new Vector3(7f, 0.08f, 0.3f), Color.black);
            Prim(PrimitiveType.Cube, mainRotor.transform, Vector3.zero, new Vector3(0.3f, 0.08f, 7f), Color.black);
            Prim(PrimitiveType.Cylinder, mainRotor.transform, new Vector3(0, -0.15f, 0), new Vector3(0.25f, 0.15f, 0.25f), Color.gray);

            // tail rotor — smaller blades, spin around X
            var tailRotor = new GameObject("TailRotor");
            tailRotor.transform.SetParent(root.transform, false);
            tailRotor.transform.localPosition = new Vector3(0.35f, 0.8f, -5.5f);
            Prim(PrimitiveType.Cube, tailRotor.transform, Vector3.zero, new Vector3(0.06f, 1.8f, 0.15f), Color.black);
            Prim(PrimitiveType.Cube, tailRotor.transform, Vector3.zero, new Vector3(0.06f, 0.15f, 1.8f), Color.black);

            // landing skids — two parallel cylinders underneath
            var sL = Prim(PrimitiveType.Cylinder, root.transform, new Vector3(-1.0f, -1.2f, 0),
                new Vector3(0.12f, 2.0f, 0.12f), Color.gray);
            sL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var sR = Prim(PrimitiveType.Cylinder, root.transform, new Vector3(1.0f, -1.2f, 0),
                new Vector3(0.12f, 2.0f, 0.12f), Color.gray);
            sR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var loop = root.AddComponent<HelicopterLoop>();
            loop.startPos = startPos;
            loop.endPos = endPos;
            loop.speed = speed;
            loop.mainRotor = mainRotor.transform;
            loop.tailRotor = tailRotor.transform;

            // NOT static — it moves
            return root;
        }

        // ========================================================================
        // FLAGPOLE
        // ========================================================================
        public static GameObject AddFlagpole(Transform parent, Vector3 pos, Color flagColor, string label)
        {
            var root = new GameObject(string.IsNullOrEmpty(label) ? "Flagpole" : "Flagpole_" + label);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            // base block
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 0.4f, 0), new Vector3(1.2f, 0.8f, 1.2f),
                new Color(0.22f, 0.22f, 0.22f), keepCollider: true);
            // pole
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 8f, 0), new Vector3(0.4f, 7.5f, 0.4f),
                new Color(0.30f, 0.30f, 0.32f), keepCollider: true);
            // ball finial
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 15.5f, 0), new Vector3(0.5f, 0.5f, 0.5f),
                new Color(0.80f, 0.75f, 0.25f));
            // flag — static rectangular block attached near top
            Prim(PrimitiveType.Cube, root.transform, new Vector3(1.8f, 13.5f, 0), new Vector3(3.2f, 2.0f, 0.05f),
                flagColor);

            // label via a TextMesh so the caller's `label` argument is used
            if (!string.IsNullOrEmpty(label))
            {
                var tgo = new GameObject("Label");
                tgo.transform.SetParent(root.transform, false);
                tgo.transform.localPosition = new Vector3(1.8f, 12.0f, 0.06f);
                tgo.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                var tm = tgo.AddComponent<TextMesh>();
                tm.text = label;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.6f;
                tm.color = Color.white;
                tm.fontSize = 48;
            }

            MarkTree(root, true);
            return root;
        }

        // ========================================================================
        // LIGHTHOUSE
        // ========================================================================
        public static GameObject AddLighthouse(Transform parent, Vector3 pos, Color lampColor)
        {
            var root = new GameObject("Lighthouse");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            var white = new Color(0.95f, 0.95f, 0.92f);
            var red = new Color(0.75f, 0.12f, 0.10f);

            // 3 tapered cylinders — radii 2.5, 2.0, 1.5 — total ~20 tall (keep colliders on base)
            // Unity Cylinder primitive has default radius 0.5 & height 2 — so scale x/z = 2*radius, y = height/2
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 3.5f, 0), new Vector3(5.0f, 3.5f, 5.0f),
                white, keepCollider: true);
            // red stripe band on first-to-second transition
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 7.1f, 0), new Vector3(5.05f, 0.5f, 5.05f), red);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 10.5f, 0), new Vector3(4.0f, 3.5f, 4.0f),
                white, keepCollider: true);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 17.0f, 0), new Vector3(3.0f, 3.0f, 3.0f),
                white);

            // gallery ring
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 20.1f, 0), new Vector3(3.6f, 0.15f, 3.6f),
                new Color(0.20f, 0.20f, 0.20f));
            // lamp room — emissive
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 21.2f, 0), new Vector3(2.2f, 1.0f, 2.2f),
                lampColor, emissive: true, emisIntensity: 8f);
            // cap dome — use sphere scaled flat
            Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 22.8f, 0), new Vector3(2.4f, 1.2f, 2.4f),
                new Color(0.15f, 0.15f, 0.15f));
            // spire
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 24.0f, 0), new Vector3(0.15f, 0.6f, 0.15f),
                new Color(0.10f, 0.10f, 0.10f));

            // rotating beam — thin long emissive cylinder spun around Y from lamp center
            var beamPivot = new GameObject("BeamPivot");
            beamPivot.transform.SetParent(root.transform, false);
            beamPivot.transform.localPosition = new Vector3(0, 21.2f, 0);
            // lay the beam horizontally along +Z; it sweeps around as the pivot rotates
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            beam.transform.SetParent(beamPivot.transform, false);
            beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            beam.transform.localPosition = new Vector3(0, 0, 20f); // push along Z so it radiates outward
            beam.transform.localScale = new Vector3(0.25f, 20f, 0.25f);
            var br = beam.GetComponent<Renderer>();
            if (br != null) br.sharedMaterial = Emissive(lampColor, 4f);
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            beamPivot.AddComponent<SpinY>().degPerSec = 45f;

            // beam pivot is animated — don't mark as static; mark the rest
            MarkTree(root, true);
            beamPivot.isStatic = false;
            beam.isStatic = false;
            return root;
        }

        // ========================================================================
        // SEARCHLIGHT
        // ========================================================================
        public static GameObject AddSearchlight(Transform parent, Vector3 pos, Vector3 lookDirection)
        {
            var root = new GameObject("Searchlight");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;

            // mount pole (keep collider — structural)
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 1.5f, 0), new Vector3(0.6f, 1.5f, 0.6f),
                new Color(0.30f, 0.30f, 0.32f), keepCollider: true);

            // yaw pivot — swings left/right
            var yawPivot = new GameObject("YawPivot");
            yawPivot.transform.SetParent(root.transform, false);
            yawPivot.transform.localPosition = new Vector3(0, 3.0f, 0);

            // orient the pivot so the head faces lookDirection
            Vector3 flat = lookDirection; flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            yawPivot.transform.localRotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

            // head box
            Prim(PrimitiveType.Cube, yawPivot.transform, new Vector3(0, 0, 0.2f), new Vector3(0.8f, 0.6f, 1.0f),
                new Color(0.20f, 0.20f, 0.22f));
            // lens — emissive disc
            Prim(PrimitiveType.Cylinder, yawPivot.transform, new Vector3(0, 0, 0.75f),
                new Vector3(0.5f, 0.05f, 0.5f), new Color(1f, 0.95f, 0.7f), emissive: true, emisIntensity: 6f)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // spot light
            var lightGO = new GameObject("SpotLight");
            lightGO.transform.SetParent(yawPivot.transform, false);
            lightGO.transform.localPosition = new Vector3(0, 0, 0.8f);
            lightGO.transform.localRotation = Quaternion.identity; // points +Z along pivot forward
            var l = lightGO.AddComponent<Light>();
            l.type = LightType.Spot;
            l.intensity = 8f;
            l.range = 30f;
            l.spotAngle = 20f;
            l.color = new Color(1f, 0.97f, 0.85f);
            l.shadows = LightShadows.None; // why: decor light — avoid shadow cost

            // sweep component
            var sweep = yawPivot.AddComponent<YawSweeper>();
            sweep.amplitudeDeg = 45f;
            sweep.periodSec = 6f;

            // pole + head are mostly static; pivot animates
            root.isStatic = true;
            return root;
        }
    }
}
