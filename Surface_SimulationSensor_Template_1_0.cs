using UnityEngine;

// TEMPLATE - a perfectly flat surface. Use it as is to see what the sensor gets with no texture,
// or copy it to make a new surface:
//   1. Copy this file and give the copy a new name, e.g. Surface_SimulationSensor_Wood_1_0.cs
//   2. Rename the class below to the same name as the new file.
//   3. Change the menuName text below (what you see under Create > Mouse Surface).
//   4. Add the surface's own values under "Shape" (always in mm, with the source in the tooltip).
//   5. Write the shape in BuildHeightsMM: give every point h[z * n + x] a height in mm.
//      The point sits at x * texelMM, z * texelMM. The pattern must repeat at the edges
//      (point n is point 0 again), so use Wrap() or whole numbers of periods per tile.
// Everything else (mean height, min/max, roughness result, sending it to the shader) is done by the base.
[CreateAssetMenu(fileName = "Surface_Flat", menuName = "Mouse Surface/Flat (template)")]
public class Surface_SimulationSensor_Template_1_0 : Surface_SimulationSensor_Base_1_0
{
    // [Header("Shape (mm)")]
    // [Tooltip("What it is, and where the number comes from.")]
    // [SerializeField, Min(0.0f)] private float exampleValueMM = 0.01f;

    protected override void BuildHeightsMM(float[] h, int n, float texelMM)
    {
        // Flat: every height stays 0.
        // Example of a shape (do not uncomment here, put it in your copy):
        // for (int z = 0; z < n; z++)
        //     for (int x = 0; x < n; x++)
        //         h[z * n + x] = exampleValueMM * Mathf.Sin(2.0f * Mathf.PI * x / n);
    }
}
