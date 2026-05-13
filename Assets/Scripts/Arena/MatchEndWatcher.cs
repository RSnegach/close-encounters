using UnityEngine;
using CloseEncounters.Combat;
using CloseEncounters.Core;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Listens for vehicle kills; when only one player-controlled vehicle remains alive
    /// for a short grace period, calls GameManager.EndMatch with the winner.
    /// </summary>
    public class MatchEndWatcher : MonoBehaviour
    {
        public float endDelay = 3f;
        private float _winnerTimer = -1f;
        private int _winnerId = -1;
        private bool _ended;

        private void OnEnable()
        {
            DamageSystem.OnVehicleKilled += HandleKill;
        }

        private void OnDisable()
        {
            DamageSystem.OnVehicleKilled -= HandleKill;
        }

        private void HandleKill(VehicleRuntime victim, VehicleRuntime attacker)
        {
            CheckForVictor();
        }

        private void Update()
        {
            if (_ended) return;
            if (_winnerTimer < 0f) return;
            _winnerTimer -= Time.deltaTime;
            if (_winnerTimer <= 0f)
            {
                _ended = true;
                if (GameManager.Instance != null)
                    GameManager.Instance.EndMatch(_winnerId);
            }
        }

        private void CheckForVictor()
        {
            int aliveHuman = 0;
            int lastHumanId = -1;
            int aliveAI = 0;
            int lastAIId = -1;
            var all = VehicleRuntime.LiveInstances;
            for (int i = 0; i < all.Count; i++)
            {
                var vr = all[i];
                if (vr == null || !vr.IsAlive) continue;
                if (vr.IsAI) { aliveAI++; lastAIId = vr.PlayerId; }
                else { aliveHuman++; lastHumanId = vr.PlayerId; }
            }

            int totalAlive = aliveHuman + aliveAI;
            if (totalAlive <= 1)
            {
                _winnerId = aliveHuman == 1 ? lastHumanId : (aliveAI == 1 ? lastAIId : -1);
                if (_winnerTimer < 0f) _winnerTimer = endDelay;
            }
        }
    }
}
