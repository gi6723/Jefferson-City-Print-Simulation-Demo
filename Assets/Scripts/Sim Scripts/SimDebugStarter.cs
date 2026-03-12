using UnityEngine;
using mumachiningmr.xr3dprintersimulator;

public class SimDebugStarter : MonoBehaviour
{
    [SerializeField] private PrintSimulation_Extruder extruder;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            extruder.tryGradualSimulation();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            extruder.tryAutoFinishSimulation();
        }
    }
}