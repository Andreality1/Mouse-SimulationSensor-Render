using UnityEngine;

// White office paper: a mat of straight fibers lying in random directions, on top of each other.
// Each fiber is a ridge with a round top. Where fibers cross, the top one shows.
// The heights are then scaled so the RMS roughness equals roughnessRmsMM.
// Same algorithm and values as Main_SimulationSensor_14_0.
[CreateAssetMenu(fileName = "Surface_Paper", menuName = "Mouse Surface/Paper (white office)")]
public class Surface_SimulationSensor_Paper_1_0 : Surface_SimulationSensor_Base_1_0
{
    [Header("Shape (mm)")]
    [Tooltip("Fiber width. Eucalyptus pulp 18.0-19.1 um (Morais et al. 2019).")]
    [SerializeField, Min(0.002f)] private float fiberWidthMM = 0.019f;
    [Tooltip("Fiber length. Eucalyptus pulp 0.70-0.84 mm (Morais et al. 2019).")]
    [SerializeField, Min(0.01f)] private float fiberLengthMM = 0.8f;
    [Tooltip("RMS roughness. ESTIMATE between plain-paper Ra < 2.1 um (patent) and measured Sa 5.4 um.")]
    [SerializeField, Min(0.0001f)] private float roughnessRmsMM = 0.003f;

    [Header("Paper Results (calculated - edits are ignored)")]
    [SerializeField] private int fiberCount;

    private const float Coverage = 3.0f;   // fiber area / tile area: about 3 fibers over every point (estimate, paper is a thick fiber mat)
    private const int Seed = 3220;         // fixed, so the same paper comes back every run

    private void Reset() { reflectance = 0.8f; }   // white papers reflect 50-90 %; 0.8 is an ESTIMATE

    protected override void BuildHeightsMM(float[] h, int n, float texelMM)
    {
        float w = Mathf.Max(fiberWidthMM, 0.002f);
        float len = Mathf.Max(fiberLengthMM, 0.01f);
        float tile = n * texelMM;
        var rng = new System.Random(Seed);

        int fibers = Mathf.Max(1, Mathf.RoundToInt(Coverage * tile * tile / (w * len)));
        float r = 0.5f * w;
        float step = texelMM * 0.5f;
        int alongSteps = Mathf.CeilToInt(len / step);
        int acrossSteps = Mathf.CeilToInt(w / step);

        for (int f = 0; f < fibers; f++)
        {
            float cx = (float)rng.NextDouble() * tile;
            float cy = (float)rng.NextDouble() * tile;
            float ang = (float)rng.NextDouble() * Mathf.PI;
            float baseH = (float)rng.NextDouble();          // stacking: 0 = bottom layer, 1 = on top of others
            float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);

            for (int i = 0; i <= alongSteps; i++)
            {
                float s = -0.5f * len + i * step;
                for (int j = 0; j <= acrossSteps; j++)
                {
                    float u = -r + j * step;
                    float prof = Mathf.Sqrt(Mathf.Max(1.0f - (u / r) * (u / r), 0.0f));   // round top, 1 on the center line
                    int ix = Wrap(Mathf.FloorToInt((cx + s * dx - u * dy) / texelMM), n);
                    int iy = Wrap(Mathf.FloorToInt((cy + s * dy + u * dx) / texelMM), n);
                    int k = iy * n + ix;
                    float top = baseH + prof;
                    if (top > h[k]) h[k] = top;
                }
            }
        }

        ScaleToRms(h, Mathf.Max(roughnessRmsMM, 0.0001f));
        fiberCount = fibers;
    }
}
