using TMPro;
using UnityEngine;

namespace mumachiningmr.xr3dprintersimulator
{
    /// <summary>
    /// Updates a UI label to show the currently selected GCode file name.
    /// No MRTK dependency (use TMP_Text in your panel).
    /// </summary>
    public class FileSelectionManager : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private PrintSimulation_Extruder extruder;
        [SerializeField] private TMP_Text filepathText;

        private void Awake()
        {
            if (extruder == null)
                extruder = GetComponentInParent<PrintSimulation_Extruder>();
        }

        public void RefreshLabel()
        {
            if (filepathText == null)
                return;

            filepathText.text = extruder != null ? extruder.GetSelectedGcodeDisplayName() : "None";
        }
    }
}