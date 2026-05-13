using UnityEngine;

namespace CloseEncounters.Arena
{
    // why: separate MonoBehaviour keeps the static decor helper non-runtime & zero-GC in Update
    public class HelicopterLoop : MonoBehaviour
    {
        public Vector3 startPos;
        public Vector3 endPos;
        public float speed = 20f;

        [HideInInspector] public Transform mainRotor;
        [HideInInspector] public Transform tailRotor;

        private Vector3 _target;
        private Vector3 _dir;
        private bool _toEnd = true;

        void Start()
        {
            transform.position = startPos;
            _target = endPos;
            FaceTarget();
        }

        void FaceTarget()
        {
            _dir = (_target - transform.position);
            _dir.y = 0f;
            float sq = _dir.sqrMagnitude;
            if (sq > 0.0001f)
            {
                _dir /= Mathf.Sqrt(sq);
                transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // spin rotors (avoid allocations — direct Rotate)
            if (mainRotor != null) mainRotor.Rotate(0f, 1800f * dt, 0f, Space.Self);
            if (tailRotor != null) tailRotor.Rotate(2400f * dt, 0f, 0f, Space.Self);

            Vector3 pos = transform.position;
            Vector3 to = _target - pos;
            to.y = 0f;
            float distSq = to.x * to.x + to.z * to.z;

            float step = speed * dt;
            if (distSq <= step * step)
            {
                // arrived — snap (preserve altitude of the target endpoint) and flip
                pos.x = _target.x;
                pos.z = _target.z;
                pos.y = _target.y;
                transform.position = pos;
                _toEnd = !_toEnd;
                _target = _toEnd ? endPos : startPos;
                FaceTarget();
                return;
            }

            pos.x += _dir.x * step;
            pos.z += _dir.z * step;
            transform.position = pos;
        }
    }

    // why: reusable yaw-sweeper for searchlights — kept here so the decor file stays a single class
    public class YawSweeper : MonoBehaviour
    {
        public float amplitudeDeg = 45f;
        public float periodSec = 6f;
        private float _t;
        private Quaternion _base;

        void Start() { _base = transform.localRotation; }

        void Update()
        {
            _t += Time.deltaTime;
            float a = Mathf.Sin((_t / periodSec) * Mathf.PI * 2f) * amplitudeDeg;
            transform.localRotation = _base * Quaternion.Euler(0f, a, 0f);
        }
    }

    // why: cheap continuous Y rotation for the lighthouse lamp beam
    public class SpinY : MonoBehaviour
    {
        public float degPerSec = 45f;
        void Update() { transform.Rotate(0f, degPerSec * Time.deltaTime, 0f, Space.Self); }
    }
}
