using UnityEngine;

// Cloth mouse pad: a plain weave of polyester yarns. Threads along X (warp) and along Z (weft)
// pass over and under each other at every crossing. Each yarn has a flattened round top.
// Sources are vendor guides, not lab measurements, so every shape value is marked.
[CreateAssetMenu(fileName = "Surface_ClothPad", menuName = "Mouse Surface/Cloth mouse pad")]
public class Surface_SimulationSensor_ClothPad_1_0 : Surface_SimulationSensor_Base_1_0
{
    [Header("Shape")]
    [Tooltip("Threads per inch. Vendor guides give 300-400 TPI for micro-weave pads (not lab-measured). The pitch is rounded so a whole, even number of threads fits the tile.")]
    [SerializeField, Min(10.0f)] private float threadsPerInch = 350.0f;
    [Tooltip("Yarn diameter in mm. DERIVED: 50-75 denier polyester (vendor guides) at 1.38 g/cm3 gives 0.072-0.088 mm.")]
    [SerializeField, Min(0.005f)] private float yarnDiameterMM = 0.08f;
    [Tooltip("How flat the yarn top is: height of the yarn cap / yarn radius. ESTIMATE (multifilament yarns flatten when woven).")]
    [SerializeField, Range(0.05f, 1.0f)] private float yarnFlattening = 0.5f;
    [Tooltip("Over/under wave height of each yarn, as a share of the yarn diameter. ESTIMATE.")]
    [SerializeField, Range(0.0f, 0.5f)] private float weaveWave = 0.25f;

    [Header("Cloth Results (calculated - edits are ignored)")]
    [SerializeField] private float pitchUsedMM;
    [SerializeField] private int threadsPerTile;

    private void Reset() { reflectance = 0.3f; }   // ESTIMATE: no 850 nm value found for pad cloth (pads are often dark)

    protected override void BuildHeightsMM(float[] h, int n, float texelMM)
    {
        float tile = n * texelMM;
        float pitchWanted = 25.4f / Mathf.Max(threadsPerInch, 10.0f);
        int m = Mathf.Max(2, Mathf.RoundToInt(tile / pitchWanted));
        if (m % 2 == 1) m++;                               // even count, so the over/under pattern repeats at the tile edge
        float p = tile / m;

        float rw = 0.5f * Mathf.Min(Mathf.Max(yarnDiameterMM, 0.005f), p);   // a yarn cannot be wider than its pitch here
        float cap = rw * yarnFlattening;
        float A = weaveWave * Mathf.Max(yarnDiameterMM, 0.005f);
        float bottom = -A - cap;                            // seen through the gaps between yarns

        for (int z = 0; z < n; z++)
        {
            float zz = (z + 0.5f) * texelMM;
            int j = Mathf.FloorToInt(zz / p);
            float dz = zz - (j + 0.5f) * p;
            float signJ = (j % 2 == 0) ? 1.0f : -1.0f;
            for (int x = 0; x < n; x++)
            {
                float xx = (x + 0.5f) * texelMM;
                int i = Mathf.FloorToInt(xx / p);
                float dx = xx - (i + 0.5f) * p;
                float signI = (i % 2 == 0) ? 1.0f : -1.0f;

                float top = bottom;
                if (Mathf.Abs(dz) < rw)   // warp yarn (runs along X)
                {
                    float warp = A * Mathf.Sin(Mathf.PI * xx / p) * signJ + cap * Mathf.Sqrt(1.0f - (dz / rw) * (dz / rw));
                    top = Mathf.Max(top, warp);
                }
                if (Mathf.Abs(dx) < rw)   // weft yarn (runs along Z), opposite wave
                {
                    float weft = -A * Mathf.Sin(Mathf.PI * zz / p) * signI + cap * Mathf.Sqrt(1.0f - (dx / rw) * (dx / rw));
                    top = Mathf.Max(top, weft);
                }
                h[z * n + x] = top;
            }
        }
        pitchUsedMM = p;
        threadsPerTile = m;
    }
}
