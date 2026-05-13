using System;
using System.Collections.Generic;
using UnityEngine;
using CloseEncounters.Core;

namespace CloseEncounters.Vehicle
{
    /// <summary>
    /// Vehicle builder: 3D grid editor with orbit camera, ghost preview,
    /// placement/removal, BFS connectivity validation, undo/redo, and
    /// domain-specific grid sizing.
    /// </summary>
    public class VehicleBuilder : MonoBehaviour
    {
        // ==================================================================
        // Grid dimensions per domain
        // ==================================================================

        private static readonly Dictionary<string, Vector3Int> DomainGridSizes = new Dictionary<string, Vector3Int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ground",    new Vector3Int(7, 5, 7) },
            { "air",       new Vector3Int(10, 6, 12) },
            { "water",     new Vector3Int(12, 8, 16) }
        };

        // ==================================================================
        // State
        // ==================================================================

        private string _domain = "ground";
        private Vector3Int _gridSize;
        private int _budget;
        private int _usedBudget;

        // Grid contents: cell -> placed PartData id (origin cell only for multi-cell)
        private readonly Dictionary<Vector3Int, PlacedPart> _grid = new Dictionary<Vector3Int, PlacedPart>();

        // Visual GameObjects per origin cell
        private readonly Dictionary<Vector3Int, GameObject> _partObjects = new Dictionary<Vector3Int, GameObject>();

        // Current selection / tool
        private PartData _selectedPart;
        private int _currentLayer; // Y layer the cursor moves on
        private float _forwardAngle; // vehicle forward direction
        private Vector3Int _hoverCell = new Vector3Int(-1, -1, -1);
        private GameObject _ghostPreview;
        private bool _ghostValid;

        // Undo / redo
        private readonly List<BuildAction> _undoStack = new List<BuildAction>();
        private readonly List<BuildAction> _redoStack = new List<BuildAction>();

        // Camera orbit
        private Transform _cameraRig;
        private Camera _cam;
        private float _orbitYaw = 45f;
        private float _orbitPitch = 35f;
        private float _orbitDistance = 15f;
        private Vector3 _orbitTarget;

        private const float CellSize = 1f;
        private const float OrbitSpeed = 3f;
        private const float ZoomSpeed = 2f;
        private const float MinZoom = 5f;
        private const float MaxZoom = 40f;

        // Right-click drag detection (avoid removing parts when orbit-dragging)
        private bool _rightClickDragged;

        // ==================================================================
        // Events
        // ==================================================================

        public event Action<PartData, Vector3Int> OnPartPlaced;
        public event Action<PartData, Vector3Int> OnPartRemoved;
        public event Action<int, int> OnBudgetChanged;  // (used, total)
        public event Action<string> OnValidationError;
        public event Action<float> OnForwardChanged;
        public event Action<int> OnLayerChanged;
        public event Action<PartData> OnPartSelected;

        // ==================================================================
        // Initialization
        // ==================================================================

        private bool _initialized;

        /// <summary>
        /// Called by SceneBootstrapper. Reads settings from GameManager and
        /// delegates to Setup().
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            MatchSettings settings = GameManager.Instance?.Settings;
            string domain = settings != null ? settings.domain : "ground";
            int budget    = settings != null ? settings.budget : 3000;

            Setup(domain, budget);
        }

        /// <summary>
        /// Fallback: if nobody called Initialize() before the first frame,
        /// do it now so the builder always works.
        /// </summary>
        private void Start()
        {
            if (!_initialized)
                Initialize();

            // Builder always has a visible, free cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Configures the builder for a given domain and budget.
        /// Can be called externally (e.g. from SceneBootstrapper) or
        /// internally from Initialize().
        /// </summary>
        public void Setup(string domain, int budget)
        {
            _domain = domain ?? "ground";
            _budget = budget;
            _gridSize = GetGridSize(_domain);
            _currentLayer = 0;
            _forwardAngle = 0f;
            _usedBudget = 0;

            SetupCamera();

            _orbitTarget = new Vector3(
                _gridSize.x * CellSize * 0.5f,
                _gridSize.y * CellSize * 0.25f,
                _gridSize.z * CellSize * 0.5f
            );

            // Apply camera position immediately so frame-1 is correct
            UpdateCameraPosition();

            // Add a directional light so parts are visible.
            if (FindAnyObjectByType<Light>() == null)
            {
                var lightObj = new GameObject("BuilderLight");
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.97f, 0.9f);
                light.intensity = 1.2f;
                lightObj.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            }

            // Add a ground reference plane under the grid.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "BuilderGround";
            ground.transform.position = new Vector3(
                _gridSize.x * CellSize * 0.5f,
                -0.01f,
                _gridSize.z * CellSize * 0.5f
            );
            ground.transform.localScale = new Vector3(
                _gridSize.x * CellSize * 0.15f,
                1f,
                _gridSize.z * CellSize * 0.15f
            );
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.color = new Color(0.2f, 0.2f, 0.25f);
            ground.GetComponent<MeshRenderer>().material = groundMat;

            _initialized = true;
            Debug.Log($"[VehicleBuilder] Initialized: domain={_domain}, grid={_gridSize}, budget={_budget}");
        }

        private void SetupCamera()
        {
            // Destroy the scene's default Main Camera so it doesn't
            // compete with ours.  Try both tag and name searches.
            Camera existingMain = Camera.main;
            if (existingMain != null && existingMain != _cam)
            {
                Debug.Log("[VehicleBuilder] Destroying default Main Camera.");
                Destroy(existingMain.gameObject);
            }
            // Also find by name in case the tag was stripped
            GameObject mainCamByName = GameObject.Find("Main Camera");
            if (mainCamByName != null && mainCamByName != _cam?.gameObject)
            {
                Debug.Log("[VehicleBuilder] Destroying 'Main Camera' object by name.");
                Destroy(mainCamByName);
            }

            // Avoid recreating if already set up (e.g. Setup called twice)
            if (_cam != null) return;

            // Create camera rig (pivot for orbit)
            var rigObj = new GameObject("BuilderCameraRig");
            _cameraRig = rigObj.transform;

            var camObj = new GameObject("BuilderCamera");
            camObj.tag = "MainCamera"; // so Camera.main returns this
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;
            _cam.fieldOfView = 50f;
            // Also add an AudioListener (Unity expects one per scene)
            camObj.AddComponent<AudioListener>();
            camObj.transform.SetParent(_cameraRig, false);
        }

        // ==================================================================
        // Update loop
        // ==================================================================

        private void Update()
        {
            HandleCameraInput();
            UpdateCameraPosition();
            HandleBuildInput();
            UpdateGhostPreview();
            UpdateHoverHighlight();
            UpdateFrontLabel();
        }

        // ------------------------------------------------------------------
        // Camera orbit
        // ------------------------------------------------------------------

        private void HandleCameraInput()
        {
            // Ctrl + left-click drag to pan
            if (Input.GetMouseButton(0) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                float panSpeed = _orbitDistance * 0.02f;
                Quaternion rotation = Quaternion.Euler(0f, _orbitYaw, 0f);
                Vector3 right = rotation * Vector3.right;
                Vector3 forward = rotation * Vector3.forward;
                _orbitTarget -= right * Input.GetAxis("Mouse X") * panSpeed;
                _orbitTarget -= forward * Input.GetAxis("Mouse Y") * panSpeed;
            }
            // Right-mouse-drag or middle-mouse-drag to orbit
            else if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                _orbitYaw += Input.GetAxis("Mouse X") * OrbitSpeed;
                _orbitPitch -= Input.GetAxis("Mouse Y") * OrbitSpeed;
                _orbitPitch = Mathf.Clamp(_orbitPitch, 5f, 85f);
            }

            // Scroll: Shift+Scroll = change layer, plain scroll = zoom
            // Skip scroll handling when pointer is over UI (let dropdowns scroll)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f && !IsPointerOverUI())
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    // Layer change
                    if (scroll > 0f)
                    {
                        _currentLayer = Mathf.Min(_gridSize.y - 1, _currentLayer + 1);
                        OnLayerChanged?.Invoke(_currentLayer);
                    }
                    else
                    {
                        _currentLayer = Mathf.Max(0, _currentLayer - 1);
                        OnLayerChanged?.Invoke(_currentLayer);
                    }
                }
                else
                {
                    _orbitDistance -= scroll * ZoomSpeed * _orbitDistance;
                    _orbitDistance = Mathf.Clamp(_orbitDistance, MinZoom, MaxZoom);
                }
            }
        }

        private void UpdateCameraPosition()
        {
            if (_cam == null) return;

            Quaternion rotation = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_orbitDistance);

            _cam.transform.position = _orbitTarget + offset;
            _cam.transform.LookAt(_orbitTarget);
        }

        // ------------------------------------------------------------------
        // Build input
        // ------------------------------------------------------------------

        private void HandleBuildInput()
        {
            // Q/E: change Y layer
            if (Input.GetKeyDown(KeyCode.Q))
            {
                _currentLayer = Mathf.Max(0, _currentLayer - 1);
                OnLayerChanged?.Invoke(_currentLayer);
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                _currentLayer = Mathf.Min(_gridSize.y - 1, _currentLayer + 1);
                OnLayerChanged?.Invoke(_currentLayer);
            }

            // F: cycle forward direction
            if (Input.GetKeyDown(KeyCode.F))
            {
                _forwardAngle = (_forwardAngle + 90f) % 360f;
                OnForwardChanged?.Invoke(_forwardAngle);
            }

            // Escape: deselect current part
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _selectedPart = null;
                OnPartSelected?.Invoke(null);
            }

            // Ctrl+Z: undo
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
            }

            // Ctrl+Y: redo
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }

            // Hover detection
            _hoverCell = RaycastToGrid();

            // Left click: place part OR pick up existing part
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                if (_selectedPart != null && IsValidCell(_hoverCell))
                {
                    TryPlacePart(_selectedPart, _hoverCell);
                }
                else if (_selectedPart == null && IsValidCell(_hoverCell) && _grid.ContainsKey(_hoverCell))
                {
                    // Pick up the existing part for repositioning
                    PlacedPart placed = _grid[_hoverCell];
                    _selectedPart = placed.partData;
                    RemovePartInternal(placed);
                    OnPartSelected?.Invoke(_selectedPart);
                }
            }

            // Right click: remove part (only on a short click, not an orbit drag)
            if (Input.GetMouseButtonDown(1))
            {
                _rightClickDragged = false;
            }
            if (Input.GetMouseButton(1))
            {
                // If the mouse moved while held, treat as an orbit drag
                if (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f)
                    _rightClickDragged = true;
            }
            if (Input.GetMouseButtonUp(1) && !_rightClickDragged && !IsPointerOverUI())
            {
                if (IsValidCell(_hoverCell))
                {
                    TryRemovePart(_hoverCell);
                }
            }
        }

        // ------------------------------------------------------------------
        // Grid raycast
        // ------------------------------------------------------------------

        private Vector3Int RaycastToGrid()
        {
            if (_cam == null) return new Vector3Int(-1, -1, -1);

            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);

            // Intersect with the Y-layer plane
            float planeY = _currentLayer * CellSize;
            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                int gx = Mathf.FloorToInt(hitPoint.x / CellSize);
                int gy = _currentLayer;
                int gz = Mathf.FloorToInt(hitPoint.z / CellSize);

                return new Vector3Int(gx, gy, gz);
            }

            return new Vector3Int(-1, -1, -1);
        }

        // ------------------------------------------------------------------
        // Part placement
        // ------------------------------------------------------------------

        public bool TryPlacePart(PartData part, Vector3Int origin)
        {
            if (part == null) return false;

            // Domain check
            if (!part.IsValidForDomain(_domain))
            {
                OnValidationError?.Invoke($"'{part.partName}' is not valid for domain '{_domain}'.");
                return false;
            }

            // Budget check
            if (_budget > 0 && _usedBudget + part.cost > _budget)
            {
                OnValidationError?.Invoke($"Not enough budget. Need {part.cost}, have {_budget - _usedBudget} remaining.");
                return false;
            }

            // Bounds check for all cells the part occupies
            for (int dx = 0; dx < part.size.x; dx++)
            for (int dy = 0; dy < part.size.y; dy++)
            for (int dz = 0; dz < part.size.z; dz++)
            {
                Vector3Int cell = origin + new Vector3Int(dx, dy, dz);
                if (!InBounds(cell))
                {
                    OnValidationError?.Invoke($"Part extends outside the grid at {cell}.");
                    return false;
                }
                if (_grid.ContainsKey(cell))
                {
                    OnValidationError?.Invoke($"Cell {cell} is already occupied.");
                    return false;
                }
            }

            // Control module uniqueness
            if (part.IsControlModule() && HasControlModule())
            {
                OnValidationError?.Invoke("Only one control module is allowed per vehicle.");
                return false;
            }

            // Armor placement rules
            bool isArmor = IsArmorPart(part);
            for (int dx2 = 0; dx2 < part.size.x; dx2++)
            for (int dy2 = 0; dy2 < part.size.y; dy2++)
            for (int dz2 = 0; dz2 < part.size.z; dz2++)
            {
                Vector3Int cell2 = origin + new Vector3Int(dx2, dy2, dz2);

                Vector3Int below = cell2 + new Vector3Int(0, -1, 0);
                if (_grid.TryGetValue(below, out var belowPart) && IsArmorPart(belowPart.partData))
                {
                    OnValidationError?.Invoke("Cannot build on top of armor plates.");
                    return false;
                }

                if (isArmor)
                {
                    Vector3Int aboveC = cell2 + Vector3Int.up;
                    Vector3Int belowC = cell2 + Vector3Int.down;
                    if (_grid.TryGetValue(aboveC, out var aboveP) && IsArmorPart(aboveP.partData))
                    {
                        OnValidationError?.Invoke("Cannot stack armor plates vertically.");
                        return false;
                    }
                    if (_grid.TryGetValue(belowC, out var belowP) && IsArmorPart(belowP.partData))
                    {
                        OnValidationError?.Invoke("Cannot stack armor plates vertically.");
                        return false;
                    }
                }
            }

            if (isArmor)
            {
                bool attached = false;
                for (int dx2 = 0; dx2 < part.size.x && !attached; dx2++)
                for (int dy2 = 0; dy2 < part.size.y && !attached; dy2++)
                for (int dz2 = 0; dz2 < part.size.z && !attached; dz2++)
                {
                    Vector3Int cell2 = origin + new Vector3Int(dx2, dy2, dz2);
                    Vector3Int[] adj = {
                        cell2 + Vector3Int.right, cell2 + Vector3Int.left,
                        cell2 + Vector3Int.up, cell2 + Vector3Int.down,
                        cell2 + new Vector3Int(0, 0, 1), cell2 + new Vector3Int(0, 0, -1)
                    };
                    for (int n = 0; n < adj.Length; n++)
                        if (_grid.TryGetValue(adj[n], out var adjPart) && !IsArmorPart(adjPart.partData))
                            attached = true;
                }
                if (!attached)
                {
                    OnValidationError?.Invoke("Armor must be placed next to an existing part.");
                    return false;
                }
            }

            // Water/air propulsion must attach to the side (X or Z face) of a structural block
            if (IsWaterOrAirPropulsion(part))
            {
                bool hasSideStructure = false;
                for (int dx2 = 0; dx2 < part.size.x && !hasSideStructure; dx2++)
                for (int dy2 = 0; dy2 < part.size.y && !hasSideStructure; dy2++)
                for (int dz2 = 0; dz2 < part.size.z && !hasSideStructure; dz2++)
                {
                    Vector3Int cell2 = origin + new Vector3Int(dx2, dy2, dz2);
                    Vector3Int[] sideAdj = {
                        cell2 + Vector3Int.right, cell2 + Vector3Int.left,
                        cell2 + new Vector3Int(0, 0, 1), cell2 + new Vector3Int(0, 0, -1)
                    };
                    for (int n = 0; n < sideAdj.Length; n++)
                        if (_grid.TryGetValue(sideAdj[n], out var adjPart) && IsStructuralPart(adjPart.partData))
                            hasSideStructure = true;
                }
                if (!hasSideStructure)
                {
                    OnValidationError?.Invoke($"'{part.partName}' must be placed on the side of a structural block.");
                    return false;
                }
            }

            // Place it
            PlacedPart placed = new PlacedPart(part, origin);

            for (int dx = 0; dx < part.size.x; dx++)
            for (int dy = 0; dy < part.size.y; dy++)
            for (int dz = 0; dz < part.size.z; dz++)
            {
                Vector3Int cell = origin + new Vector3Int(dx, dy, dz);
                _grid[cell] = placed;
            }

            _usedBudget += part.cost;

            // Create visual
            CreatePartVisual(placed);

            // Record undo action
            _undoStack.Add(new BuildAction(BuildActionType.Place, placed));
            _redoStack.Clear();

            OnPartPlaced?.Invoke(part, origin);
            OnBudgetChanged?.Invoke(_usedBudget, _budget);

            // Validate connectivity
            if (!ValidateConnectivity())
            {
                Debug.LogWarning("[VehicleBuilder] Warning: vehicle has disconnected parts.");
            }

            return true;
        }

        public bool TryRemovePart(Vector3Int cell)
        {
            if (!_grid.TryGetValue(cell, out PlacedPart placed)) return false;

            // Remove all cells this part occupies
            Vector3Int origin = placed.origin;
            PartData part = placed.partData;

            for (int dx = 0; dx < part.size.x; dx++)
            for (int dy = 0; dy < part.size.y; dy++)
            for (int dz = 0; dz < part.size.z; dz++)
            {
                Vector3Int c = origin + new Vector3Int(dx, dy, dz);
                _grid.Remove(c);
            }

            _usedBudget -= part.cost;
            if (_usedBudget < 0) _usedBudget = 0;

            // Remove visual
            DestroyPartVisual(origin);

            // Record undo
            _undoStack.Add(new BuildAction(BuildActionType.Remove, placed));
            _redoStack.Clear();

            OnPartRemoved?.Invoke(part, origin);
            OnBudgetChanged?.Invoke(_usedBudget, _budget);

            return true;
        }

        // ------------------------------------------------------------------
        // Visuals
        // ------------------------------------------------------------------

        private void CreatePartVisual(PlacedPart placed)
        {
            var go = new GameObject($"BuilderPart_{placed.partData.id}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = GridToWorld(placed.origin);

            var node = go.AddComponent<PartNode>();

            // Auto-detect armor face orientation before Setup creates the mesh
            if (IsArmorPart(placed.partData))
                node.armorFace = DetectArmorFace(placed);

            node.Setup(placed.partData, placed.origin);

            _partObjects[placed.origin] = go;
        }

        /// <summary>
        /// For armor, find the direction toward the first non-armor neighbor.
        /// Returns e.g. (1,0,0) if neighbor is at +X → armor faces +X.
        /// Falls back to (0,1,0) for a top-mounted horizontal slab.
        /// </summary>
        private Vector3Int DetectArmorFace(PlacedPart placed)
        {
            Vector3Int[] dirs = {
                new Vector3Int( 1, 0, 0),  // +X
                new Vector3Int(-1, 0, 0),  // -X
                new Vector3Int( 0, 0, 1),  // +Z
                new Vector3Int( 0, 0,-1),  // -Z
                new Vector3Int( 0,-1, 0),  // below (top slab)
                new Vector3Int( 0, 1, 0),  // above (bottom slab)
            };

            // Check all cells of the armor part
            for (int d = 0; d < dirs.Length; d++)
            {
                for (int dx = 0; dx < placed.partData.size.x; dx++)
                for (int dy = 0; dy < placed.partData.size.y; dy++)
                for (int dz = 0; dz < placed.partData.size.z; dz++)
                {
                    Vector3Int cell = placed.origin + new Vector3Int(dx, dy, dz);
                    Vector3Int neighbor = cell + dirs[d];
                    if (_grid.TryGetValue(neighbor, out var adj) && !IsArmorPart(adj.partData))
                        return dirs[d];
                }
            }

            return new Vector3Int(0, -1, 0); // default: horizontal on top
        }

        private void DestroyPartVisual(Vector3Int origin)
        {
            if (_partObjects.TryGetValue(origin, out GameObject go))
            {
                Destroy(go);
                _partObjects.Remove(origin);
            }
        }

        // ------------------------------------------------------------------
        // Ghost preview
        // ------------------------------------------------------------------

        private void UpdateGhostPreview()
        {
            if (_selectedPart == null || !IsValidCell(_hoverCell))
            {
                if (_ghostPreview != null)
                    _ghostPreview.SetActive(false);
                return;
            }

            // Create or reuse ghost
            if (_ghostPreview == null)
            {
                _ghostPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ghostPreview.name = "GhostPreview";
                DestroyImmediate(_ghostPreview.GetComponent<Collider>());
                var renderer = _ghostPreview.GetComponent<MeshRenderer>();
                renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                SetMaterialTransparent(renderer.material);
            }

            _ghostPreview.SetActive(true);

            // Position and scale to match part size
            Vector3 worldPos = GridToWorld(_hoverCell);
            Vector3 center = worldPos + new Vector3(
                (_selectedPart.size.x - 1) * CellSize * 0.5f,
                (_selectedPart.size.y - 1) * CellSize * 0.5f,
                (_selectedPart.size.z - 1) * CellSize * 0.5f
            );
            _ghostPreview.transform.position = center;
            _ghostPreview.transform.localScale = new Vector3(
                _selectedPart.size.x * CellSize * 0.95f,
                _selectedPart.size.y * CellSize * 0.95f,
                _selectedPart.size.z * CellSize * 0.95f
            );

            // Color: green if valid, red if invalid
            _ghostValid = CanPlaceAt(_selectedPart, _hoverCell);
            Color ghostColor = _ghostValid
                ? new Color(0.2f, 0.8f, 0.2f, 0.35f)
                : new Color(0.8f, 0.2f, 0.2f, 0.35f);

            var mat = _ghostPreview.GetComponent<MeshRenderer>().material;
            mat.color = ghostColor;
        }

        private void SetMaterialTransparent(Material mat)
        {
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        // ------------------------------------------------------------------
        // Hover highlight
        // ------------------------------------------------------------------

        private GameObject _hoverHighlight;

        private void UpdateHoverHighlight()
        {
            // Show highlight when hovering over a placed part (even with a part selected)
            bool hoveringPart = IsValidCell(_hoverCell) && _grid.ContainsKey(_hoverCell);

            if (!hoveringPart)
            {
                if (_hoverHighlight != null)
                    _hoverHighlight.SetActive(false);
                return;
            }

            // Create outline highlight — wireframe-style (thin transparent cube, slightly larger)
            if (_hoverHighlight == null)
            {
                _hoverHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _hoverHighlight.name = "HoverHighlight";
                DestroyImmediate(_hoverHighlight.GetComponent<Collider>());
                var renderer = _hoverHighlight.GetComponent<MeshRenderer>();
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                SetMaterialTransparent(mat);
                mat.color = new Color(1f, 1f, 1f, 0.15f);
                renderer.material = mat;
            }

            _hoverHighlight.SetActive(true);

            PlacedPart placed = _grid[_hoverCell];
            Vector3 worldPos = GridToWorld(placed.origin);
            Vector3 center = worldPos + new Vector3(
                (placed.partData.size.x - 1) * CellSize * 0.5f,
                (placed.partData.size.y - 1) * CellSize * 0.5f,
                (placed.partData.size.z - 1) * CellSize * 0.5f
            );
            _hoverHighlight.transform.position = center;
            _hoverHighlight.transform.localScale = new Vector3(
                placed.partData.size.x * CellSize * 1.06f,
                placed.partData.size.y * CellSize * 1.06f,
                placed.partData.size.z * CellSize * 1.06f
            );

            // White outline color when no part selected (click to pick up)
            // Green when a part is selected (indicates occupied cell)
            var hlRend = _hoverHighlight.GetComponent<MeshRenderer>();
            if (hlRend != null)
            {
                if (_selectedPart == null)
                    hlRend.material.color = new Color(1f, 1f, 1f, 0.2f);  // white = clickable
                else
                    hlRend.material.color = new Color(1f, 0.3f, 0.3f, 0.2f); // red = blocked
            }
        }

        // ------------------------------------------------------------------
        // Hover tooltip for placed parts
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsValidCell(_hoverCell) || !_grid.ContainsKey(_hoverCell)) return;

            PlacedPart placed = _grid[_hoverCell];
            PartData pd = placed.partData;
            if (pd == null) return;

            string text = $"<b>{(string.IsNullOrEmpty(pd.partName) ? pd.id : pd.partName)}</b>\n";
            text += $"Cost: ${pd.cost}  |  HP: {pd.hp}  |  Mass: {pd.massKg}kg\n";
            text += $"Size: {pd.size.x}x{pd.size.y}x{pd.size.z}  |  {pd.category}";

            int dmg = pd.GetStat<int>("damage", 0);
            float fireRate = pd.GetStat<float>("fire_rate", pd.GetStat<float>("fireRate", 0f));
            int ammo = pd.GetStat<int>("ammo", 0);
            if (dmg > 0)
                text += $"\nDmg: {dmg}  |  Rate: {fireRate:F1}/s  |  Ammo: {ammo}";

            float thrust = pd.GetStat<float>("thrust", 0f);
            if (thrust > 0f)
                text += $"\nThrust: {thrust:F0}N";

            int armor = pd.GetStat<int>("armor_value", 0);
            if (armor > 0)
                text += $"\nArmor: {armor}";

            Vector2 mouse = Event.current.mousePosition;
            Vector2 size = GUI.skin.box.CalcSize(new GUIContent(text));
            size.x = Mathf.Max(size.x + 20f, 200f);
            size.y += 10f;

            // Dark background tooltip
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.richText = true;
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = 12;
            style.padding = new RectOffset(8, 8, 6, 6);

            // Position to the right of cursor
            Rect rect = new Rect(mouse.x + 15f, mouse.y - 10f, size.x, size.y);

            // Keep on screen
            if (rect.xMax > Screen.width) rect.x = mouse.x - size.x - 15f;
            if (rect.yMax > Screen.height) rect.y = Screen.height - size.y;

            GUI.Box(rect, text, style);
        }

        // ------------------------------------------------------------------
        // GL grid drawing
        // ------------------------------------------------------------------

        private Material _gridLineMaterial;

        private void OnRenderObject()
        {
            if (_cam == null) return;
            // Draw grid for any camera (not just ours) so it's always visible.
            if (Camera.current == null) return;

            if (_gridLineMaterial == null)
            {
                _gridLineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                _gridLineMaterial.hideFlags = HideFlags.HideAndDontSave;
                _gridLineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _gridLineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _gridLineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _gridLineMaterial.SetInt("_ZWrite", 0);
            }

            _gridLineMaterial.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);

            // Draw the current layer grid
            float y = _currentLayer * CellSize;
            Color gridColor = new Color(0.4f, 0.6f, 0.8f, 0.3f);
            Color activeLayerColor = new Color(0.5f, 0.7f, 1f, 0.5f);

            // Horizontal lines (along X)
            for (int z = 0; z <= _gridSize.z; z++)
            {
                GL.Color(gridColor);
                GL.Vertex3(0f, y, z * CellSize);
                GL.Vertex3(_gridSize.x * CellSize, y, z * CellSize);
            }

            // Vertical lines (along Z)
            for (int x = 0; x <= _gridSize.x; x++)
            {
                GL.Color(gridColor);
                GL.Vertex3(x * CellSize, y, 0f);
                GL.Vertex3(x * CellSize, y, _gridSize.z * CellSize);
            }

            // Grid border on current layer (brighter)
            GL.Color(activeLayerColor);
            // Bottom edge
            GL.Vertex3(0f, y, 0f);
            GL.Vertex3(_gridSize.x * CellSize, y, 0f);
            // Top edge
            GL.Vertex3(0f, y, _gridSize.z * CellSize);
            GL.Vertex3(_gridSize.x * CellSize, y, _gridSize.z * CellSize);
            // Left edge
            GL.Vertex3(0f, y, 0f);
            GL.Vertex3(0f, y, _gridSize.z * CellSize);
            // Right edge
            GL.Vertex3(_gridSize.x * CellSize, y, 0f);
            GL.Vertex3(_gridSize.x * CellSize, y, _gridSize.z * CellSize);

            // Vertical pillars at corners to show grid height
            Color pillarColor = new Color(0.3f, 0.5f, 0.7f, 0.15f);
            GL.Color(pillarColor);
            float maxY = _gridSize.y * CellSize;

            GL.Vertex3(0f, 0f, 0f);
            GL.Vertex3(0f, maxY, 0f);

            GL.Vertex3(_gridSize.x * CellSize, 0f, 0f);
            GL.Vertex3(_gridSize.x * CellSize, maxY, 0f);

            GL.Vertex3(0f, 0f, _gridSize.z * CellSize);
            GL.Vertex3(0f, maxY, _gridSize.z * CellSize);

            GL.Vertex3(_gridSize.x * CellSize, 0f, _gridSize.z * CellSize);
            GL.Vertex3(_gridSize.x * CellSize, maxY, _gridSize.z * CellSize);

            // Wireframe outline around hovered part
            if (IsValidCell(_hoverCell) && _grid.ContainsKey(_hoverCell))
            {
                PlacedPart hp = _grid[_hoverCell];
                Vector3 lo = GridToWorld(hp.origin) - Vector3.one * 0.03f;
                Vector3 hi = lo + new Vector3(
                    hp.partData.size.x * CellSize + 0.06f,
                    hp.partData.size.y * CellSize + 0.06f,
                    hp.partData.size.z * CellSize + 0.06f);

                Color oc = _selectedPart == null
                    ? new Color(1f, 1f, 1f, 0.8f)
                    : new Color(1f, 0.3f, 0.3f, 0.6f);
                GL.Color(oc);

                // Bottom
                GL.Vertex3(lo.x, lo.y, lo.z); GL.Vertex3(hi.x, lo.y, lo.z);
                GL.Vertex3(hi.x, lo.y, lo.z); GL.Vertex3(hi.x, lo.y, hi.z);
                GL.Vertex3(hi.x, lo.y, hi.z); GL.Vertex3(lo.x, lo.y, hi.z);
                GL.Vertex3(lo.x, lo.y, hi.z); GL.Vertex3(lo.x, lo.y, lo.z);
                // Top
                GL.Vertex3(lo.x, hi.y, lo.z); GL.Vertex3(hi.x, hi.y, lo.z);
                GL.Vertex3(hi.x, hi.y, lo.z); GL.Vertex3(hi.x, hi.y, hi.z);
                GL.Vertex3(hi.x, hi.y, hi.z); GL.Vertex3(lo.x, hi.y, hi.z);
                GL.Vertex3(lo.x, hi.y, hi.z); GL.Vertex3(lo.x, hi.y, lo.z);
                // Verticals
                GL.Vertex3(lo.x, lo.y, lo.z); GL.Vertex3(lo.x, hi.y, lo.z);
                GL.Vertex3(hi.x, lo.y, lo.z); GL.Vertex3(hi.x, hi.y, lo.z);
                GL.Vertex3(hi.x, lo.y, hi.z); GL.Vertex3(hi.x, hi.y, hi.z);
                GL.Vertex3(lo.x, lo.y, hi.z); GL.Vertex3(lo.x, hi.y, hi.z);
            }

            // Forward direction arrow on the grid floor
            DrawForwardArrow();

            GL.End();
            GL.PopMatrix();
        }

        private void DrawForwardArrow()
        {
            // End the current LINES batch — the arrow uses TRIANGLES/QUADS
            GL.End();

            Vector3 gridCenter = new Vector3(
                _gridSize.x * CellSize * 0.5f,
                0f,
                _gridSize.z * CellSize * 0.5f
            );

            Quaternion rot = Quaternion.Euler(0f, _forwardAngle, 0f);
            Vector3 dir = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;

            // Place arrow just outside the grid edge
            float halfExtent = Mathf.Max(_gridSize.x, _gridSize.z) * CellSize * 0.5f;
            Vector3 arrowBase = gridCenter + dir * (halfExtent + 0.5f);
            float arrowLen = 1.2f;
            float arrowWidth = 0.7f;
            float shaftWidth = 0.25f;
            float shaftLen = 0.7f;
            float y = 0.02f;

            Vector3 tip = arrowBase + dir * arrowLen;
            tip.y = y;
            arrowBase.y = y;

            // Shaft (filled quad)
            Color green = new Color(0.3f, 0.85f, 0.5f, 0.85f);
            GL.Begin(GL.QUADS);
            GL.Color(green);

            Vector3 shaftStart = arrowBase - dir * shaftLen;
            Vector3 s0 = shaftStart - right * shaftWidth * 0.5f;
            Vector3 s1 = shaftStart + right * shaftWidth * 0.5f;
            Vector3 s2 = arrowBase + right * shaftWidth * 0.5f;
            Vector3 s3 = arrowBase - right * shaftWidth * 0.5f;
            s0.y = s1.y = s2.y = s3.y = y;
            GL.Vertex3(s0.x, s0.y, s0.z);
            GL.Vertex3(s1.x, s1.y, s1.z);
            GL.Vertex3(s2.x, s2.y, s2.z);
            GL.Vertex3(s3.x, s3.y, s3.z);
            GL.End();

            // Arrowhead (filled triangle)
            GL.Begin(GL.TRIANGLES);
            GL.Color(green);
            Vector3 headLeft = arrowBase - right * arrowWidth * 0.5f;
            Vector3 headRight = arrowBase + right * arrowWidth * 0.5f;
            headLeft.y = headRight.y = y;
            GL.Vertex3(tip.x, tip.y, tip.z);
            GL.Vertex3(headLeft.x, headLeft.y, headLeft.z);
            GL.Vertex3(headRight.x, headRight.y, headRight.z);
            GL.End();

            // Resume LINES for the rest of the grid drawing
            GL.Begin(GL.LINES);
        }

        private TextMesh _frontLabel;

        private void EnsureFrontLabel()
        {
            if (_frontLabel != null) return;

            var labelObj = new GameObject("FrontLabel");
            _frontLabel = labelObj.AddComponent<TextMesh>();
            _frontLabel.text = "FRONT";
            _frontLabel.fontSize = 36;
            _frontLabel.characterSize = 0.08f;
            _frontLabel.anchor = TextAnchor.LowerCenter;
            _frontLabel.alignment = TextAlignment.Center;
            _frontLabel.color = new Color(0.3f, 0.85f, 0.5f, 0.9f);
            _frontLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _frontLabel.GetComponent<MeshRenderer>().material = _frontLabel.font.material;
        }

        private void UpdateFrontLabel()
        {
            EnsureFrontLabel();
            if (_frontLabel == null || _cam == null) return;

            Vector3 gridCenter = new Vector3(
                _gridSize.x * CellSize * 0.5f,
                0f,
                _gridSize.z * CellSize * 0.5f
            );

            Quaternion rot = Quaternion.Euler(0f, _forwardAngle, 0f);
            Vector3 dir = rot * Vector3.forward;
            float halfExtent = Mathf.Max(_gridSize.x, _gridSize.z) * CellSize * 0.5f;

            // Position above the arrow tip
            Vector3 pos = gridCenter + dir * (halfExtent + 1.8f);
            pos.y = 0.6f;
            _frontLabel.transform.position = pos;

            // Face the camera but stay upright
            Vector3 lookDir = _cam.transform.position - pos;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.01f)
                _frontLabel.transform.rotation = Quaternion.LookRotation(-lookDir, Vector3.up);
        }

        // ------------------------------------------------------------------
        // BFS connectivity validation
        // ------------------------------------------------------------------

        /// <summary>
        /// Validates that all placed parts form a single connected group
        /// via face adjacency. Returns true if valid or grid is empty.
        /// </summary>
        public bool ValidateConnectivity()
        {
            if (_grid.Count == 0) return true;

            // Collect unique origin cells
            var origins = new HashSet<Vector3Int>();
            foreach (var kv in _grid)
            {
                origins.Add(kv.Value.origin);
            }

            if (origins.Count <= 1) return true;

            // BFS from the first origin
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();

            // Get all occupied cells
            var allCells = new HashSet<Vector3Int>(_grid.Keys);

            // Start from the first occupied cell
            var enumerator = allCells.GetEnumerator();
            enumerator.MoveNext();
            Vector3Int startCell = enumerator.Current;
            enumerator.Dispose();

            queue.Enqueue(startCell);
            visited.Add(startCell);

            Vector3Int[] directions = new Vector3Int[]
            {
                new Vector3Int(1, 0, 0),
                new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 1, 0),
                new Vector3Int(0, -1, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(0, 0, -1)
            };

            while (queue.Count > 0)
            {
                Vector3Int current = queue.Dequeue();

                for (int i = 0; i < directions.Length; i++)
                {
                    Vector3Int neighbor = current + directions[i];
                    if (allCells.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited.Count == allCells.Count;
        }

        /// <summary>
        /// Full validation for the vehicle before entering combat.
        /// </summary>
        public bool ValidateVehicle(out string error)
        {
            error = null;

            if (_grid.Count == 0)
            {
                error = "Vehicle has no parts.";
                return false;
            }

            // Must have a control module
            if (!HasControlModule())
            {
                error = "Vehicle requires a control module.";
                return false;
            }

            // Must have propulsion in the base layer
            if (!HasPropulsionInBaseLayer())
            {
                error = "Vehicle requires at least one propulsion part in the base layer (layer 0).";
                return false;
            }

            // Must be connected
            if (!ValidateConnectivity())
            {
                error = "Vehicle has disconnected parts. All parts must be connected.";
                return false;
            }

            // Budget check
            if (_budget > 0 && _usedBudget > _budget)
            {
                error = $"Vehicle exceeds budget: {_usedBudget}/{_budget}.";
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Undo / Redo
        // ------------------------------------------------------------------

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            int lastIndex = _undoStack.Count - 1;
            BuildAction action = _undoStack[lastIndex];
            _undoStack.RemoveAt(lastIndex);

            if (action.type == BuildActionType.Place)
            {
                // Reverse a placement: remove the part
                RemovePartInternal(action.placed);
            }
            else if (action.type == BuildActionType.Remove)
            {
                // Reverse a removal: re-place the part
                PlacePartInternal(action.placed);
            }

            _redoStack.Add(action);
            OnBudgetChanged?.Invoke(_usedBudget, _budget);
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            int lastIndex = _redoStack.Count - 1;
            BuildAction action = _redoStack[lastIndex];
            _redoStack.RemoveAt(lastIndex);

            if (action.type == BuildActionType.Place)
            {
                PlacePartInternal(action.placed);
            }
            else if (action.type == BuildActionType.Remove)
            {
                RemovePartInternal(action.placed);
            }

            _undoStack.Add(action);
            OnBudgetChanged?.Invoke(_usedBudget, _budget);
        }

        private void PlacePartInternal(PlacedPart placed)
        {
            for (int dx = 0; dx < placed.partData.size.x; dx++)
            for (int dy = 0; dy < placed.partData.size.y; dy++)
            for (int dz = 0; dz < placed.partData.size.z; dz++)
            {
                Vector3Int cell = placed.origin + new Vector3Int(dx, dy, dz);
                _grid[cell] = placed;
            }

            _usedBudget += placed.partData.cost;
            CreatePartVisual(placed);
        }

        private void RemovePartInternal(PlacedPart placed)
        {
            for (int dx = 0; dx < placed.partData.size.x; dx++)
            for (int dy = 0; dy < placed.partData.size.y; dy++)
            for (int dz = 0; dz < placed.partData.size.z; dz++)
            {
                Vector3Int cell = placed.origin + new Vector3Int(dx, dy, dz);
                _grid.Remove(cell);
            }

            _usedBudget -= placed.partData.cost;
            if (_usedBudget < 0) _usedBudget = 0;
            DestroyPartVisual(placed.origin);
        }

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Select a part to place. Pass null to deselect.
        /// </summary>
        public void SelectPart(PartData part)
        {
            _selectedPart = part;
        }

        /// <summary>
        /// Clear the entire grid.
        /// </summary>
        public void ClearGrid()
        {
            var origins = new List<Vector3Int>(_partObjects.Keys);
            for (int i = 0; i < origins.Count; i++)
                DestroyPartVisual(origins[i]);

            _grid.Clear();
            _partObjects.Clear();
            _usedBudget = 0;
            _undoStack.Clear();
            _redoStack.Clear();
            OnBudgetChanged?.Invoke(_usedBudget, _budget);
        }

        /// <summary>
        /// Export the current build to VehicleData for saving / spawning.
        /// </summary>
        public VehicleData ExportVehicleData(string name = "Player Vehicle")
        {
            var data = new VehicleData();
            data.name = name;
            data.domain = _domain;
            data.forwardAngle = _forwardAngle;

            // Collect unique origins
            var origins = new HashSet<Vector3Int>();
            foreach (var kv in _grid)
            {
                origins.Add(kv.Value.origin);
            }

            foreach (Vector3Int origin in origins)
            {
                PlacedPart placed = _grid[origin];
                int[] face = null;

                // Save armorFace from the visual PartNode if it's armor
                if (IsArmorPart(placed.partData) && _partObjects.TryGetValue(origin, out var go))
                {
                    var node = go.GetComponent<PartNode>();
                    if (node != null)
                        face = new int[] { node.armorFace.x, node.armorFace.y, node.armorFace.z };
                }

                data.parts.Add(new PartEntry(placed.partData.id,
                    new int[] { origin.x, origin.y, origin.z }, face));
            }

            return data;
        }

        /// <summary>
        /// Import a VehicleData into the builder grid.
        /// </summary>
        public void ImportVehicleData(VehicleData data)
        {
            if (data == null) return;

            ClearGrid();

            _domain = data.domain;
            _gridSize = GetGridSize(_domain);
            _forwardAngle = data.forwardAngle;

            for (int i = 0; i < data.parts.Count; i++)
            {
                PartEntry entry = data.parts[i];
                PartData partData = PartRegistry.Instance?.GetPart(entry.id);
                if (partData == null) continue;

                Vector3Int origin = new Vector3Int(
                    entry.gridPosition.Length > 0 ? entry.gridPosition[0] : 0,
                    entry.gridPosition.Length > 1 ? entry.gridPosition[1] : 0,
                    entry.gridPosition.Length > 2 ? entry.gridPosition[2] : 0
                );

                TryPlacePart(partData, origin);
            }
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        public int GetUsedBudget() => _usedBudget;
        public int GetBudget() => _budget;
        public int GetPartCount() => GetUniqueOrigins().Count;
        public string GetDomain() => _domain;
        public Vector3Int GetGridSize() => _gridSize;
        public float GetForwardAngle() => _forwardAngle;
        public int GetCurrentLayer() => _currentLayer;

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool InBounds(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < _gridSize.x
                && cell.y >= 0 && cell.y < _gridSize.y
                && cell.z >= 0 && cell.z < _gridSize.z;
        }

        private bool IsValidCell(Vector3Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.z >= 0;
        }

        private bool CanPlaceAt(PartData part, Vector3Int origin)
        {
            if (part == null) return false;
            if (!part.IsValidForDomain(_domain)) return false;
            if (_budget > 0 && _usedBudget + part.cost > _budget) return false;
            if (part.IsControlModule() && HasControlModule()) return false;

            bool isArmor = IsArmorPart(part);

            for (int dx = 0; dx < part.size.x; dx++)
            for (int dy = 0; dy < part.size.y; dy++)
            for (int dz = 0; dz < part.size.z; dz++)
            {
                Vector3Int cell = origin + new Vector3Int(dx, dy, dz);
                if (!InBounds(cell)) return false;
                if (_grid.ContainsKey(cell)) return false;

                // Rule A: cannot place anything on top of armor
                Vector3Int below = cell + new Vector3Int(0, -1, 0);
                if (_grid.TryGetValue(below, out var belowPart) && IsArmorPart(belowPart.partData))
                    return false;

                // Rule B: armor cannot be stacked vertically on other armor
                if (isArmor)
                {
                    Vector3Int above = cell + Vector3Int.up;
                    Vector3Int belowA = cell + Vector3Int.down;
                    if (_grid.TryGetValue(above, out var aboveAdj) && IsArmorPart(aboveAdj.partData))
                        return false;
                    if (_grid.TryGetValue(belowA, out var belowAdj) && IsArmorPart(belowAdj.partData))
                        return false;
                }
            }

            // Rule C: armor must attach to at least one existing non-armor part
            if (isArmor)
            {
                bool hasNeighbor = false;
                for (int dx = 0; dx < part.size.x && !hasNeighbor; dx++)
                for (int dy = 0; dy < part.size.y && !hasNeighbor; dy++)
                for (int dz = 0; dz < part.size.z && !hasNeighbor; dz++)
                {
                    Vector3Int cell = origin + new Vector3Int(dx, dy, dz);
                    Vector3Int[] neighbors = {
                        cell + Vector3Int.right, cell + Vector3Int.left,
                        cell + Vector3Int.up, cell + Vector3Int.down,
                        cell + new Vector3Int(0, 0, 1), cell + new Vector3Int(0, 0, -1)
                    };
                    for (int n = 0; n < neighbors.Length; n++)
                        if (_grid.TryGetValue(neighbors[n], out var adj) && !IsArmorPart(adj.partData))
                            hasNeighbor = true;
                }
                if (!hasNeighbor) return false;
            }

            // Water/air propulsion must attach to side (X or Z face) of a structural block
            if (IsWaterOrAirPropulsion(part))
            {
                bool hasSideStructure = false;
                for (int dx = 0; dx < part.size.x && !hasSideStructure; dx++)
                for (int dy = 0; dy < part.size.y && !hasSideStructure; dy++)
                for (int dz = 0; dz < part.size.z && !hasSideStructure; dz++)
                {
                    Vector3Int cell = origin + new Vector3Int(dx, dy, dz);
                    Vector3Int[] sideAdj = {
                        cell + Vector3Int.right, cell + Vector3Int.left,
                        cell + new Vector3Int(0, 0, 1), cell + new Vector3Int(0, 0, -1)
                    };
                    for (int n = 0; n < sideAdj.Length; n++)
                        if (_grid.TryGetValue(sideAdj[n], out var adj) && IsStructuralPart(adj.partData))
                            hasSideStructure = true;
                }
                if (!hasSideStructure) return false;
            }

            return true;
        }

        private static bool IsArmorPart(PartData part)
        {
            return part != null && string.Equals(part.category, "defense", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStructuralPart(PartData part)
        {
            return part != null && string.Equals(part.category, "structural", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWaterOrAirPropulsion(PartData part)
        {
            if (part == null) return false;
            if (!string.Equals(part.category, "propulsion", StringComparison.OrdinalIgnoreCase))
                return false;
            // Ground propulsion (wheels, tracks) and masts are exempt
            string sub = part.subcategory?.ToLowerInvariant() ?? "";
            return sub != "wheel" && sub != "track" && sub != "mast";
        }

        private bool HasControlModule()
        {
            foreach (var kv in _grid)
            {
                if (kv.Value.partData.IsControlModule())
                    return true;
            }
            return false;
        }

        private bool HasPropulsionInBaseLayer()
        {
            foreach (var kv in _grid)
            {
                if (kv.Key.y != 0) continue;
                if (string.Equals(kv.Value.partData.category, "propulsion", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private HashSet<Vector3Int> GetUniqueOrigins()
        {
            var origins = new HashSet<Vector3Int>();
            foreach (var kv in _grid)
                origins.Add(kv.Value.origin);
            return origins;
        }

        private Vector3 GridToWorld(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * CellSize,
                gridPos.y * CellSize,
                gridPos.z * CellSize
            );
        }

        private static Vector3Int GetGridSize(string domain)
        {
            if (DomainGridSizes.TryGetValue(domain, out Vector3Int size))
                return size;
            return new Vector3Int(7, 5, 7); // fallback
        }

        private static bool IsPointerOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        // ------------------------------------------------------------------
        // Cleanup
        // ------------------------------------------------------------------

        private void OnDestroy()
        {
            if (_ghostPreview != null) Destroy(_ghostPreview);
            if (_hoverHighlight != null) Destroy(_hoverHighlight);
            if (_gridLineMaterial != null) DestroyImmediate(_gridLineMaterial);
            if (_cameraRig != null) Destroy(_cameraRig.gameObject);
            if (_frontLabel != null) Destroy(_frontLabel.gameObject);
        }

        // ==================================================================
        // Nested types
        // ==================================================================

        private class PlacedPart
        {
            public PartData partData;
            public Vector3Int origin;

            public PlacedPart(PartData data, Vector3Int origin)
            {
                this.partData = data;
                this.origin = origin;
            }
        }

        private enum BuildActionType
        {
            Place,
            Remove
        }

        private class BuildAction
        {
            public BuildActionType type;
            public PlacedPart placed;

            public BuildAction(BuildActionType type, PlacedPart placed)
            {
                this.type = type;
                this.placed = placed;
            }
        }
    }
}
