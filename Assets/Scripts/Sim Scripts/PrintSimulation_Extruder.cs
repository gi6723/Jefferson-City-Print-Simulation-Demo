using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace mumachiningmr.xr3dprintersimulator
{
    public class PrintSimulation_Extruder : MonoBehaviour
    {
        [Header("Printer Parts (from PrinterRoot prefab)")]
        [SerializeField] private Transform printBed;
        [SerializeField] private Transform extruderHead;

        [Header("Visualization")]
        [Tooltip("Thickness of the filament cubes in meters. Original working value was 0.00174.")]
        [SerializeField] private float filamentThickness = 0.00174f;

        [Tooltip("Assign an MRTK/Graphics Tools material here (e.g. MRTK_Standard_White). " +
                 "Applied to each cube's MeshRenderer. Leave empty to use Unity default.")]
        [SerializeField] private Material filamentMaterial;

        [Tooltip("Optional: parent to keep the hierarchy tidy. If null, will parent to printBed.")]
        [SerializeField] private Transform filamentParent;

        [Tooltip("Offset in meters from the printBed pivot UP to the actual bed surface.")]
        [SerializeField] private float bedSurfaceOffsetMeters = 0.0f;

        [Tooltip("Additional Y offset applied ONLY to the extruder head visual, independent of filament origin. " +
                 "Use this to align the nozzle tip with where filament spawns if the nozzle pivot is off.")]
        [SerializeField] private float extruderHeadOffsetMeters = 0.0f;
        [SerializeField] private float extruderHeadOffsetZMeters = 0.0f;
        [SerializeField] private float extruderHeadOffsetXMeters = 0.0f;

        [Tooltip("How many completed layers to keep visible at once. Older layers beyond this count " +
                 "are destroyed to keep GPU memory low. 5 is a safe starting value for HoloLens 2.")]
        [SerializeField] private int visibleLayerBudget = 5;

        [Header("Simulation")]
        [SerializeField] private float printSpeed = 5f;
        [Header("Speed Slider Range")]
        [SerializeField] private float sliderSpeedMin = 0.5f;
        [SerializeField] private float sliderSpeedMax = 6.0f;

        [Tooltip("Rapid mode: how many gcode lines to process per frame. Higher = faster.")]
        [SerializeField] private int rapidLinesPerFrame = 200;

        [Header("GCode Source")]
        [SerializeField] private TextAsset initialGcode;
        [SerializeField] private string initialStreamingAssetRelativePath = "";

        [Obsolete("Use SetGcodeText or SetGcodeFromStreamingAssets instead.")]
        [SerializeField] private string gcodepath = "";
        [Obsolete("Use SetGcodeText or SetGcodeFromStreamingAssets instead.")]
        [SerializeField] private string fileName = "";

        [Header("Debug / Visibility")]
        [Tooltip("Force spawned filament to this Unity layer index (0-31). -1 = don't force.")]
        [SerializeField] private int forceFilamentLayer = -1;

        [Tooltip("Log every N segments (0 = disable).")]
        [SerializeField] private int logEveryNSegments = 1000;

        [SerializeField] private bool logModeChanges = true;

        [Header("GCode Coordinate Mapping")]
        [Tooltip("Most slicers place gcode origin at a bed corner. Check this to center it on the bed.")]
        [SerializeField] private bool centerGcodeOnBed = true;

        [Tooltip("Bed size in MILLIMETERS. Ender 2 Pro = 165 x 165.")]
        [SerializeField] private Vector2 bedSizeMm = new Vector2(165f, 165f);

        [Tooltip("Tiny Y lift to avoid z-fighting between filament and bed surface.")]
        [SerializeField] private float bedLiftMeters = 0.0005f;

        // ---- Per-layer cube tracking ----
        private readonly Dictionary<int, List<GameObject>> _layerCubes = new();
        private readonly List<int> _layerOrder = new(); // ordered so we can evict oldest first
        private readonly List<GameObject> filamentExtrusions = new(); // all cubes for showLayer

        // ---- GCode state ----
        private string selectedDisplayName = "None";
        private string[] gcodeLines = Array.Empty<string>();
        private int gcodeLineIndex = 0;

        private bool isMetric = true;
        private bool isAbsolute = true;

        private Vector3 origin;
        private Vector3 extruderCoordinate;
        private Vector3 fromCoord;
        private Vector3 toCoord;

        private float stepValue = 0f;
        private float lastGcodeZmm = float.NaN;

        private int linecounter = 0;
        private int totalLayers = 0;
        private int currentLayer = 1;
        private int segmentCount = 0;

        private bool isRapidSim = false;
        private bool isGradualSim = false;
        private bool isRunning = false;
        private bool modelComplete = false;

        // -------------------------------------------------------------------------
        // Awake
        // -------------------------------------------------------------------------

        private void Awake()
        {
            Debug.Log($"[PrintSimulation_Extruder] Awake on '{gameObject.name}'. " +
                      $"printBed={(printBed ? printBed.name : "NULL")}, " +
                      $"extruderHead={(extruderHead ? extruderHead.name : "NULL")}, " +
                      $"filamentParent={(filamentParent ? filamentParent.name : "NULL")}");

            if (initialGcode != null)
            {
                Debug.Log($"[PrintSimulation_Extruder] Initial gcode TextAsset: {initialGcode.name}");
                SetGcodeText(initialGcode.text, initialGcode.name);
            }
            else if (!string.IsNullOrWhiteSpace(initialStreamingAssetRelativePath))
            {
                StartCoroutine(SetGcodeFromStreamingAssets(
                    initialStreamingAssetRelativePath,
                    System.IO.Path.GetFileName(initialStreamingAssetRelativePath)
                ));
            }
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        public string getGcodeFilepath() => selectedDisplayName;

        public bool setGCodeFilepath(string newPath)
        {
            Debug.LogWarning($"[PrintSimulation_Extruder] setGCodeFilepath deprecated.");
            selectedDisplayName = newPath;
            return false;
        }

        public int getTotalLayers() => totalLayers;

        public void setPrintSpeed(float newSpeed)
        {
            float mapped;
            if (newSpeed <= 1.0f)
                mapped = Mathf.Lerp(sliderSpeedMin, sliderSpeedMax, Mathf.Clamp01(newSpeed));
            else
                mapped = Mathf.Max(0.05f, newSpeed * 0.02f);
            if (Mathf.Approximately(mapped, printSpeed)) return;
            printSpeed = mapped;
            Debug.Log($"[PrintSimulation_Extruder] setPrintSpeed in={newSpeed:0.000} -> {printSpeed:0.000} m/s");
        }

        public void StopSimulation()
        {
            Debug.Log("[PrintSimulation_Extruder] StopSimulation() called.");
            StopAllCoroutines();
            printSpeed = sliderSpeedMin;
            clearPrintData(keepLoadedGcode: false);
            // nozzle Y/Z untouched — only X is driven during sim
        }

        public void showLayer(int newLayer)
        {
            currentLayer = Mathf.Clamp(newLayer, 0, totalLayers);
            foreach (var go in filamentExtrusions)
            {
                if (go == null) continue;
                if (go.name.Length > 1 && int.TryParse(go.name.Substring(1), out int layer))
                    go.SetActive(layer <= currentLayer);
            }
        }

        public void tryGradualSimulation()
        {
            if (isRunning) { Debug.LogWarning("[PrintSimulation_Extruder] Already running."); return; }
            if (modelComplete) { showLayer(totalLayers); return; }
            if (gcodeLines == null || gcodeLines.Length == 0) { Debug.LogWarning("[PrintSimulation_Extruder] No gcode loaded."); return; }
            Debug.Log($"[PrintSimulation_Extruder] Starting GRADUAL sim '{selectedDisplayName}'");
            clearPrintData(keepLoadedGcode: true);
            StartCoroutine(initiateGradualModelSimulation());
            isRunning = true;
        }

        public void tryAutoFinishSimulation()
        {
            if (isRunning) { Debug.LogWarning("[PrintSimulation_Extruder] Already running."); return; }
            if (modelComplete) { showLayer(totalLayers); return; }
            if (gcodeLines == null || gcodeLines.Length == 0) { Debug.LogWarning("[PrintSimulation_Extruder] No gcode loaded."); return; }
            Debug.Log($"[PrintSimulation_Extruder] Starting RAPID sim '{selectedDisplayName}'");
            clearPrintData(keepLoadedGcode: true);
            StartCoroutine(initiateRapidModelSimulation());
            isRunning = true;
        }

        public string GetSelectedGcodeDisplayName() => selectedDisplayName;

        public void SetGcodeText(string gcodeText, string displayName)
        {
            selectedDisplayName = string.IsNullOrWhiteSpace(displayName) ? "GCode" : displayName;
            gcodeLines = SplitLines(gcodeText);
            gcodeLineIndex = 0;
            totalLayers = EstimateTotalLayers(gcodeLines);
            modelComplete = false;
            clearPrintData(keepLoadedGcode: true);
            Debug.Log($"[PrintSimulation_Extruder] Loaded '{selectedDisplayName}' lines={gcodeLines.Length} layers≈{totalLayers}");
        }

        public IEnumerator SetGcodeFromStreamingAssets(string relativePath, string displayName)
        {
            var url = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
#if UNITY_WSA && !UNITY_EDITOR
            url = Application.streamingAssetsPath.TrimEnd('/') + "/" + relativePath.TrimStart('/');
#endif
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
#if UNITY_2022_1_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            { Debug.LogError($"[PrintSimulation_Extruder] Failed to load '{relativePath}': {req.error}"); yield break; }
            SetGcodeText(req.downloadHandler.text, displayName);
        }

        // -------------------------------------------------------------------------
        // Simulation coroutines
        // -------------------------------------------------------------------------

        private IEnumerator initiateGradualModelSimulation()
        {
            isGradualSim = true; isRapidSim = false;
            locateOrigin();
            // nozzle Y/Z untouched — only X is driven during sim
            fromCoord = origin; toCoord = origin;
            Debug.Log($"[PrintSimulation_Extruder] Gradual sim begin. origin={origin} speed={printSpeed}");

            while (gcodeLineIndex < gcodeLines.Length)
            {
                processNextGCodeLine();
                // Only drive nozzle local X — it slides on the X rail only.
                // Y and Z in local space never change (bed lowers in real life, not in sim).
                if (extruderHead != null)
                {
                    // Drive local X to track filament, drive local Y via extruderHeadOffsetMeters
                    Transform root = extruderHead.parent != null ? extruderHead.parent : extruderHead;
                    Vector3 localTarget = root.InverseTransformPoint(extruderCoordinate);
                    Vector3 localCurrent = extruderHead.localPosition;
                    float targetLocalX = Mathf.MoveTowards(localCurrent.x, localTarget.x, printSpeed * Time.deltaTime);
                    extruderHead.localPosition = new Vector3(targetLocalX + extruderHeadOffsetXMeters, localTarget.y + extruderHeadOffsetMeters, localTarget.z + extruderHeadOffsetZMeters);
                }
                yield return null;
            }
            FinalizeSim();
        }

        private IEnumerator initiateRapidModelSimulation()
        {
            isGradualSim = false; isRapidSim = true;
            locateOrigin();
            // nozzle Y/Z untouched — only X is driven during sim
            fromCoord = origin; toCoord = origin;
            Debug.Log($"[PrintSimulation_Extruder] Rapid sim begin. origin={origin} linesPerFrame={rapidLinesPerFrame}");

            while (gcodeLineIndex < gcodeLines.Length)
            {
                int n = Mathf.Max(1, rapidLinesPerFrame);
                for (int i = 0; i < n && gcodeLineIndex < gcodeLines.Length; i++)
                    processNextGCodeLine();
                yield return null;
            }
            FinalizeSim();
        }

        private void FinalizeSim()
        {
            // nozzle Y/Z untouched — only X is driven during sim
            totalLayers = Mathf.Max(totalLayers, currentLayer);
            isRunning = false;
            modelComplete = true;
            Debug.Log($"[PrintSimulation_Extruder] Simulation complete. segments={segmentCount} layers={_layerOrder.Count} liveCubes={filamentExtrusions.Count}");
        }

        // -------------------------------------------------------------------------
        // GCode line processing
        // -------------------------------------------------------------------------

        public void processNextGCodeLine()
        {
            if (gcodeLineIndex >= gcodeLines.Length) return;

            string currentLine = gcodeLines[gcodeLineIndex++];
            linecounter++;
            if (string.IsNullOrWhiteSpace(currentLine)) return;

            // Layer marker: ;LAYER:12
            int layerIdx = currentLine.IndexOf("LAYER:", StringComparison.OrdinalIgnoreCase);
            if (layerIdx >= 0)
            {
                var tail = currentLine.Substring(layerIdx + "LAYER:".Length).Trim();
                if (int.TryParse(tail, out int layer))
                    currentLayer = Mathf.Max(1, layer + 1);
            }

            int commentIdx = currentLine.IndexOf(';');
            if (commentIdx >= 0) currentLine = currentLine.Substring(0, commentIdx);
            currentLine = currentLine.Trim();
            if (currentLine.Length == 0) return;

            if (currentLine.StartsWith("G20")) { if (logModeChanges) Debug.Log("[PrintSimulation_Extruder] Mode: inches"); isMetric = false; return; }
            if (currentLine.StartsWith("G21")) { if (logModeChanges) Debug.Log("[PrintSimulation_Extruder] Mode: mm"); isMetric = true; return; }
            if (currentLine.StartsWith("G90")) { if (logModeChanges) Debug.Log("[PrintSimulation_Extruder] Mode: absolute"); isAbsolute = true; return; }
            if (currentLine.StartsWith("G91")) { if (logModeChanges) Debug.Log("[PrintSimulation_Extruder] Mode: relative"); isAbsolute = false; return; }

            bool isG0 = currentLine.StartsWith("G0");
            bool isG1 = currentLine.StartsWith("G1");
            if (!isG0 && !isG1) return;

            bool hasE = currentLine.Contains(" E") || currentLine.Contains("E");

            if (TryParseAxis(currentLine, 'Z', out float zmm))
            {
                if (!float.IsNaN(lastGcodeZmm) && zmm > lastGcodeZmm + 0.0005f)
                    currentLayer++;
                lastGcodeZmm = zmm;
            }

            toCoord = parseToVector3(currentLine);

            if (!hasE || isG0)
            {
                extruderCoordinate = toCoord;
                fromCoord = toCoord;
                return;
            }

            extruderCoordinate = toCoord;

            if (logEveryNSegments > 0 && segmentCount > 0 && segmentCount % logEveryNSegments == 0)
                Debug.Log($"[PrintSimulation_Extruder] Segments={segmentCount} layer={currentLayer} " +
                          $"liveCubes={filamentExtrusions.Count} layersBudget={_layerOrder.Count}/{visibleLayerBudget}");

            spawnFilamentCube(fromCoord, toCoord, currentLayer);
            fromCoord = toCoord;
        }

        // -------------------------------------------------------------------------
        // Cube spawning with layer budget
        // -------------------------------------------------------------------------

        private void spawnFilamentCube(Vector3 from, Vector3 to, int layer)
        {

            var delta = from - to;
            if (delta.magnitude < 0.0001f) return; // skip zero-length

            segmentCount++;

            // Create cube — identical to original working implementation
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "L" + layer;

            // Remove collider
            var col = cube.GetComponent<Collider>();
            if (col) Destroy(col);

            // Apply material to MeshRenderer — Graphics Tools/Standard works on MeshRenderer
            if (filamentMaterial != null)
            {
                var mr = cube.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = filamentMaterial;
            }

            // Force Unity layer if set
            if (forceFilamentLayer >= 0 && forceFilamentLayer <= 31)
                cube.layer = forceFilamentLayer;

            // Parent
            cube.transform.SetParent(EnsureFilamentRoot(), worldPositionStays: true);

            // Orient and scale exactly like original working code
            cube.transform.up = delta;
            cube.transform.position = (from + to) / 2f;
            cube.transform.localScale = new Vector3(filamentThickness, delta.magnitude, filamentThickness);

            cube.SetActive(true);

            // First cube diagnostic — runs after creation so cube exists
            if (segmentCount == 1)
            {
                var mr = cube.GetComponent<MeshRenderer>();
                Debug.Log($"[FilamentDbg] First cube. from={from} to={to} layer={layer} " +
                          $"root='{cube.transform.parent?.name}' thickness={filamentThickness} " +
                          $"mat={(mr?.sharedMaterial != null ? mr.sharedMaterial.name : "NULL/Default")} " +
                          $"shader={(mr?.sharedMaterial?.shader != null ? mr.sharedMaterial.shader.name : "NULL")}");
            }

            // Register in layer tracking
            if (!_layerCubes.ContainsKey(layer))
            {
                _layerCubes[layer] = new List<GameObject>();
                _layerOrder.Add(layer);
            }
            _layerCubes[layer].Add(cube);
            filamentExtrusions.Add(cube);

            // Enforce budget
            EnforceLayerBudget();
        }

        private void EnforceLayerBudget()
        {
            if (visibleLayerBudget <= 0) return;

            while (_layerOrder.Count > visibleLayerBudget)
            {
                int oldestLayer = _layerOrder[0];
                _layerOrder.RemoveAt(0);

                if (_layerCubes.TryGetValue(oldestLayer, out var cubes))
                {
                    foreach (var c in cubes)
                    {
                        if (c != null)
                        {
                            filamentExtrusions.Remove(c);
                            Destroy(c);
                        }
                    }
                    cubes.Clear();
                    _layerCubes.Remove(oldestLayer);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private Transform EnsureFilamentRoot()
        {
            if (filamentParent != null) return filamentParent;
            filamentParent = printBed != null ? printBed : transform;
            return filamentParent;
        }

        private void locateOrigin()
        {
            var bedPos = printBed != null ? printBed.position : transform.position;
            origin = new Vector3(bedPos.x, bedPos.y + bedSurfaceOffsetMeters, bedPos.z);
            stepValue = origin.y + bedLiftMeters;
            extruderCoordinate = origin;
            fromCoord = origin;
            toCoord = origin;
            lastGcodeZmm = float.NaN;
            currentLayer = 1;
            Debug.Log($"[PrintSimulation_Extruder] locateOrigin -> bedPivot={bedPos} offset={bedSurfaceOffsetMeters} origin={origin} stepY={stepValue:0.0000}");
        }

        private bool clearPrintData(bool keepLoadedGcode = false)
        {
            Debug.Log($"[PrintSimulation_Extruder] clearPrintData cubes={filamentExtrusions.Count}");
            isRapidSim = false; isGradualSim = false; isRunning = false; modelComplete = false;
            segmentCount = 0;

            foreach (var go in filamentExtrusions)
                if (go != null) Destroy(go);
            filamentExtrusions.Clear();
            _layerCubes.Clear();
            _layerOrder.Clear();

            locateOrigin();

            if (!keepLoadedGcode)
            {
                gcodeLines = Array.Empty<string>();
                gcodeLineIndex = 0;
                selectedDisplayName = "None";
            }
            else
            {
                gcodeLineIndex = 0;
            }
            return true;
        }

        private Vector3 parseToVector3(string line)
        {
            float gx = float.NaN, gy = float.NaN, gz = float.NaN;
            TryParseAxis(line, 'X', out gx);
            TryParseAxis(line, 'Y', out gy);
            TryParseAxis(line, 'Z', out gz);

            if (centerGcodeOnBed)
            {
                if (!float.IsNaN(gx)) gx -= bedSizeMm.x * 0.5f;
                if (!float.IsNaN(gy)) gy -= bedSizeMm.y * 0.5f;
            }

            float wx = float.NaN, wy = float.NaN, wz = float.NaN;
            if (!float.IsNaN(gy)) wx = convertToWorldUnits(gy);
            if (!float.IsNaN(gx)) wz = convertToWorldUnits(gx);

            if (!float.IsNaN(gz))
            {
                wy = (origin.y + bedLiftMeters) + convertToWorldUnits(gz);
                stepValue = wy;
            }
            else { wy = stepValue; }

            // Transform gcode offsets through PrinterRoot's rotation so QR-anchor
            // orientation is respected. gcode Y -> local X axis, gcode X -> local Z axis
            // (matches the working world-axis mapping when rotation is identity).
            Transform root = transform.parent != null ? transform.parent : transform;

            if (isAbsolute)
            {
                Vector3 pos = origin;
                if (!float.IsNaN(wx)) pos += root.TransformDirection(wx, 0f, 0f);
                if (!float.IsNaN(wz)) pos += root.TransformDirection(0f, 0f, wz);
                pos.y = float.IsNaN(wy) ? fromCoord.y : wy;
                return pos;
            }
            else
            {
                Vector3 delta = Vector3.zero;
                if (!float.IsNaN(wx)) delta += root.TransformDirection(wx, 0f, 0f);
                if (!float.IsNaN(wz)) delta += root.TransformDirection(0f, 0f, wz);
                delta.y = float.IsNaN(wy) ? 0f : (wy - fromCoord.y);
                return fromCoord + delta;
            }
        }

        private static bool TryParseAxis(string line, char axis, out float value)
        {
            value = float.NaN;
            int idx = line.IndexOf(axis);
            if (idx < 0) return false;
            idx++;
            int end = idx;
            while (end < line.Length && (char.IsDigit(line[end]) || line[end] == '.' || line[end] == '-' || line[end] == '+'))
                end++;
            var token = line.Substring(idx, end - idx);
            if (float.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            { value = parsed; return true; }
            return false;
        }

        public float convertToWorldUnits(float num)
        {
            if (!isMetric) num *= 25.4f;
            return num / 1000f;
        }

        private static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        }

        private static int EstimateTotalLayers(string[] lines)
        {
            int maxLayer = 0;
            foreach (var l in lines)
            {
                if (l == null) continue;
                int idx = l.IndexOf("LAYER:", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var tail = l.Substring(idx + "LAYER:".Length).Trim();
                if (int.TryParse(tail, out int layer))
                    maxLayer = Mathf.Max(maxLayer, layer + 1);
            }
            return maxLayer;
        }

        private void OnDestroy() => clearPrintData(keepLoadedGcode: false);
    }
}