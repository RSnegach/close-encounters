using System;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Core;

namespace CloseEncounters.AI
{
    // =========================================================================
    //  AIBuilder — static helper that procedurally assembles a VehicleData for
    //  AI opponents.  Picks parts from the PartRegistry that match the domain
    //  and budget, then places them on a logical grid.
    // =========================================================================
    public static class AIBuilder
    {
        // =====================================================================
        //  Public API
        // =====================================================================

        /// <summary>
        /// Build a complete vehicle for the given domain and budget.
        /// Returns null if PartRegistry is unavailable or no control module
        /// exists for the domain.
        /// </summary>
        public static VehicleData BuildVehicle(string domain, int budget, AIDifficultyLevel difficulty)
        {
            if (PartRegistry.Instance == null)
            {
                Debug.LogError("[AIBuilder] PartRegistry not available.");
                return null;
            }

            // Choose a template
            BuildTemplate template = PickTemplate(domain, difficulty);
            if (template == null)
            {
                Debug.LogError($"[AIBuilder] No template for domain={domain} difficulty={difficulty}");
                return null;
            }

            var vehicle = new VehicleData
            {
                name   = GenerateName(domain, difficulty),
                domain = domain,
            };

            var placed = new List<PlacedPart>(24);
            int spent = 0;

            // --- Phase 1: mandatory control module ---
            PartData controlPart = PickBestPart("control", domain, budget, template.preferredControlSub);
            if (controlPart == null)
            {
                Debug.LogError($"[AIBuilder] No control module found for domain={domain}");
                return null;
            }

            PlacedPart controlPlaced = Place(controlPart, Vector3Int.zero, placed);
            placed.Add(controlPlaced);
            spent += controlPart.cost;

            // --- Phase 2: structure ---
            int structureBudget = (int)(budget * template.structureFraction);
            spent += FillCategory("structure", domain, structureBudget, spent, budget,
                template.preferredStructureSub, placed, GridZone.AdjacentToControl);

            // --- Phase 3: propulsion ---
            int propulsionBudget = (int)(budget * template.propulsionFraction);
            spent += FillCategory("propulsion", domain, propulsionBudget, spent, budget,
                template.preferredPropulsionSub, placed, GridZone.Back);

            // --- Phase 4: weapons ---
            int weaponBudget = (int)(budget * template.weaponFraction);
            spent += FillCategory("weapon", domain, weaponBudget, spent, budget,
                template.preferredWeaponSub, placed, GridZone.Top);

            // --- Phase 5: defense ---
            int defenseBudget = (int)(budget * template.defenseFraction);
            spent += FillCategory("defense", domain, defenseBudget, spent, budget,
                template.preferredDefenseSub, placed, GridZone.Exterior);

            // --- Phase 6: utility / leftover ---
            int remaining = budget - spent;
            if (remaining > 0)
            {
                spent += FillCategory("utility", domain, remaining, spent, budget,
                    null, placed, GridZone.Any);
            }

            // --- Phase 7: random variation with leftover ---
            remaining = budget - spent;
            if (remaining > 50)
            {
                spent += FillRandomVariation(domain, remaining, spent, budget, placed);
            }

            // Convert placed parts to VehicleData entries
            for (int i = 0; i < placed.Count; i++)
            {
                PlacedPart pp = placed[i];
                vehicle.parts.Add(new PartEntry(pp.part.id, new int[]
                {
                    pp.gridPos.x, pp.gridPos.y, pp.gridPos.z
                }));
            }

            Debug.Log($"[AIBuilder] Built '{vehicle.name}' — {placed.Count} parts, cost {spent}/{budget}");
            return vehicle;
        }

        // =====================================================================
        //  Templates
        // =====================================================================

        private class BuildTemplate
        {
            public string name;
            public float structureFraction;
            public float propulsionFraction;
            public float weaponFraction;
            public float defenseFraction;

            public string preferredControlSub;
            public string preferredStructureSub;
            public string preferredPropulsionSub;
            public string preferredWeaponSub;
            public string preferredDefenseSub;

            public int maxWeapons;
            public int maxPropulsion;
        }

        private static BuildTemplate PickTemplate(string domain, AIDifficultyLevel difficulty)
        {
            string key = (domain ?? "ground").ToLowerInvariant();

            switch (key)
            {
                case "ground":
                    return GroundTemplate(difficulty);
                case "water":
                case "sea":
                    return WaterTemplate(difficulty);
                case "air":
                    return AirTemplate(difficulty);
                default:
                    return GroundTemplate(difficulty);
            }
        }

        // --- Ground templates ---

        private static BuildTemplate GroundTemplate(AIDifficultyLevel d)
        {
            switch (d)
            {
                case AIDifficultyLevel.Easy:
                    return new BuildTemplate
                    {
                        name = "GroundEasy",
                        structureFraction  = 0.30f,
                        propulsionFraction = 0.25f,
                        weaponFraction     = 0.25f,
                        defenseFraction    = 0.20f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "frame",
                        preferredPropulsionSub = "wheel",
                        preferredWeaponSub     = "turret",
                        preferredDefenseSub    = "armor",
                        maxWeapons    = 1,
                        maxPropulsion = 4,
                    };
                case AIDifficultyLevel.Medium:
                    return new BuildTemplate
                    {
                        name = "GroundMedium",
                        structureFraction  = 0.25f,
                        propulsionFraction = 0.20f,
                        weaponFraction     = 0.30f,
                        defenseFraction    = 0.25f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "frame",
                        preferredPropulsionSub = "wheel",
                        preferredWeaponSub     = "turret",
                        preferredDefenseSub    = "armor",
                        maxWeapons    = 2,
                        maxPropulsion = 4,
                    };
                case AIDifficultyLevel.Hard:
                default:
                    return new BuildTemplate
                    {
                        name = "GroundHard",
                        structureFraction  = 0.20f,
                        propulsionFraction = 0.15f,
                        weaponFraction     = 0.40f,
                        defenseFraction    = 0.25f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "reinforced_frame",
                        preferredPropulsionSub = "track",
                        preferredWeaponSub     = "cannon",
                        preferredDefenseSub    = "reactive_armor",
                        maxWeapons    = 3,
                        maxPropulsion = 4,
                    };
            }
        }

        // --- Water templates ---

        private static BuildTemplate WaterTemplate(AIDifficultyLevel d)
        {
            switch (d)
            {
                case AIDifficultyLevel.Easy:
                    // Pirate — light, fast, one cannon
                    return new BuildTemplate
                    {
                        name = "Pirate",
                        structureFraction  = 0.25f,
                        propulsionFraction = 0.30f,
                        weaponFraction     = 0.25f,
                        defenseFraction    = 0.20f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "hull",
                        preferredPropulsionSub = "sail",
                        preferredWeaponSub     = "cannon",
                        preferredDefenseSub    = "hull_plating",
                        maxWeapons    = 1,
                        maxPropulsion = 2,
                    };
                case AIDifficultyLevel.Medium:
                    // Destroyer — balanced
                    return new BuildTemplate
                    {
                        name = "Destroyer",
                        structureFraction  = 0.25f,
                        propulsionFraction = 0.20f,
                        weaponFraction     = 0.35f,
                        defenseFraction    = 0.20f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "hull",
                        preferredPropulsionSub = "engine",
                        preferredWeaponSub     = "turret",
                        preferredDefenseSub    = "hull_plating",
                        maxWeapons    = 3,
                        maxPropulsion = 2,
                    };
                case AIDifficultyLevel.Hard:
                default:
                    // Battleship — heavy armour, many guns
                    return new BuildTemplate
                    {
                        name = "Battleship",
                        structureFraction  = 0.20f,
                        propulsionFraction = 0.15f,
                        weaponFraction     = 0.40f,
                        defenseFraction    = 0.25f,
                        preferredControlSub    = "control_module",
                        preferredStructureSub  = "reinforced_hull",
                        preferredPropulsionSub = "engine",
                        preferredWeaponSub     = "cannon",
                        preferredDefenseSub    = "heavy_plating",
                        maxWeapons    = 5,
                        maxPropulsion = 2,
                    };
            }
        }

        // --- Air template ---

        private static BuildTemplate AirTemplate(AIDifficultyLevel d)
        {
            float weaponW = d == AIDifficultyLevel.Hard ? 0.35f :
                            d == AIDifficultyLevel.Medium ? 0.30f : 0.25f;

            return new BuildTemplate
            {
                name = $"Air{d}",
                structureFraction  = 0.20f,
                propulsionFraction = 0.30f,
                weaponFraction     = weaponW,
                defenseFraction    = 1f - 0.20f - 0.30f - weaponW,
                preferredControlSub    = "control_module",
                preferredStructureSub  = "fuselage",
                preferredPropulsionSub = "rotor",
                preferredWeaponSub     = "missile",
                preferredDefenseSub    = "light_armor",
                maxWeapons    = d == AIDifficultyLevel.Hard ? 4 : 2,
                maxPropulsion = 4,
            };
        }

        // =====================================================================
        //  Part picking
        // =====================================================================

        /// <summary>
        /// Pick the best single part matching the category + domain + budget.
        /// If preferredSub is non-null, strongly prefer it.
        /// </summary>
        private static PartData PickBestPart(string category, string domain, int maxCost, string preferredSub)
        {
            List<PartData> all = PartRegistry.Instance.GetPartsByCategory(category);
            PartData best = null;
            float bestScore = float.MinValue;

            for (int i = 0; i < all.Count; i++)
            {
                PartData p = all[i];
                if (p.cost > maxCost) continue;
                if (!p.IsValidForDomain(domain)) continue;

                float score = p.hp + (p.cost * 0.1f); // favour higher-HP / more expensive
                if (preferredSub != null && string.Equals(p.subcategory, preferredSub, StringComparison.OrdinalIgnoreCase))
                    score += 500f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        /// <summary>
        /// Gather candidate parts sorted by fitness (descending).
        /// </summary>
        private static List<PartData> GatherCandidates(string category, string domain, int maxCostPerPart, string preferredSub)
        {
            List<PartData> all = PartRegistry.Instance.GetPartsByCategory(category);
            var candidates = new List<PartData>(all.Count);

            for (int i = 0; i < all.Count; i++)
            {
                PartData p = all[i];
                if (p.cost > maxCostPerPart) continue;
                if (!p.IsValidForDomain(domain)) continue;
                candidates.Add(p);
            }

            // Sort descending by a heuristic score
            candidates.Sort((a, b) =>
            {
                float sa = ScorePart(a, preferredSub);
                float sb = ScorePart(b, preferredSub);
                return sb.CompareTo(sa);
            });

            return candidates;
        }

        private static float ScorePart(PartData p, string preferredSub)
        {
            float score = p.hp * 0.5f + p.cost * 0.3f;
            if (preferredSub != null && string.Equals(p.subcategory, preferredSub, StringComparison.OrdinalIgnoreCase))
                score += 300f;
            return score;
        }

        // =====================================================================
        //  Category filling — buys as many parts as the sub-budget allows,
        //  respecting the overall budget cap.
        // =====================================================================

        private static int FillCategory(string category, string domain, int subBudget, int alreadySpent,
            int totalBudget, string preferredSub, List<PlacedPart> placed, GridZone zone)
        {
            int maxCostPerPart = totalBudget - alreadySpent;
            if (maxCostPerPart <= 0) return 0;

            List<PartData> candidates = GatherCandidates(category, domain, maxCostPerPart, preferredSub);
            if (candidates.Count == 0) return 0;

            int spent = 0;
            int maxParts = GetMaxPartsForCategory(category);

            for (int attempt = 0; attempt < maxParts && spent < subBudget; attempt++)
            {
                int remaining = Mathf.Min(subBudget - spent, totalBudget - alreadySpent - spent);
                if (remaining <= 0) break;

                PartData pick = PickAffordable(candidates, remaining);
                if (pick == null) break;

                Vector3Int pos = FindGridPosition(pick, placed, zone);
                PlacedPart pp = Place(pick, pos, placed);
                placed.Add(pp);
                spent += pick.cost;
            }

            return spent;
        }

        /// <summary>
        /// From sorted candidates, pick one that fits in the remaining budget.
        /// Adds slight randomisation — sometimes picks 2nd or 3rd best.
        /// </summary>
        private static PartData PickAffordable(List<PartData> sorted, int maxCost)
        {
            // Collect affordable
            int affordable = 0;
            for (int i = 0; i < sorted.Count && affordable < 5; i++)
            {
                if (sorted[i].cost <= maxCost) affordable++;
            }

            if (affordable == 0) return null;

            // Pick from top-N with slight randomness
            int pick = UnityEngine.Random.Range(0, Mathf.Min(affordable, 3));
            int idx = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].cost > maxCost) continue;
                if (idx == pick) return sorted[i];
                idx++;
            }

            return null;
        }

        private static int GetMaxPartsForCategory(string category)
        {
            switch (category)
            {
                case "structure":  return 6;
                case "propulsion": return 6;
                case "weapon":     return 4;
                case "defense":    return 4;
                case "utility":    return 3;
                case "control":    return 1;
                default:           return 3;
            }
        }

        // =====================================================================
        //  Random variation with leftover budget
        // =====================================================================

        private static int FillRandomVariation(string domain, int remaining, int alreadySpent,
            int totalBudget, List<PlacedPart> placed)
        {
            string[] categories = { "weapon", "defense", "structure", "utility" };
            int spent = 0;

            for (int attempt = 0; attempt < 4 && remaining - spent > 50; attempt++)
            {
                string cat = categories[UnityEngine.Random.Range(0, categories.Length)];
                int subBudget = Mathf.Min(remaining - spent, (remaining - spent) / 2 + 50);

                int maxCost = totalBudget - alreadySpent - spent;
                if (maxCost <= 0) break;

                List<PartData> candidates = GatherCandidates(cat, domain, maxCost, null);
                if (candidates.Count == 0) continue;

                PartData pick = PickAffordable(candidates, maxCost);
                if (pick == null) continue;

                Vector3Int pos = FindGridPosition(pick, placed, GridZone.Any);
                PlacedPart pp = Place(pick, pos, placed);
                placed.Add(pp);
                spent += pick.cost;
            }

            return spent;
        }

        // =====================================================================
        //  Grid placement
        // =====================================================================

        private enum GridZone
        {
            Center,
            AdjacentToControl,
            Back,
            Top,
            Exterior,
            Any,
        }

        private struct PlacedPart
        {
            public PartData part;
            public Vector3Int gridPos;
        }

        private static PlacedPart Place(PartData part, Vector3Int pos, List<PlacedPart> existing)
        {
            return new PlacedPart { part = part, gridPos = pos };
        }

        /// <summary>
        /// Find a non-overlapping grid position for a part in the requested zone.
        /// </summary>
        private static Vector3Int FindGridPosition(PartData part, List<PlacedPart> placed, GridZone zone)
        {
            // Determine zone offsets
            Vector3Int baseOffset = ZoneBaseOffset(zone, placed);

            // Try to find a non-overlapping slot near the base offset
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector3Int candidate = baseOffset + RandomSmallOffset(attempt);
                if (!Overlaps(candidate, part.size, placed))
                    return candidate;
            }

            // Fallback: expand outward along +X until non-overlapping
            for (int dx = placed.Count; dx < placed.Count + 256; dx++)
            {
                Vector3Int candidate = baseOffset + new Vector3Int(dx, 0, 0);
                if (!Overlaps(candidate, part.size, placed))
                    return candidate;
            }
            return baseOffset + new Vector3Int(placed.Count + 256, 0, 0);
        }

        private static Vector3Int ZoneBaseOffset(GridZone zone, List<PlacedPart> placed)
        {
            // Find control centre (first placed part, typically at origin)
            Vector3Int control = placed.Count > 0 ? placed[0].gridPos : Vector3Int.zero;

            switch (zone)
            {
                case GridZone.Center:
                    return control;

                case GridZone.AdjacentToControl:
                    // Place structure adjacent (x+-1 or z+-1)
                    return control + new Vector3Int(
                        UnityEngine.Random.Range(-1, 2),
                        0,
                        UnityEngine.Random.Range(-1, 2));

                case GridZone.Back:
                    // Behind the control centre (negative z = back)
                    return control + new Vector3Int(0, 0, -2);

                case GridZone.Top:
                    // Above the control centre
                    return control + new Vector3Int(0, 1, 0);

                case GridZone.Exterior:
                    // On the perimeter — pick a random face
                    int face = UnityEngine.Random.Range(0, 4);
                    switch (face)
                    {
                        case 0: return control + new Vector3Int(2, 0, 0);
                        case 1: return control + new Vector3Int(-2, 0, 0);
                        case 2: return control + new Vector3Int(0, 0, 2);
                        default: return control + new Vector3Int(0, 0, -2);
                    }

                case GridZone.Any:
                default:
                    return control + new Vector3Int(
                        UnityEngine.Random.Range(-2, 3),
                        UnityEngine.Random.Range(0, 2),
                        UnityEngine.Random.Range(-2, 3));
            }
        }

        private static Vector3Int RandomSmallOffset(int attempt)
        {
            if (attempt == 0) return Vector3Int.zero;
            return new Vector3Int(
                UnityEngine.Random.Range(-attempt, attempt + 1),
                0,
                UnityEngine.Random.Range(-attempt, attempt + 1));
        }

        /// <summary>
        /// Check if placing a part with the given size at 'pos' overlaps any
        /// already-placed part.
        /// </summary>
        private static bool Overlaps(Vector3Int pos, Vector3Int size, List<PlacedPart> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                PlacedPart pp = placed[i];
                if (AABBOverlap(pos, size, pp.gridPos, pp.part.size))
                    return true;
            }
            return false;
        }

        private static bool AABBOverlap(Vector3Int aPos, Vector3Int aSize, Vector3Int bPos, Vector3Int bSize)
        {
            // Treat size as extending in positive direction from pos
            return aPos.x < bPos.x + bSize.x && aPos.x + aSize.x > bPos.x
                && aPos.y < bPos.y + bSize.y && aPos.y + aSize.y > bPos.y
                && aPos.z < bPos.z + bSize.z && aPos.z + aSize.z > bPos.z;
        }

        // =====================================================================
        //  Name generation
        // =====================================================================

        private static readonly string[] GroundPrefixes = { "Iron", "Steel", "Thunder", "Stone", "Rust", "Gravel", "Hammer", "Boulder" };
        private static readonly string[] GroundSuffixes = { "Roller", "Crusher", "Treader", "Runner", "Brawler", "Charger", "Tank", "Ram" };
        private static readonly string[] WaterPrefixes  = { "Storm", "Coral", "Tide", "Reef", "Anchor", "Kraken", "Harpoon", "Abyss" };
        private static readonly string[] WaterSuffixes  = { "Raider", "Corsair", "Leviathan", "Marauder", "Dreadnought", "Galleon", "Frigate", "Sloop" };
        private static readonly string[] AirPrefixes    = { "Sky", "Cloud", "Gale", "Zephyr", "Talon", "Hawk", "Storm", "Blitz" };
        private static readonly string[] AirSuffixes    = { "Fury", "Striker", "Raptor", "Wing", "Phantom", "Viper", "Ace", "Falcon" };

        private static string GenerateName(string domain, AIDifficultyLevel difficulty)
        {
            string[] prefixes, suffixes;
            switch ((domain ?? "ground").ToLowerInvariant())
            {
                case "water":
                case "sea":
                    prefixes = WaterPrefixes;
                    suffixes = WaterSuffixes;
                    break;
                case "air":
                    prefixes = AirPrefixes;
                    suffixes = AirSuffixes;
                    break;
                default:
                    prefixes = GroundPrefixes;
                    suffixes = GroundSuffixes;
                    break;
            }

            string prefix = prefixes[UnityEngine.Random.Range(0, prefixes.Length)];
            string suffix = suffixes[UnityEngine.Random.Range(0, suffixes.Length)];

            string tier = difficulty == AIDifficultyLevel.Hard ? "III" :
                          difficulty == AIDifficultyLevel.Medium ? "II" : "I";

            return $"{prefix} {suffix} {tier}";
        }
    }
}
