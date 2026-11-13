using UnityEngine;

// Hard (plastic) mouse pad: a fine random texture of round bumps and dips of different sizes and depths.
// The heights are scaled so the RMS roughness equals roughnessRmsMM.
[CreateAssetMenu(fileName = "Surface_HardPad", menuName = "Mouse Surface/Hard mouse pad")]
public class Surface_SimulationSensor_HardPad_1_0 : Surface_SimulationSensor_Base_1_0
{
    [Header("Shape (mm)")]
    [Tooltip("RMS roughness. DERIVED from a textured mouse-pad patent (Ra 2-20 um, preferred 2-8 um, US5776585): Ra 5 um / 0.8 = RMS 0.00625 mm. The patent is a ball-mouse glass pad.")]
    [SerializeField, Min(0.0001f)] private float roughnessRmsMM = 0.00625f;
    [Tooltip("Typical bump diameter. ESTIMATE: no measured value found.")]
    [SerializeField, Min(0.008f)] private float bumpDiameterMM = 0.05f;

    [Header("Hard Pad Results (calculated - edits are ignored)")]
    [SerializeField] private int bumpCount;

    private const float Coverage = 4.0f;   // bump area / tile area: about 4 bumps over every point
    private const int Seed = 5776;         // fixed, so the same pad comes back every run

    private void Reset() { reflectance = 0.3f; }   // ESTIMATE: no 850 nm value found (pads are often dark)

    protected override void BuildHeightsMM(float[] h, int n, float texelMM)
    {
        float tile = n * texelMM;
        float d = Mathf.Max(bumpDiameterMM, 0.008f);
        var rng = new System.Random(Seed);
        int bumps = Mathf.Max(1, Mathf.RoundToInt(Coverage * tile * tile / (d * d)));

        for (int b = 0; b < bumps; b++)
        {
            float cx = (float)rng.NextDouble() * tile;
            float cz = (float)rng.NextDouble() * tile;
            float size = d * (0.5f + (float)rng.NextDouble());          // 0.5x to 1.5x the typical diameter
            float sigma = 0.5f * size;
            float amp = ((float)rng.NextDouble() * 2.0f - 1.0f);        // bump (+) or dip (-)
            int reach = Mathf.CeilToInt(2.5f * sigma / texelMM);
            int ix0 = Mathf.FloorToInt(cx / texelMM), iz0 = Mathf.FloorToInt(cz / texelMM);
            float inv2s2 = 1.0f / (2.0f * sigma * sigma);

            for (int dz = -reach; dz <= reach; dz++)
            {
                float pz = (iz0 + dz + 0.5f) * texelMM - cz;
                int row = Wrap(iz0 + dz, n) * n;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float px = (ix0 + dx + 0.5f) * texelMM - cx;
                    h[row + Wrap(ix0 + dx, n)] += amp * Mathf.Exp(-(px * px + pz * pz) * inv2s2);
                }
            }
        }

        ScaleToRms(h, Mathf.Max(roughnessRmsMM, 0.0001f));
        bumpCount = bumps;
    }
}
