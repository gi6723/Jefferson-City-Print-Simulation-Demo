using TMPro;
using UnityEngine;

public class HudLogger : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private int maxLines = 12;

    private readonly System.Collections.Generic.Queue<string> lines = new();

    public void Log(string msg)
    {
        var line = $"[{Time.time:0.00}] {msg}";
        Debug.Log(line);

        lines.Enqueue(line);
        while (lines.Count > maxLines) lines.Dequeue();

        if (text != null)
            text.text = string.Join("\n", lines);
    }
}