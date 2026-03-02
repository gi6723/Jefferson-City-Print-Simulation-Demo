using System.Collections.Generic;
using Microsoft.MixedReality.OpenXR;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(ARMarkerManager))]
public class QRCodeContentPlacer : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("The 3D printer model prefab to spawn.")]
    [SerializeField] private GameObject printerModelPrefab;

    [Tooltip("The File Selection UI prefab (shown first).")]
    [SerializeField] private GameObject fileSelectionUIPrefab;

    [Tooltip("The Control Panel UI prefab (shown during simulation).")]
    [SerializeField] private GameObject controlPanelUIPrefab;

    [Header("Layout")]
    [Tooltip("Local-space offset of the UI panel relative to the printer model (e.g. to the left).")]
    [SerializeField] private Vector3 uiPositionOffset = new Vector3(-0.5f, 0f, 0f);

    private ARMarkerManager markerManager;

    // Spawned instances
    private GameObject spawnedPrinter;
    private GameObject spawnedFileSelectionUI;
    private GameObject spawnedControlPanelUI;

    // The QR payload we respond to
    private const string TARGET_QR_PAYLOAD = "Enjoy";

    // Track whether QR is currently visible
    private bool isSpawned = false;

    // Which UI phase we're in
    public enum DemoPhase { FileSelection, ControlPanel }
    private DemoPhase currentPhase = DemoPhase.FileSelection;

    private void Awake()
    {
        markerManager = GetComponent<ARMarkerManager>();
        markerManager.markersChanged += OnMarkersChanged;
    }

    private void OnDestroy()
    {
        if (markerManager != null)
            markerManager.markersChanged -= OnMarkersChanged;
    }

    private void OnMarkersChanged(ARMarkersChangedEventArgs args)
    {
        foreach (var marker in args.added)
            HandleAdded(marker);

        foreach (var marker in args.updated)
            HandleUpdated(marker);

        foreach (var marker in args.removed)
            HandleRemoved(marker);
    }

    private void HandleAdded(ARMarker marker)
    {
        var decoded = marker.GetDecodedString();

        if (decoded != TARGET_QR_PAYLOAD)
            return;

        if (isSpawned)
            return;

        // Spawn printer
        spawnedPrinter = Instantiate(printerModelPrefab);

        // Spawn both UIs, position them to the left of the printer
        spawnedFileSelectionUI = Instantiate(fileSelectionUIPrefab);
        spawnedControlPanelUI = Instantiate(controlPanelUIPrefab);

        ApplyPose(marker);

        // Start in FileSelection phase
        SetPhase(DemoPhase.FileSelection);

        isSpawned = true;

        Debug.Log("[QRCodeContentPlacer] Spawned printer and UI.");
    }

    private void HandleUpdated(ARMarker marker)
    {
        if (!isSpawned) return;

        var decoded = marker.GetDecodedString();
        if (decoded != TARGET_QR_PAYLOAD) return;

        if (marker.trackingState == TrackingState.Tracking)
            ApplyPose(marker);
    }

    private void HandleRemoved(ARMarker marker)
    {
        // Keep content alive after QR is removed - freeze last known pose
        Debug.Log("[QRCodeContentPlacer] QR removed - content remains at last pose.");
    }

    private void ApplyPose(ARMarker marker)
    {
        if (spawnedPrinter == null) return;

        var pos = marker.transform.position;
        var rot = marker.transform.rotation;

        spawnedPrinter.transform.SetPositionAndRotation(pos, rot);

        // Position UI to the left of the printer in world space
        var uiPos = pos + rot * uiPositionOffset;
        spawnedFileSelectionUI.transform.SetPositionAndRotation(uiPos, rot);
        spawnedControlPanelUI.transform.SetPositionAndRotation(uiPos, rot);
    }

    /// <summary>
    /// Call this from your File Selection UI when the user picks a file and starts the simulation.
    /// </summary>
    public void SwitchToControlPanel()
    {
        SetPhase(DemoPhase.ControlPanel);
    }

    /// <summary>
    /// Call this to go back to File Selection (e.g. simulation ends or reset).
    /// </summary>
    public void SwitchToFileSelection()
    {
        SetPhase(DemoPhase.FileSelection);
    }

    private void SetPhase(DemoPhase phase)
    {
        currentPhase = phase;

        bool showFileSelection = (phase == DemoPhase.FileSelection);

        if (spawnedFileSelectionUI != null)
            spawnedFileSelectionUI.SetActive(showFileSelection);

        if (spawnedControlPanelUI != null)
            spawnedControlPanelUI.SetActive(!showFileSelection);

        Debug.Log($"[QRCodeContentPlacer] Phase switched to: {phase}");
    }
}