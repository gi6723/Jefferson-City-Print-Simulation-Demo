using TMPro;
using UnityEngine;
using mumachiningmr.xr3dprintersimulator;

/// <summary>
/// Attach this to the SimControlPanel GameObject.
/// Wire the Stop button's OnClick -> StartSimBridge.OnStopClicked()
/// Wire the MRTK slider's OnValueUpdated -> SimControlUI.OnSpeedSliderChanged(float)
/// </summary>
public class SimControlUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private PrintSimulation_Extruder extruder;
    [SerializeField] private StartSimBridge bridge;

    [Header("Speed Slider")]
    [Tooltip("Min multiplier shown on slider (left end). Recommend 1.")]
    [SerializeField] private float speedMin = 1f;
    [Tooltip("Max multiplier shown on slider (right end). Recommend 500.")]
    [SerializeField] private float speedMax = 500f;

    [Header("Optional Labels")]
    [SerializeField] private TMP_Text speedValueLabel;
    [SerializeField] private TMP_Text modelNameLabel;

    private void OnEnable()
    {
        // When the panel becomes visible, show the model name
        if (modelNameLabel != null && extruder != null)
            modelNameLabel.text = extruder.GetSelectedGcodeDisplayName();

        // Set default speed display
        if (speedValueLabel != null)
            speedValueLabel.text = "Speed: 200x";
    }

    /// <summary>
    /// Hook to MRTK3 slider OnValueUpdated event (passes 0..1 normalized).
    /// Maps 0..1 to speedMin.speedMax and calls setPrintSpeed.
    /// </summary>
    public void OnSpeedSliderChanged(float normalizedValue)
    {
        if (extruder == null) return;

        float speed = Mathf.Lerp(speedMin, speedMax, normalizedValue);
        extruder.setPrintSpeed(speed);

        if (speedValueLabel != null)
            speedValueLabel.text = $"Speed: {Mathf.RoundToInt(speed)}x";
    }

    /// <summary>
    /// Hook to Stop button OnClick.
    /// </summary>
    public void OnStopClicked()
    {
        if (bridge != null)
            bridge.OnStopClicked();
    }
}