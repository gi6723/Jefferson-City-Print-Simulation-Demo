using UnityEngine;

[System.Serializable]
public class PrintJobPreset
{
    public string displayName;
    public TextAsset gcodeFile;
    public GameObject previewPrefab;
}