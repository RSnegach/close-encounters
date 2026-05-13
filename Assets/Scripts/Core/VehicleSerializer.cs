using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CloseEncounters.Core
{
    [Serializable]
    public class PartEntry
    {
        public string id;
        public int[] gridPosition;
        public int[] armorFace; // null for non-armor, [x,y,z] for armor orientation

        public PartEntry() { }

        public PartEntry(string id, int[] gridPosition, int[] armorFace = null)
        {
            this.id = id;
            this.gridPosition = gridPosition;
            this.armorFace = armorFace;
        }

        public Dictionary<string, object> ToDictionary()
        {
            var posArray = new List<object>();
            if (gridPosition != null)
                for (int i = 0; i < gridPosition.Length; i++)
                    posArray.Add(gridPosition[i]);

            var dict = new Dictionary<string, object>
            {
                { "id", id },
                { "gridPosition", posArray }
            };

            if (armorFace != null)
            {
                var faceArray = new List<object>();
                for (int i = 0; i < armorFace.Length; i++)
                    faceArray.Add(armorFace[i]);
                dict["armorFace"] = faceArray;
            }

            return dict;
        }

        public static PartEntry FromDictionary(Dictionary<string, object> dict)
        {
            var entry = new PartEntry();
            if (dict.TryGetValue("id", out object idObj))
                entry.id = idObj?.ToString() ?? "";

            // Support both "gridPosition" (Unity) and "grid_position" (Godot) keys
            object posObj = null;
            if (!dict.TryGetValue("gridPosition", out posObj))
                dict.TryGetValue("grid_position", out posObj);

            if (posObj is List<object> posList)
            {
                entry.gridPosition = new int[posList.Count];
                for (int i = 0; i < posList.Count; i++)
                {
                    if (posList[i] is int pi)
                        entry.gridPosition[i] = pi;
                    else if (posList[i] is long pl)
                        entry.gridPosition[i] = (int)pl;
                    else if (posList[i] is double pd)
                        entry.gridPosition[i] = (int)pd;
                    else if (int.TryParse(posList[i]?.ToString(), out int parsed))
                        entry.gridPosition[i] = parsed;
                }
            }
            else
            {
                entry.gridPosition = new int[] { 0, 0, 0 };
            }

            // Parse armorFace if present
            object faceObj = null;
            if (!dict.TryGetValue("armorFace", out faceObj))
                dict.TryGetValue("armor_face", out faceObj);
            if (faceObj is List<object> faceList && faceList.Count >= 3)
            {
                entry.armorFace = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    if (faceList[i] is long fl) entry.armorFace[i] = (int)fl;
                    else if (faceList[i] is double fd) entry.armorFace[i] = (int)fd;
                    else if (int.TryParse(faceList[i]?.ToString(), out int fp)) entry.armorFace[i] = fp;
                }
            }

            return entry;
        }
    }

    [Serializable]
    public class VehicleData
    {
        public string name;
        public string domain;
        public float forwardAngle;
        public List<PartEntry> parts;

        public VehicleData()
        {
            name = "Untitled";
            domain = "ground";
            forwardAngle = 0f;
            parts = new List<PartEntry>();
        }

        public Dictionary<string, object> ToDictionary()
        {
            var partsArray = new List<object>();
            for (int i = 0; i < parts.Count; i++)
                partsArray.Add(parts[i].ToDictionary());

            return new Dictionary<string, object>
            {
                { "name", name },
                { "domain", domain },
                { "forwardAngle", forwardAngle },
                { "parts", partsArray }
            };
        }

        public static VehicleData FromDictionary(Dictionary<string, object> dict)
        {
            var data = new VehicleData();

            if (dict.TryGetValue("name", out object nameObj))
                data.name = nameObj?.ToString() ?? "Untitled";

            if (dict.TryGetValue("domain", out object domainObj))
                data.domain = domainObj?.ToString() ?? "ground";

            // Support both "forwardAngle" (Unity) and "forward_angle" (Godot)
            object angleObj = null;
            if (!dict.TryGetValue("forwardAngle", out angleObj))
                dict.TryGetValue("forward_angle", out angleObj);
            if (angleObj != null)
            {
                if (angleObj is double d) data.forwardAngle = (float)d;
                else if (angleObj is float f) data.forwardAngle = f;
                else if (angleObj is int i) data.forwardAngle = i;
                else if (angleObj is long l) data.forwardAngle = l;
            }

            if (dict.TryGetValue("parts", out object partsObj) && partsObj is List<object> partsList)
            {
                data.parts = new List<PartEntry>(partsList.Count);
                for (int i = 0; i < partsList.Count; i++)
                {
                    if (partsList[i] is Dictionary<string, object> partDict)
                        data.parts.Add(PartEntry.FromDictionary(partDict));
                }
            }

            return data;
        }

        public int CalculateTotalCost()
        {
            if (PartRegistry.Instance == null) return 0;

            int total = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                PartData part = PartRegistry.Instance.GetPart(parts[i].id);
                if (part != null)
                    total += part.cost;
            }
            return total;
        }

        public float CalculateTotalMass()
        {
            if (PartRegistry.Instance == null) return 0f;

            float total = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                PartData part = PartRegistry.Instance.GetPart(parts[i].id);
                if (part != null)
                    total += part.massKg;
            }
            return total;
        }
    }

    public static class VehicleSerializer
    {
        private const string VehicleFolder = "vehicles";
        private const string FileExtension = ".json";

        public static string GetVehicleDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, VehicleFolder);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        // --- Save ---

        public static bool Save(VehicleData vehicle, string fileName = null)
        {
            if (vehicle == null)
            {
                Debug.LogError("[VehicleSerializer] Cannot save null vehicle data.");
                return false;
            }

            if (string.IsNullOrEmpty(fileName))
                fileName = vehicle.name;

            if (!TryResolveSafePath(fileName, out string path))
            {
                Debug.LogError("[VehicleSerializer] Rejected unsafe file name.");
                return false;
            }

            try
            {
                string json = MiniJson.Serialize(vehicle.ToDictionary());
                File.WriteAllText(path, json);
                Debug.Log($"[VehicleSerializer] Saved vehicle to {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VehicleSerializer] Failed to save vehicle: {e.Message}");
                return false;
            }
        }

        // --- Load ---

        public static VehicleData Load(string fileName)
        {
            if (!TryResolveSafePath(fileName, out string path))
            {
                Debug.LogError("[VehicleSerializer] Rejected unsafe or empty file name.");
                return null;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[VehicleSerializer] Vehicle file not found: {path}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                var dict = MiniJson.Deserialize(json);
                if (dict == null)
                {
                    Debug.LogError($"[VehicleSerializer] Failed to parse vehicle JSON: {path}");
                    return null;
                }
                return VehicleData.FromDictionary(dict);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VehicleSerializer] Failed to load vehicle: {e.Message}");
                return null;
            }
        }

        // --- Load from raw JSON string (for Resources/presets) ---

        public static VehicleData LoadFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var dict = MiniJson.Deserialize(json);
                if (dict == null) return null;
                return VehicleData.FromDictionary(dict);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VehicleSerializer] Failed to parse JSON: {e.Message}");
                return null;
            }
        }

        // --- List ---

        public static string[] ListSavedVehicles()
        {
            string dir = GetVehicleDirectory();
            string[] files = Directory.GetFiles(dir, "*" + FileExtension);
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            return names;
        }

        // --- Delete ---

        public static bool Delete(string fileName)
        {
            if (!TryResolveSafePath(fileName, out string path)) return false;
            if (!File.Exists(path)) return false;

            try
            {
                File.Delete(path);
                Debug.Log($"[VehicleSerializer] Deleted vehicle: {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VehicleSerializer] Failed to delete vehicle: {e.Message}");
                return false;
            }
        }

        // --- Exists ---

        public static bool Exists(string fileName)
        {
            if (!TryResolveSafePath(fileName, out string path)) return false;
            return File.Exists(path);
        }

        // Rejects names containing directory separators or parent-traversal segments.
        private static bool TryResolveSafePath(string fileName, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(fileName)) return false;
            fileName = SanitizeFileName(fileName);
            if (fileName.Contains("..")) return false;
            if (fileName.IndexOfAny(new[] { '/', '\\' }) >= 0) return false;
            if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
                fileName += FileExtension;
            path = Path.Combine(GetVehicleDirectory(), fileName);
            return true;
        }

        // --- Utilities ---

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "vehicle";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool isInvalid = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j]) { isInvalid = true; break; }
                }
                sb.Append(isInvalid ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
