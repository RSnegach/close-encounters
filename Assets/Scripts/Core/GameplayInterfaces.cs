using UnityEngine;

namespace CloseEncounters.Core
{
    /// <summary>Anything that can receive damage (vehicles, props, boss units, etc.).</summary>
    public interface IDamageTarget
    {
        void TakeDamage(int amount, Vector3 hitPoint);
    }

    /// <summary>Tagged prop that can be broken off a static mount (sandbag, crate, tree).</summary>
    public interface IBreakable
    {
        void BreakFree(Vector3 impactForce);
    }

    /// <summary>Installations that detonate in a chain-reaction (ammo dumps, fuel depots).</summary>
    public interface IChainExplosive
    {
        void Detonate(int incomingDamage);
    }
}
