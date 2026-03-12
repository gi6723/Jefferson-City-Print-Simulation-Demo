using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mumachiningmr.xr3dprintersimulator
{
    /// <summary>
    /// Lightweight bridge between a UI slider and the PrintSimulation_Extruder.
    /// - No MRTK/Editor dependencies (safe for device builds).
    /// - Works with either Unity UI Slider, or MRTK3 Slider if you call OnSliderValueChanged(float) from its event.
    /// </summary>
    public class SliderManager : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private PrintSimulation_Extruder extruder;
        [Tooltip("Optional: assign if you're using a UnityEngine.UI.Slider.")]
        [SerializeField] private Slider unitySlider;

        [Header("Optional Text")]
        [SerializeField] private TMP_Text sliderValueText;
        [SerializeField] private TMP_Text sliderLabelText;

        [Header("Mode")]
        [SerializeField] private bool isSpeedSlider = true;

        private void Awake()
        {
            if (extruder == null)
                extruder = GetComponentInParent<PrintSimulation_Extruder>();

            if (unitySlider != null)
                unitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        private void OnDestroy()
        {
            if (unitySlider != null)
                unitySlider.onValueChanged.RemoveListener(OnSliderValueChanged);
        }

        /// <summary>
        /// Hook this to a slider event (Unity UI or MRTK3) that passes a float.
        /// </summary>
        public void OnSliderValueChanged(float value)
        {
            if (extruder == null)
                return;

            if (isSpeedSlider)
            {
                extruder.setPrintSpeed(value);
                if (sliderValueText != null)
                    sliderValueText.text = "x" + Math.Round(value);
            }
            else
            {
                extruder.showLayer((int)value);
                if (sliderValueText != null)
                    sliderValueText.text = "" + Math.Round(value);
            }
        }

        public void sliderTypeSpeed()
        {
            isSpeedSlider = true;
            if (sliderLabelText != null) sliderLabelText.text = "Speed";
        }

        public void sliderTypeLayer(int maxLayer)
        {
            isSpeedSlider = false;

            if (unitySlider != null)
            {
                unitySlider.minValue = 0;
                unitySlider.maxValue = Mathf.Max(0, maxLayer);
                unitySlider.wholeNumbers = true;
            }

            if (sliderLabelText != null) sliderLabelText.text = "Layer";
        }
    }
}