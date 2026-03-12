using UnityEngine;
using mumachiningmr.xr3dprintersimulator;

public class StartSimBridge : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private GcodeSelectionController selection;
    [SerializeField] private PrintSimulation_Extruder extruder;

    [Header("Debug (optional)")]
    [SerializeField] private HudLogger hud;

    [Header("Mode")]
    [SerializeField] private bool useGradual = false;

    private void Awake()
    {
        Debug.Log($"[StartSimBridge] Awake on '{gameObject.name}'. selection={(selection ? selection.name : "NULL")}, extruder={(extruder ? extruder.name : "NULL")}, hud={(hud ? hud.name : "NULL")}, mode={(useGradual ? "Gradual" : "Rapid")}");
    }

    public void OnStartClicked()
    {
        Debug.Log("[StartSimBridge] OnStartClicked()");
        hud?.Log("Start pressed.");

        if (selection == null) { Debug.LogError("[StartSimBridge] ERROR: selection null"); hud?.Log("ERROR: selection null"); return; }
        if (extruder == null) { Debug.LogError("[StartSimBridge] ERROR: extruder null"); hud?.Log("ERROR: extruder null"); return; }

        var gcode = selection.GetSelectedGcode();
        if (gcode == null) { Debug.LogWarning("[StartSimBridge] WARN: no gcode selected"); hud?.Log("WARN: no gcode selected"); return; }

        var selectedName = selection.GetSelectedName();
        Debug.Log($"[StartSimBridge] Selected '{selectedName}' TextAsset='{gcode.name}' chars={gcode.text?.Length ?? 0}");
        hud?.Log($"Selected: {selectedName} ({gcode.name}) chars={gcode.text?.Length ?? 0}");

        extruder.SetGcodeText(gcode.text, selectedName);
        Debug.Log("[StartSimBridge] Gcode loaded into extruder (SetGcodeText called).");
        hud?.Log("Gcode loaded into extruder.");

        if (useGradual)
        {
            Debug.Log("[StartSimBridge] Starting GRADUAL simulation...");
            hud?.Log("Starting gradual sim...");
            extruder.tryGradualSimulation();
        }
        else
        {
            Debug.Log("[StartSimBridge] Starting RAPID simulation...");
            hud?.Log("Starting rapid sim...");
            extruder.tryAutoFinishSimulation();
        }
    }
}