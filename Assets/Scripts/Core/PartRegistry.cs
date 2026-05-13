using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloseEncounters.Core
{
    public class PartRegistry : MonoBehaviour
    {
        public static PartRegistry Instance { get; private set; }

        private readonly Dictionary<string, PartData> _parts = new Dictionary<string, PartData>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] PartFiles =
        {
            "Data/Parts/structural",
            "Data/Parts/propulsion",
            "Data/Parts/weapons",
            "Data/Parts/defense",
            "Data/Parts/utility",
            "Data/Parts/control"
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAllParts();
        }

        // --- Public API ---

        public PartData GetPart(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _parts.TryGetValue(id, out PartData part);
            return part;
        }

        public List<PartData> GetPartsForDomain(string domain)
        {
            var results = new List<PartData>();
            foreach (var part in _parts.Values)
            {
                if (part.IsValidForDomain(domain))
                    results.Add(part);
            }
            return results;
        }

        public List<PartData> GetPartsByCategory(string category)
        {
            var results = new List<PartData>();
            foreach (var part in _parts.Values)
            {
                if (string.Equals(part.category, category, StringComparison.OrdinalIgnoreCase))
                    results.Add(part);
            }
            return results;
        }

        public List<PartData> GetAllParts()
        {
            return new List<PartData>(_parts.Values);
        }

        public int PartCount => _parts.Count;

        // --- Loading ---

        private void LoadAllParts()
        {
            int loaded = 0;

            for (int i = 0; i < PartFiles.Length; i++)
            {
                TextAsset asset = Resources.Load<TextAsset>(PartFiles[i]);
                if (asset == null)
                {
                    Debug.LogWarning($"[PartRegistry] Part file not found: Resources/{PartFiles[i]}.json");
                    continue;
                }

                loaded += ParseAndRegister(asset.text, PartFiles[i]);
            }

            Debug.Log($"[PartRegistry] Loaded {loaded} parts from {PartFiles.Length} files. Registry total: {_parts.Count}");
        }

        private int ParseAndRegister(string json, string sourceFile)
        {
            int count = 0;

            // Try parsing as a root object with a "parts" array
            var root = MiniJson.Deserialize(json);
            if (root != null)
            {
                if (root.TryGetValue("parts", out object partsObj) && partsObj is List<object> partsArray)
                {
                    count += RegisterFromList(partsArray, sourceFile);
                }
                else
                {
                    // Single-object file: treat the root as a part
                    RegisterPart(root, sourceFile);
                    count++;
                }
                return count;
            }

            // Try parsing as a top-level array
            var rootArray = MiniJson.DeserializeArray(json);
            if (rootArray != null)
            {
                count += RegisterFromList(rootArray, sourceFile);
            }
            else
            {
                Debug.LogWarning($"[PartRegistry] Failed to parse JSON from {sourceFile}");
            }

            return count;
        }

        private int RegisterFromList(List<object> list, string sourceFile)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Dictionary<string, object> dict)
                {
                    RegisterPart(dict, sourceFile);
                    count++;
                }
            }
            return count;
        }

        private void RegisterPart(Dictionary<string, object> dict, string sourceFile)
        {
            PartData part = PartData.FromDictionary(dict);

            if (string.IsNullOrEmpty(part.id))
            {
                Debug.LogWarning($"[PartRegistry] Skipping part with empty id in {sourceFile}");
                return;
            }

            if (_parts.ContainsKey(part.id))
            {
                Debug.LogWarning($"[PartRegistry] Duplicate part id '{part.id}' from {sourceFile} — overwriting");
            }

            _parts[part.id] = part;
        }
    }
}
