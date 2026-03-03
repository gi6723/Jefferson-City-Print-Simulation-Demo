using System.Collections.Generic;
using TMPro;
using UnityEngine;
using MixedReality.Toolkit;

public class GcodeSelectionController : MonoBehaviour
{
    [Header("Presets (match by index)")]
    [SerializeField] private List<PrintJobPreset> jobs = new();

    [Header("MRTK Interactables (match by index)")]
    [SerializeField] private List<StatefulInteractable> jobButtons = new();

    [Header("Optional labels (match by index)")]
    [SerializeField] private List<TMP_Text> jobLabels = new();

    [Header("Preview Anchor")]
    [SerializeField] private Transform previewAnchor;

    [Header("Preview Transform (relative to PreviewAnchor)")]
    [Tooltip("Local position of the preview relative to PreviewAnchor.")]
    [SerializeField] private Vector3 previewLocalPosition = new Vector3(0f, 0f, 0.03f);

    [Tooltip("Rotation correction applied to the spawned preview (use this to stand OBJs upright).")]
    [SerializeField] private Vector3 previewLocalEuler = new Vector3(-90f, 0f, 0f);

    [Tooltip("Local scale applied to the spawned preview (tune per your import scale).")]
    [SerializeField] private Vector3 previewLocalScale = Vector3.one;

    private int selectedIndex = -1;
    private GameObject previewInstance;

    private void Awake()
    {
        int n = Mathf.Min(jobs.Count, jobButtons.Count);

        // Set labels if you provided them
        int labelN = Mathf.Min(jobs.Count, jobLabels.Count);
        for (int i = 0; i < labelN; i++)
        {
            if (jobLabels[i] == null) continue;

            string name = !string.IsNullOrWhiteSpace(jobs[i].displayName)
                ? jobs[i].displayName
                : (jobs[i].gcodeFile != null ? jobs[i].gcodeFile.name : $"Job {i + 1}");

            jobLabels[i].text = name;
        }

        // Hook MRTK events
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var interactable = jobButtons[i];
            if (interactable == null) continue;

            // Remove previous listeners to avoid stacking in playmode
            interactable.OnClicked.RemoveAllListeners();
            interactable.OnClicked.AddListener(() => Select(idx));
        }
    }

    private void Select(int idx)
    {
        if (idx < 0 || idx >= jobs.Count) return;

        selectedIndex = idx;

        if (previewInstance != null)
            Destroy(previewInstance);

        if (previewAnchor == null)
        {
            Debug.LogWarning("[GcodeSelectionController] previewAnchor not assigned.");
            return;
        }

        var prefab = jobs[idx].previewPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[GcodeSelectionController] No previewPrefab assigned for index {idx}.");
            return;
        }

        previewInstance = Instantiate(prefab, previewAnchor);

        // Center on anchor + push slightly forward (avoid clipping) + apply upright correction
        previewInstance.transform.localPosition = previewLocalPosition;
        previewInstance.transform.localRotation = Quaternion.Euler(previewLocalEuler);
        previewInstance.transform.localScale = previewLocalScale;

        Debug.Log($"[GcodeSelectionController] Selected '{GetSelectedName()}', preview spawned.");
    }

    public string GetSelectedName()
    {
        if (selectedIndex < 0 || selectedIndex >= jobs.Count) return "None";
        return !string.IsNullOrWhiteSpace(jobs[selectedIndex].displayName)
            ? jobs[selectedIndex].displayName
            : (jobs[selectedIndex].gcodeFile != null ? jobs[selectedIndex].gcodeFile.name : "Unnamed");
    }
}