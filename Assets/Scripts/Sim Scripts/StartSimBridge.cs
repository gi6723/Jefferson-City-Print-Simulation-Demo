using UnityEngine;
using mumachiningmr.xr3dprintersimulator;

public class StartSimBridge : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private GcodeSelectionController selection;
    [SerializeField] private PrintSimulation_Extruder extruder;

    [Header("UI Panels")]
    [Tooltip("The panel shown before simulation starts (Gcode selection UI).")]
    [SerializeField] private GameObject selectionPanel;
    [Tooltip("The panel shown during simulation (speed slider + stop button).")]
    [SerializeField] private GameObject simControlPanel;

    [Header("Debug (optional)")]
    [SerializeField] private HudLogger hud;

    [Header("Mode")]
    [SerializeField] private bool useGradual = true;

    private void Awake()
    {
        Debug.Log($"[StartSimBridge] Awake on '{gameObject.name}'. " +
                  $"selection={(selection ? selection.name : "NULL")}, " +
                  $"extruder={(extruder ? extruder.name : "NULL")}, " +
                  $"mode={(useGradual ? "Gradual" : "Rapid")}");

        // Start with selection UI visible, sim control hidden
        if (selectionPanel != null) selectionPanel.SetActive(true);
        if (simControlPanel != null) simControlPanel.SetActive(false);
    }

    public void OnStartClicked()
    {
        Debug.Log("[StartSimBridge] OnStartClicked()");
        hud?.Log("Start pressed.");

        if (selection == null) { Debug.LogError("[StartSimBridge] ERROR: selection null"); return; }
        if (extruder == null)  { Debug.LogError("[StartSimBridge] ERROR: extruder null"); return; }

        var gcode = selection.GetSelectedGcode();
        if (gcode == null) { Debug.LogWarning("[StartSimBridge] WARN: no gcode selected"); hud?.Log("No gcode selected."); return; }

        var selectedName = selection.GetSelectedName();
        Debug.Log($"[StartSimBridge] Selected '{selectedName}' chars={gcode.text?.Length ?? 0}");

        extruder.SetGcodeText(gcode.text, selectedName);

        // Swap UI panels
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (simControlPanel != null) simControlPanel.SetActive(true);

        if (useGradual)
            extruder.tryGradualSimulation();
        else
            extruder.tryAutoFinishSimulation();
    }

    /// <summary>
    /// Called by the Stop button in the sim control panel.
    /// Stops simulation, destroys all filament, returns to selection UI.
    /// </summary>
    public void OnStopClicked()
    {
        Debug.Log("[StartSimBridge] OnStopClicked()");
        hud?.Log("Stop pressed.");

        if (extruder != null)
            extruder.StopSimulation();

        // Swap back to selection UI
        if (simControlPanel != null) simControlPanel.SetActive(false);
        if (selectionPanel != null)  selectionPanel.SetActive(true);
    }
}