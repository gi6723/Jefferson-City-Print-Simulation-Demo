using System;
using System.Collections.Generic;
using Microsoft.MixedReality.OpenXR;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(ARMarkerManager))]
public class QRCodeContentPlacer : MonoBehaviour
{
    [Header("Pose Correction")]
    [Tooltip("Rotation offset applied to spawned content to correct orientation.")]
    [SerializeField] private Vector3 rotationOffset = new Vector3(-90f, 0f, 0f);
    
    [Header("Single QR Mode")]
    [Tooltip("Only spawn when the decoded QR payload matches this exact string.")]
    [SerializeField] private string requiredDecodedText = "Enjoy";

    [Tooltip("If true, trims whitespace before comparing decoded text.")]
    [SerializeField] private bool trimDecoded = true;

    [Tooltip("If true, comparison is case-sensitive.")]
    [SerializeField] private bool caseSensitive = true;

    [Header("Content Prefab")]
    [Tooltip("Prefab to spawn when the QR payload matches. Recommended: one prefab containing Ender2Pro + UI offset to the left.")]
    [SerializeField] private GameObject contentPrefab;

    [Header("Behavior")]
    [Tooltip("If true, continuously updates the spawned object's transform from the marker while tracked.")]
    [SerializeField] private bool followUpdates = true;

    [Tooltip("Only show content when marker trackingState is Tracking.")]
    [SerializeField] private bool requireFullTracking = true;

    [Tooltip("If true, keep content visible when tracking is lost after being seen (freezes last pose).")]
    [SerializeField] private bool keepVisibleWhenTrackingLost = true;

    [Tooltip("If true, keep content visible after a marker is removed (freezes last pose).")]
    [SerializeField] private bool keepAliveOnRemoved = true;

    // TrackableId -> instance
    private readonly Dictionary<TrackableId, GameObject> spawnedById = new();
    private readonly Dictionary<TrackableId, string> decodedById = new();

    // Stable key (decoded) -> instance + last pose
    private readonly Dictionary<string, GameObject> spawnedByKey = new();
    private readonly Dictionary<string, Pose> lastPoseByKey = new();
    private readonly Dictionary<string, bool> everTrackedByKey = new();

    private ARMarkerManager markerManager;

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

        if (followUpdates)
        {
            foreach (var marker in args.updated)
                HandleUpdated(marker);
        }

        foreach (var marker in args.removed)
            HandleRemoved(marker);
    }

    private void HandleAdded(ARMarker marker)
    {
        var decoded = marker.GetDecodedString();

        if (!IsMatch(decoded))
            return;

        if (contentPrefab == null)
        {
            Debug.LogWarning("[QRCodeContentPlacer] contentPrefab is not assigned.");
            return;
        }

        var key = Normalize(decoded);

        // Reuse if we already spawned for this payload.
        if (!spawnedByKey.TryGetValue(key, out var instance) || instance == null)
        {
            instance = Instantiate(contentPrefab);
            instance.name = $"QRContent_{key}";
            spawnedByKey[key] = instance;
        }

        spawnedById[marker.trackableId] = instance;
        decodedById[marker.trackableId] = decoded;

        ApplyPose(marker, instance, key);

        Debug.Log($"[QRCodeContentPlacer] Matched '{requiredDecodedText}'. Spawned/updated content at QR pose. id={marker.trackableId}");
    }

    private void HandleUpdated(ARMarker marker)
    {
        if (!spawnedById.TryGetValue(marker.trackableId, out var instance) || instance == null)
            return;

        var decoded = decodedById.TryGetValue(marker.trackableId, out var d) ? d : marker.GetDecodedString();
        if (!IsMatch(decoded))
            return;

        var key = Normalize(decoded);
        ApplyPose(marker, instance, key);
    }

    private void HandleRemoved(ARMarker marker)
    {
        if (!spawnedById.TryGetValue(marker.trackableId, out var instance) || instance == null)
            return;

        var decoded = decodedById.TryGetValue(marker.trackableId, out var d) ? d : marker.GetDecodedString();
        var key = Normalize(decoded);

        spawnedById.Remove(marker.trackableId);
        decodedById.Remove(marker.trackableId);

        if (!IsMatch(decoded))
            return;

        if (keepAliveOnRemoved)
        {
            // Freeze at last good pose (if known)
            if (!requireFullTracking || (everTrackedByKey.TryGetValue(key, out var ever) && ever))
                ApplyFrozenPose(key, instance);

            return;
        }

        Destroy(instance);
        spawnedByKey.Remove(key);
        lastPoseByKey.Remove(key);
        everTrackedByKey.Remove(key);
    }

    private void ApplyPose(ARMarker marker, GameObject instance, string key)
    {
        // Cache last pose when tracked (or whenever full tracking not required)
        if (marker.trackingState == TrackingState.Tracking)
        {
            everTrackedByKey[key] = true;
            lastPoseByKey[key] = new Pose(marker.transform.position, marker.transform.rotation);
        }
        else if (!requireFullTracking)
        {
            lastPoseByKey[key] = new Pose(marker.transform.position, marker.transform.rotation);
        }

        // Visibility rules
        if (requireFullTracking && marker.trackingState != TrackingState.Tracking)
        {
            if (keepVisibleWhenTrackingLost && everTrackedByKey.TryGetValue(key, out var ever) && ever)
            {
                ApplyFrozenPose(key, instance);
            }
            else
            {
                instance.SetActive(false);
            }
            return;
        }

        if (!instance.activeSelf)
            instance.SetActive(true);

        var correctedRot = marker.transform.rotation * Quaternion.Euler(rotationOffset);
        instance.transform.SetPositionAndRotation(marker.transform.position, correctedRot);
    }

    private void ApplyFrozenPose(string key, GameObject instance)
    {
        if (!instance.activeSelf)
            instance.SetActive(true);

        if (lastPoseByKey.TryGetValue(key, out var pose))
            instance.transform.SetPositionAndRotation(pose.position, pose.rotation * Quaternion.Euler(rotationOffset));    }

    private bool IsMatch(string decoded)
    {
        if (string.IsNullOrEmpty(decoded) || string.IsNullOrEmpty(requiredDecodedText))
            return false;

        var a = Normalize(decoded);
        var b = Normalize(requiredDecodedText);

        return caseSensitive
            ? string.Equals(a, b, StringComparison.Ordinal)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private string Normalize(string s)
    {
        if (s == null)
            return null;

        return trimDecoded ? s.Trim() : s;
    }
}