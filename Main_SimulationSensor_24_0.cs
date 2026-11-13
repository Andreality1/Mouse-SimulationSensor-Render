using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class Main_SimulationSensor_24_0 : MonoBehaviour
{
    public enum MouseMotion { Static, Linear, Circular }

    [Header("Dependencies")]
    [SerializeField] private ComputeShader hybridShader;

    // 16_0: the LED is placed from its angle and height, on the +Z side, aimed at the paper spot under the lens.
    // It moves with the mouse. Light model: point light at the window center, beam pattern cos^m, shadow rays.
    [Header("LED (Lite-On LTE-C216-P-W, datasheet pages 2 and 4)")]
    [Tooltip("Angle between the paper and the line from the LED window to the paper spot under the lens. HP mouse patents (US20050231483, US7808485): 5 to 20 degrees.")]
    [SerializeField, Range(5.0f, 20.0f)] private float ledAngleDeg = 15.0f;
    [Tooltip("Height of the LED window center above the paper. ESTIMATE.")]
    [SerializeField, Min(0.05f)] private float ledWindowHeightMM = 1.0f;
    [Tooltip("Viewing angle 2 x theta-half (datasheet page 4): 100 degrees. The light is at half strength 50 degrees away from the aim.")]
    [SerializeField, Range(10.0f, 170.0f)] private float ledBeamAngleDeg = 100.0f;
    [Tooltip("Body length x width x height (datasheet page 2). The length lies sideways (along X). The height runs along the aim, with the window on the front face.")]
    [SerializeField] private Vector3 ledBodySizeMM = new Vector3(3.20f, 1.60f, 1.10f);
    [Tooltip("LED base (its own small board), at the back of the body (datasheet page 2). The clear top is the rest of the height.")]
    [SerializeField, Min(0.0f)] private float ledBaseMM = 0.60f;
    [Tooltip("Radius of the round window in the clear top (datasheet page 2: R0.50).")]
    [SerializeField, Min(0.01f)] private float ledWindowRadiusMM = 0.50f;
    [Tooltip("Glow of the LED window in the main view only. Does not change the sensor image.")]
    [SerializeField] private float lightIntensity = 150000.0f;

    [Header("Light Shield (mm)")]
    [Tooltip("The housing walls reach down to this height above the paper. It must be above the useful light and below the direct LED-to-lens light (see LED Results). Chosen value.")]
    [SerializeField, Min(0.0f)] private float shieldBottomHeightMM = 0.6f;

    [Header("Mouse Size Limit (mm)")]
    [Tooltip("Whole mouse, width (X) x depth (Z), centered on the sensor axis. Set by the project.")]
    [SerializeField] private Vector2 mouseSizeLimitMM = new Vector2(17.5f, 17.5f);
    [Tooltip("Whole mouse height above the paper. Set by the project.")]
    [SerializeField, Min(0.1f)] private float mouseHeightLimitMM = 3.0f;

    [Header("1:1 Optics - the input (mm)")]
    [Tooltip("a: from the surface (mean height of the rough patch) to the lens center. At 1:1 the lens center to the sensor is also a. The lens is calculated from this one value.")]
    [SerializeField, Min(0.1f)] private float surfaceToLensMM = 0.9f;
    [SerializeField] private bool showOptics = true;

    [Header("Height Limit (mm)")]
    [Tooltip("Maximum height from the surface to the top of the chip.")]
    [SerializeField, Min(0.1f)] private float heightLimitMM = 2.5f;
    [Tooltip("PAW3220LU-TJDU package height (product brief, Table 2).")]
    [SerializeField, Min(0.0f)] private float chipHeightMM = 0.665f;
    [Tooltip("PAW3220LU-TJDU glass surface to wafer (product brief, Table 2). The wafer is the sensor plane.")]
    [SerializeField, Min(0.0f)] private float chipGlassToWaferMM = 0.405f;
    // 23_0 (stage 6.2): the chip glass bends the light. Glass thickness = Chip Glass To Wafer (0.405, Figure 3 C3).
    [Tooltip("23_0: index of the chip glass at 850 nm. REPRESENTATIVE: the PAW3220 glass type is not published. 1.51 = N-BK7 at 852 nm (Schott); D 263 cover glass is 1.523 at 589 nm (Schott datasheet), a little lower at 850 nm. Focus shift changes only 0.135-0.139 mm over 1.50-1.523.")]
    [SerializeField, Range(1.3f, 2.0f)] private float chipGlassIndex = 1.51f;
    [Tooltip("23_0: ON = the chip sits higher by the focus shift so the image stays sharp at 1:1 (lens, pinhole and lens-to-paper unchanged). OFF = the chip stays where it was: shows the blur the glass adds.")]
    [SerializeField] private bool refocusForGlass = true;
    [Tooltip("23_0: sensor array center minus chip center, X and Z (PAW3220 Figure 3: -3.09115, 328.20455 um). The chip body and its housing sit off the lens axis by this much; the lens stays centered on the sensor. If the chip is mounted mirrored, flip the sign.")]
    [SerializeField] private Vector2 sensorOffsetMM = new Vector2(-0.0031f, 0.3282f);

    [Header("Sensor Housing (mm, open top)")]
    [Tooltip("PAW3220LU-TJDU body size along X (product brief, Table 2).")]
    [SerializeField, Min(0.1f)] private float chipBodyWidthMM = 1.5366f;
    [Tooltip("PAW3220LU-TJDU body size along Z (product brief, Table 2).")]
    [SerializeField, Min(0.1f)] private float chipBodyDepthMM = 1.8756f;
    [Tooltip("Space between the chip and each side wall. 0 = walls touch the chip.")]
    [SerializeField, Min(0.0f)] private float chipToWallGapMM = 0.0f;
    [Tooltip("Thickness of the four side walls.")]
    [SerializeField, Min(0.01f)] private float housingWallMM = 0.2f;
    [Tooltip("Thickness of the floor = the plate the pinhole is cut through.")]
    [SerializeField, Min(0.005f)] private float housingFloorMM = 0.05f;

    [Header("Mouse Motion (mm, on the surface)")]
    [SerializeField] private MouseMotion motion = MouseMotion.Static;
    [SerializeField] private Vector2 mouseStartXZ = new Vector2(0.0f, 0.0f);
    [SerializeField] private Vector2 motionAmplitude = new Vector2(1.0f, 0.0f);
    [SerializeField] private float motionFrequencyHz = 0.25f;

    [Header("Lens Design")]
    // 18_0: largest diameter that keeps the image center sharp (blur under 1 pixel = 0.03 mm, also with the paper ±0.1 mm),
    // from a ray trace of this lens design at a = 0.9. 1.8x the light of 0.15. Made by two-photon 3D printing (fits easily: up to 3.5-4 mm).
    [Tooltip("18_0: 0.20 = largest size that keeps the image center sharp (ray trace, a = 0.9, spherical biconvex). Halfway out the blur is 0.035 (just over 1 pixel). Was 0.15 (estimate).")]
    [SerializeField, Min(0.01f)] private float lensDiameterMM = 0.20f;
    [Tooltip("ON: Full optical lens refraction. OFF: Renders lens as a simple 3D shaded object with basic lighting/shadow response.")]
    [SerializeField] private bool enableLensVisuals = true;

    [Header("Lens Material & Shape (biconvex)")]
    [SerializeField, Range(1.2f, 2.0f)] private float refractiveIndex = 1.5f;
    [SerializeField, Min(0.005f)] private float lensEdgeThicknessMM = 0.05f;
    [SerializeField] private bool useRealLens = true;

    [Header("Lens Results (calculated - edits are ignored)")]
    [SerializeField] private string lensStatus;
    [SerializeField] private float focalLengthMM;
    [SerializeField] private float curvatureRadiusMM;
    [SerializeField] private float centerThicknessMM;
    [SerializeField] private float principalPlaneOffsetMM;
    [SerializeField] private float lensCenterToSensorMM;  // = a at 1:1
    [SerializeField] private float gapMM;                 // lowest point of the lens to the surface
    [SerializeField] private float sensorToSurfaceMM;     // = 2a
    [SerializeField] private float surfaceToChipTopMM;    // 2a + (chip height - glass to wafer)

    [Header("Chip Glass Results (23_0, calculated - edits are ignored)")]
    [SerializeField] private float glassFeltThicknessMM;   // glass thickness / index: the air distance the light "feels"
    [SerializeField] private float focusShiftMM;           // glass thickness - felt thickness
    [SerializeField] private float dieHeightMM;            // sensor plane (die) above the surface
    [SerializeField] private float glassBottomHeightMM;    // chip face (glass bottom) above the surface
    [SerializeField] private float glassTransmissionPct;   // straight-in light through the glass face (Fresnel)
    [SerializeField] private float pixelSizeMM;            // sensor size / 30
    [SerializeField] private bool underHeightLimit;
    [SerializeField] private float magnificationResult;   // 1.0 at 1:1
    [SerializeField] private float fNumber;
    [SerializeField] private Vector2 fieldOfViewMM;

    [Header("Housing Results (calculated - edits are ignored)")]
    [SerializeField] private float pinholeDiameterMM;       // = lens diameter
    [SerializeField] private float floorAboveLensCenterMM;  // floor underside = lens top = half the lens thickness
    [SerializeField] private float floorUndersideHeightMM;  // above the surface
    [SerializeField] private float floorTopHeightMM;        // above the surface
    [SerializeField] private float housingHeightMM;         // floor underside to chip top
    [SerializeField] private Vector2 housingOutsideSizeMM;  // X, Z
    [SerializeField] private float floorToChipAirMM;        // floor top to the bottom (glass) of the chip

    // 17_0: lens holder plate. One clear piece with the lens (same material), it fills the inside of the walls
    // under the floor and is joined to the floor underside. Built only from values already typed above.
    [Header("Lens Holder Results (calculated - edits are ignored)")]
    [SerializeField] private Vector2 holderSizeMM;          // X, Z = chip body + 2 x Chip To Wall Gap (inside of the walls)
    [SerializeField] private float holderBottomHeightMM;    // = a - Lens Edge Thickness / 2 (bottom of the lens edge)
    [SerializeField] private float holderTopHeightMM;       // = floor underside (joined to the floor)
    [SerializeField] private float holderThicknessMM;       // top - bottom

    // 19_0 (stage 6.1): the pinhole floor clips the rays going to the outer pixels (vignetting).
    // Straight-line check from the sensor edge to the lens center plane through both floor faces (the image itself is fully traced).
    [Header("Vignetting Results (calculated - edits are ignored)")]
    [SerializeField] private float lightAtHalfwayPct;       // halfway from the center to the sensor edge, % of the center light
    [SerializeField] private float lightAtEdgePct;          // middle of the sensor edge (X), % of the center light
    [SerializeField] private float lightAtCornerPct;        // sensor corner, % of the center light

    [Header("LED Results (calculated - edits are ignored)")]
    [SerializeField] private string ledStatus;
    [SerializeField] private float ledDistanceFromAxisMM;   // window center, along Z
    [SerializeField] private float ledToSpotMM;             // window center to the paper spot
    [SerializeField] private float ledLowestPointMM;        // above the paper
    [SerializeField] private float ledHighestPointMM;       // above the paper
    [SerializeField] private float ledNearestZMM;           // closest body point to the sensor axis
    [SerializeField] private float ledFarthestZMM;          // farthest body point from the sensor axis
    [SerializeField] private float angleAtNearEdgeDeg;      // light angle at the patch edge nearest the LED
    [SerializeField] private float angleAtFarEdgeDeg;       // light angle at the far patch edge
    [SerializeField] private float beamPower;               // m in cos^m, from the beam angle
    [SerializeField] private float beamAtPatchCornersPct;   // beam strength at the patch corners, % of the aim
    [SerializeField] private float usefulLightAtShieldMM;   // highest LED-to-patch ray where it crosses the shield
    [SerializeField] private float fogLightAtShieldMM;      // lowest LED-to-lens ray where it crosses the shield
    [SerializeField] private bool shieldPassesUsefulLight;
    [SerializeField] private bool shieldBlocksFogLight;
    [SerializeField] private float mouseHighestPointMM;     // LED or chip, whichever is higher
    [SerializeField] private bool fitsInMouse;

    [Header("Sensor Plane (mm)")]
    [Tooltip("23_0: PAW3220 sensor array 795.6 x 795.6 um (Figure 3). 30 pixels (fixed) -> 26.5 um pixel. Was 0.9 (estimate).")]
    [SerializeField] private Vector2 sensorSize = new Vector2(0.7956f, 0.7956f);
    [SerializeField] private Color sensorColor = new Color(0.05f, 0.35f, 0.45f, 1.0f);
    [SerializeField] private bool showSensorImage = true;
    public enum SubSamples { Grid4x4 = 4, Grid8x8 = 8, Grid16x16 = 16 }
    [Tooltip("22_0: rays per pixel in ONE frame = N x N grid inside the pixel, one GPU thread per ray. 8 x 8 = 64, 16 x 16 = 256.")]
    [SerializeField] private SubSamples subSamplesPerSide = SubSamples.Grid8x8;
    [Tooltip("22_0 result: rays traced for the sensor in one frame (30 x 30 x N x N).")]
    [SerializeField] private int raysPerFrame;   // calculated - edits are ignored
    [SerializeField] private float sensorExposure = 1.0f;

    // 24_0 (stage 6.3): light that reaches the sensor without the clean path paper -> lens -> pinhole -> glass -> die.
    [Header("Stray Light (24_0, stage 6.3)")]
    [Tooltip("Matte reflectance of the black housing at 850 nm. 0.04 = carbon black, near-infrared reflectance 0.04 (LBNL Cool Colors pigment database, lamp black; paints with carbon black: 4-5 % total solar reflectance). Only true for carbon-black plastic: black DYES can be bright or clear at 850 nm.")]
    [SerializeField, Range(0.0f, 1.0f)] private float housingIrReflectance = 0.04f;
    [Tooltip("Housing bounces followed for stray light. 0 = off (housing is pure black, as in 23_0).")]
    [SerializeField, Range(0, 2)] private int strayBounces = 1;
    [Tooltip("ON: each lens face reflects or passes each ray at random (Fresnel), so reflections inside the lens reach the sensor as ghosts. OFF: the ray is only weakened (as in 23_0).")]
    [SerializeField] private bool lensGhosts = true;
    [Tooltip("ON: slanted rays bring less light to the outer pixels (cos^4 law). OFF: as in 23_0.")]
    [SerializeField] private bool naturalFalloff = true;
    public enum SensorView { AllLight = 0, CleanOnly = 1, StrayOnly = 2 }
    [Tooltip("What the sensor image shows. Stray Only is faint: raise Sensor Exposure to see it.")]
    [SerializeField] private SensorView sensorView = SensorView.AllLight;

    [Header("Stray Light Results (calculated - edits are ignored)")]
    [SerializeField] private float strayLightPct;            // stray / (clean + stray), whole sensor, measured on the sensor image
    [SerializeField] private float naturalFalloffEdgePct;    // cos^4 at the sensor edge, % of the center
    [SerializeField] private float naturalFalloffCornerPct;  // cos^4 at the sensor corner
    [SerializeField, Range(0.0f, 1.0f)] private float sensorOpacity = 0.35f;

    [Header("Sensor Grid")]
    [SerializeField] private bool showGrid = true;
    [Tooltip("Fixed at 30 x 30 by the project. Do not change.")]
    [SerializeField] private Vector2Int gridCells = new Vector2Int(30, 30);
    [SerializeField, Range(0.0f, 0.5f)] private float gridLineWidth = 0.06f;
    [SerializeField] private Color gridLineColor = new Color(0.45f, 0.80f, 0.90f, 1.0f);
    [SerializeField, Range(0.0f, 0.5f)] private float borderWidth = 0.12f;
    [SerializeField] private Color borderColor = new Color(0.85f, 0.95f, 1.0f, 1.0f);

    [Header("Surface (drag a Mouse Surface file here)")]
    [Tooltip("Center of the surface. Its Y is the mean height of the surface.")]
    [SerializeField] private Vector3 patchCenter = new Vector3(0.0f, -1.0f, 0.0f);
    [SerializeField] private float patchSize = 10.0f;
    [Tooltip("The surface under the mouse. Create one with right-click in the Project panel > Create > Mouse Surface. Empty = white office paper with default values.")]
    [SerializeField] private Surface_SimulationSensor_Base_1_0 surface;

    // 20_0: test marks painted on top of ANY surface (flat ink: the height map is unchanged, only the reflectance).
    // They are placed inside the patch the sensor sees, around Mouse Start XZ, so they are all in view at the start.
    [Header("Surface Marks (20_0, works on every surface)")]
    [SerializeField] private bool showMarks = true;
    [Tooltip("Number of marks. Shapes cycle: dot, cross, line, square, then random.")]
    [SerializeField, Range(1, 8)] private int markCount = 4;
    [Tooltip("1 = dot 0.12 mm, cross 0.18 mm, line 0.24 mm, square 0.15 mm (pixel = 0.03 mm).")]
    [SerializeField, Range(0.25f, 3.0f)] private float markSizeScale = 1.0f;
    [Tooltip("Line width of the cross, line and square (just over 1 pixel).")]
    [SerializeField, Range(0.008f, 0.2f)] private float markLineWidthMM = 0.04f;
    [Tooltip("21_0: groups of marks in each 4.096 mm tile, placed along the diagonal, Tile / Groups apart. Group 1 sits under the sensor at the start.")]
    [SerializeField, Range(1, 4)] private int groupsPerTile = 3;
    [Tooltip("Same seed = same positions and angles every run. Group g uses Seed + g.")]
    [SerializeField] private int markSeed = 3220;
    [Tooltip("ESTIMATE: reflectance of carbon-black toner at 850 nm (dark). Not yet from a published value.")]
    [SerializeField, Range(0.0f, 1.0f)] private float markReflectance = 0.05f;

    [Header("Surface Marks Results (calculated - edits are ignored)")]
    [SerializeField] private string[] markList;   // shape and center (X, Z in mm)

    [Header("Surface Results (calculated - edits are ignored)")]
    [SerializeField] private string surfaceInUse;
    [SerializeField] private float surfaceHeightMinMM;
    [SerializeField] private float surfaceHeightMaxMM;
    [SerializeField] private float surfaceRoughnessRmsMM;
    [SerializeField] private float surfaceReflectance;

    [Header("Chief-ray Vectors (mm)")]
    [SerializeField, Min(0.0f)] private float vectorThicknessMM = 0.003f;

    [Header("Performance Optimization")]
    [SerializeField] private float intensityClamp = 5000.0f;

    // Scale of the scene: 1 scene unit = 10 mm (1 cm).
    // Every value in the Inspector is in mm. It is converted to scene units once, when sent to the shader.
    private const float MmToUnit = 0.1f;

    private Camera _camera;
    private RenderTexture _targetBuffer;
    private RenderTexture _accumulationBuffer;
    private RenderTexture _sensorImage;
    private int _currentSample = 0;
    private int _sensorSample = 0;
    private Vector2 _lastMousePos;
    private bool _hasLastMousePos = false;
    private float _lensD, _lensH, _lensDelta;
    private float _so, _si;   // surface to front principal plane, rear principal plane to sensor (equal at 1:1)

    // 15_0 surface: the height map comes from the Surface file.
    private Surface_SimulationSensor_Base_1_0 _defaultSurface;   // used when the slot is empty
    private Surface_SimulationSensor_Base_1_0 _lastSurface;
    private int _lastSurfaceVersion = -1;
    private Texture2D _surfaceTex;
    private Texture2D _markTex;          // 20_0: mark coverage, 0 = surface, 1 = full ink (same tile as the height map)
    private bool _marksDirty = true;
    private bool _strayReadPending;      // 24_0
    private float _glassShift;           // 23_0: focus shift actually applied (0 when Refocus For Glass is off)

    // 16_0 LED frame, relative to the paper spot under the lens (mm)
    private Vector3 _ledWindowRel = new Vector3(0.0f, 1.0f, 3.73f);
    private Vector3 _ledAim = new Vector3(0.0f, -0.259f, -0.966f);
    private Vector3 _ledAxisL = Vector3.right;
    private Vector3 _ledAxisW = new Vector3(0.0f, -0.966f, 0.259f);

    private void Start()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnValidate()
    {
        _currentSample = 0;
        _sensorSample = 0;
        UpdateLensDesign();
        UpdateLedDesign();
        _marksDirty = true;
    }

    // 20_0: paints the marks into a coverage map with the same tile and texel as the surface height map.
    // Texel i covers the scene from i x TexelMM to (i + 1) x TexelMM (mm), repeated every TileMM, like the height map.
    private void EnsureMarks()
    {
        if (!_marksDirty && _markTex != null) return;
        _marksDirty = false;
        _sensorSample = 0;
        _currentSample = 0;

        int n = Surface_SimulationSensor_Base_1_0.Texels;
        float texel = Surface_SimulationSensor_Base_1_0.TexelMM;
        float[] cov = new float[n * n];

        int count = Mathf.Clamp(markCount, 1, 8);
        int groups = Mathf.Clamp(groupsPerTile, 1, 4);
        float spacing = Surface_SimulationSensor_Base_1_0.TileMM / groups;   // 21_0: diagonal step between groups (mm)
        int cells = Mathf.CeilToInt(Mathf.Sqrt(count));               // marks go in a cells x cells grid, so they never overlap
        float field = Mathf.Min(fieldOfViewMM.x, fieldOfViewMM.y);
        if (field <= 0.0f) field = 0.9f;
        float margin = 0.06f;
        float cell = Mathf.Max(field - 2.0f * margin, 0.05f) / cells;
        float lw = Mathf.Max(markLineWidthMM, 0.004f);
        markList = new string[count * groups];

        for (int g = 0; g < groups; g++)     // 21_0: one pass per group, each with its own seed and offset
        {
        var rng = new System.Random(markSeed + g);
        float gx = mouseStartXZ.x + g * spacing, gz = mouseStartXZ.y + g * spacing;

        // shuffled cell order, so the shapes land in random cells
        int[] order = new int[cells * cells];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        for (int i = order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }

        string[] names = { "Dot", "Cross", "Line", "Square" };
        for (int m = 0; m < count; m++)
        {
            int shape = m < 4 ? m : rng.Next(4);
            float size = markSizeScale * (shape == 0 ? 0.12f : shape == 1 ? 0.18f : shape == 2 ? 0.24f : 0.15f);
            float reach = size * 0.75f + lw;                              // farthest painted point from the mark center
            int c = order[m];
            float cx0 = -field * 0.5f + margin + (c % cells + 0.5f) * cell;
            float cz0 = -field * 0.5f + margin + (c / cells + 0.5f) * cell;
            float jitter = Mathf.Max(cell * 0.5f - reach, 0.0f);         // random shift that keeps the mark in its cell
            float cx = gx + cx0 + ((float)rng.NextDouble() * 2.0f - 1.0f) * jitter;
            float cz = gz + cz0 + ((float)rng.NextDouble() * 2.0f - 1.0f) * jitter;
            float ang = shape == 0 ? 0.0f : (float)rng.NextDouble() * Mathf.PI;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            markList[g * count + m] = string.Format("G{5} {0}  X {1:F3}  Z {2:F3}  size {3:F3}  angle {4:F0} deg", names[shape], cx, cz, size, ang * Mathf.Rad2Deg, g + 1);

            int r = Mathf.CeilToInt(reach / texel) + 2;
            int ic = Mathf.FloorToInt(cx / texel), jc = Mathf.FloorToInt(cz / texel);
            for (int dj = -r; dj <= r; dj++)
                for (int di = -r; di <= r; di++)
                {
                    float px = (ic + di + 0.5f) * texel - cx, pz = (jc + dj + 0.5f) * texel - cz;
                    float u = ca * px + sa * pz, v = -sa * px + ca * pz;     // into the mark's own frame
                    float d;                                                 // distance from the painted region (<= 0 inside)
                    if (shape == 0) d = Mathf.Sqrt(u * u + v * v) - size * 0.5f;
                    else if (shape == 1) d = Mathf.Min(SegDist(u, v, size * 0.5f), SegDist(v, u, size * 0.5f)) - lw * 0.5f;
                    else if (shape == 2) d = SegDist(u, v, size * 0.5f) - lw * 0.5f;
                    else d = Mathf.Abs(Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - size * 0.5f) - lw * 0.5f;
                    float a = Mathf.Clamp01(0.5f - d / texel);              // soft edge over one texel
                    if (a <= 0.0f) continue;
                    int k = WrapIndex(jc + dj, n) * n + WrapIndex(ic + di, n);
                    cov[k] = Mathf.Max(cov[k], a);
                }
        }
        }

        if (_markTex == null)
            _markTex = new Texture2D(n, n, TextureFormat.RFloat, false, true)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        _markTex.SetPixelData(cov, 0);
        _markTex.Apply(false);
    }

    // distance from (u, v) to the segment from (-half, 0) to (+half, 0)
    private static float SegDist(float u, float v, float half)
    {
        float du = Mathf.Max(Mathf.Abs(u) - half, 0.0f);
        return Mathf.Sqrt(du * du + v * v);
    }

    private static int WrapIndex(int i, int n) { return ((i % n) + n) % n; }

    // 15_0: gets the height map from the Surface file. Rebuilt only when the file or its values change.
    private void EnsureSurface()
    {
        Surface_SimulationSensor_Base_1_0 s = surface;
        if (s == null)
        {
            if (_defaultSurface == null) _defaultSurface = ScriptableObject.CreateInstance<Surface_SimulationSensor_Paper_1_0>();
            s = _defaultSurface;
        }
        if (s != _lastSurface || s.Version != _lastSurfaceVersion || _surfaceTex == null)
        {
            _sensorSample = 0;
            _currentSample = 0;
        }
        _surfaceTex = s.GetHeightMap(MmToUnit);
        _lastSurface = s;
        _lastSurfaceVersion = s.Version;

        surfaceInUse = (surface == null) ? "Paper (default, slot empty)" : s.name;
        surfaceHeightMinMM = s.HeightMinMM;
        surfaceHeightMaxMM = s.HeightMaxMM;
        surfaceRoughnessRmsMM = s.RoughnessRmsResultMM;
        surfaceReflectance = s.Reflectance;
    }

    private Vector2 ComputeMousePosition(float t)
    {
        float w = 2.0f * Mathf.PI * motionFrequencyHz;
        switch (motion)
        {
            case MouseMotion.Linear:
                return mouseStartXZ + motionAmplitude * Mathf.Sin(w * t);
            case MouseMotion.Circular:
                return mouseStartXZ + new Vector2(Mathf.Cos(w * t), Mathf.Sin(w * t)) * motionAmplitude.x;
            default:
                return mouseStartXZ;
        }
    }

    private bool SolveLens(float f, float a, out float R, out float d, out float h)
    {
        float n = Mathf.Max(refractiveIndex, 1.01f);
        float e = Mathf.Max(lensEdgeThicknessMM, 0.005f);
        bool fits = true;
        d = e;
        R = 2.0f * (n - 1.0f) * f;
        for (int i = 0; i < 40; i++)
        {
            float disc = Mathf.Max(f * f - f * d / n, 0.0f);
            R = (n - 1.0f) * (f + Mathf.Sqrt(disc));
            if (R < a * 1.001f) { R = a * 1.001f; fits = false; }   
            else fits = true;
            float sag = R - Mathf.Sqrt(R * R - a * a);
            d = 2.0f * sag + e;
        }
        h = (n - 1.0f) * f * d / (n * R);
        return fits;
    }

    // 19_0: % of the lens opening a sensor point (x, z from the axis) sees through both faces of the pinhole floor,
    // relative to the sensor center. Straight lines from the sensor to the lens center plane, 64 x 64 grid over the lens.
    private float PinholeLightPct(Vector2 sensorPoint, float lensCenterHeight)
    {
        float rLens = Mathf.Max(lensDiameterMM, 0.001f) * 0.5f;
        float rHole = Mathf.Max(pinholeDiameterMM, 0.001f) * 0.5f;
        // 23_0: the straight part of the ray runs from the lens to the glass bottom; it bends there toward the die.
        float span = Mathf.Max(glassBottomHeightMM - lensCenterHeight, 1e-4f);  // lens center plane to glass bottom
        float k0 = (floorUndersideHeightMM - lensCenterHeight) / span;          // where each floor face sits along the air part (0 = lens, 1 = glass bottom)
        float k1 = (floorTopHeightMM - lensCenterHeight) / span;
        float tG = Mathf.Max(chipGlassToWaferMM, 0.0f), nG = Mathf.Max(chipGlassIndex, 1.0f);
        const int N = 64;
        int passed = 0, passedCenter = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                Vector2 u = new Vector2(((i + 0.5f) / N * 2.0f - 1.0f) * rLens, ((j + 0.5f) / N * 2.0f - 1.0f) * rLens);
                if (u.sqrMagnitude > rLens * rLens) continue;
                Vector2 g = GlassBendPoint(u, sensorPoint, span, tG, nG);
                Vector2 g0 = GlassBendPoint(u, Vector2.zero, span, tG, nG);
                if (ThroughHole(u, g, k0, rHole) && ThroughHole(u, g, k1, rHole)) passed++;
                if (ThroughHole(u, g0, k0, rHole) && ThroughHole(u, g0, k1, rHole)) passedCenter++;
            }
        return passedCenter == 0 ? 0.0f : 100.0f * passed / passedCenter;
    }

    // 23_0: where the ray from lens point u to die point s crosses the glass bottom (Snell: sin(air) = n sin(glass)).
    // hAir = lens plane to glass bottom, tGlass = glass thickness. Solved by halving the interval.
    private static Vector2 GlassBendPoint(Vector2 u, Vector2 s, float hAir, float tGlass, float n)
    {
        Vector2 d = s - u;
        float D = d.magnitude;
        if (D < 1e-7f || tGlass <= 0.0f) return s;
        float lo = 0.0f, hi = D;                       // x = horizontal distance covered in the air
        for (int it = 0; it < 40; it++)
        {
            float x = 0.5f * (lo + hi);
            float sinAir = x / Mathf.Sqrt(x * x + hAir * hAir);
            float y = D - x;
            float sinGlass = y / Mathf.Sqrt(y * y + tGlass * tGlass);
            if (sinAir < n * sinGlass) lo = x; else hi = x;
        }
        return u + d * (0.5f * (lo + hi) / D);
    }

    private static bool ThroughHole(Vector2 lensPoint, Vector2 sensorPoint, float k, float rHole)
    {
        if (k <= 0.0f || k >= 1.0f) return true;            // that face is not between the lens and the sensor
        Vector2 p = lensPoint + (sensorPoint - lensPoint) * k;
        return p.sqrMagnitude <= rHole * rHole;
    }

    // 11_0: 1:1 lens. One input, a = surface to lens center = lens center to sensor.
    // Symmetric lens: each principal plane sits delta/2 from the lens center (delta = d - 2h).
    // So the distance to each principal plane is a - delta/2, and 1:1 needs f = (a - delta/2) / 2.
    // delta depends on the lens shape, so repeat until the values settle.
    private void UpdateLensDesign()
    {
        float a = Mathf.Max(surfaceToLensMM, 0.1f);
        float halfAperture = Mathf.Max(lensDiameterMM, 0.001f) * 0.5f;
        float R = 1.0f, d = 0.0f, h = 0.0f, delta = 0.0f;
        bool fits = true;

        float f = a * 0.5f;
        for (int it = 0; it < 30; it++)
        {
            fits = SolveLens(f, halfAperture, out R, out d, out h);
            delta = d - 2.0f * h;
            f = Mathf.Max((a - delta * 0.5f) * 0.5f, 0.001f);
        }
        fits = SolveLens(f, halfAperture, out R, out d, out h);
        delta = d - 2.0f * h;

        _so = 2.0f * f;   // 1/f = 1/so + 1/si with so = si gives so = si = 2f
        _si = 2.0f * f;
        _lensD = d; _lensH = h; _lensDelta = delta;

        // 23_0 chip glass: t of glass acts like t / n of air, so the sharp image lands (t - t/n) deeper.
        float tGlass = Mathf.Max(chipGlassToWaferMM, 0.0f);
        float nGlass = Mathf.Max(chipGlassIndex, 1.0f);
        glassFeltThicknessMM = tGlass / nGlass;
        focusShiftMM = tGlass - glassFeltThicknessMM;
        _glassShift = refocusForGlass ? focusShiftMM : 0.0f;
        float rg = (nGlass - 1.0f) / (nGlass + 1.0f);
        glassTransmissionPct = (1.0f - rg * rg) * 100.0f;
        pixelSizeMM = Mathf.Max(sensorSize.x, 0.001f) / 30.0f;

        focalLengthMM = f;
        curvatureRadiusMM = R;
        centerThicknessMM = d;
        principalPlaneOffsetMM = h;
        lensCenterToSensorMM = _si + delta * 0.5f + _glassShift;   // 23_0: + focus shift
        gapMM = a - d * 0.5f;
        sensorToSurfaceMM = _so + delta + _si + _glassShift;          // 23_0: + focus shift
        dieHeightMM = sensorToSurfaceMM;
        glassBottomHeightMM = sensorToSurfaceMM - tGlass;
        surfaceToChipTopMM = sensorToSurfaceMM + Mathf.Max(chipHeightMM - chipGlassToWaferMM, 0.0f);
        underHeightLimit = surfaceToChipTopMM <= heightLimitMM;
        magnificationResult = _si / _so;
        fNumber = f / Mathf.Max(lensDiameterMM, 0.001f);
        fieldOfViewMM = new Vector2(sensorSize.x, sensorSize.y) / Mathf.Max(magnificationResult, 1e-4f);

        // 12_0 housing: the floor (pinhole plate) sits on the lens top, the walls reach the chip top.
        float wall = Mathf.Max(housingWallMM, 0.01f);
        float gapToWall = Mathf.Max(chipToWallGapMM, 0.0f);
        pinholeDiameterMM = Mathf.Max(lensDiameterMM, 0.001f);
        floorAboveLensCenterMM = d * 0.5f;
        floorUndersideHeightMM = a + floorAboveLensCenterMM;
        floorTopHeightMM = floorUndersideHeightMM + Mathf.Max(housingFloorMM, 0.005f);
        housingHeightMM = Mathf.Max(surfaceToChipTopMM - floorUndersideHeightMM, 0.01f);
        housingOutsideSizeMM = new Vector2(Mathf.Max(chipBodyWidthMM, 0.1f) + 2.0f * (gapToWall + wall),
                                           Mathf.Max(chipBodyDepthMM, 0.1f) + 2.0f * (gapToWall + wall));
        floorToChipAirMM = (sensorToSurfaceMM - Mathf.Max(chipGlassToWaferMM, 0.0f)) - floorTopHeightMM;

        // 17_0 lens holder plate: from the bottom of the lens edge up to the floor underside, filling the walls' inside.
        holderSizeMM = new Vector2(Mathf.Max(chipBodyWidthMM, 0.1f) + 2.0f * gapToWall,
                                   Mathf.Max(chipBodyDepthMM, 0.1f) + 2.0f * gapToWall);
        holderBottomHeightMM = a - Mathf.Max(lensEdgeThicknessMM, 0.005f) * 0.5f;
        holderTopHeightMM = floorUndersideHeightMM;
        holderThicknessMM = holderTopHeightMM - holderBottomHeightMM;

        // 19_0 vignetting: share of the lens opening each sensor point still sees through the pinhole
        float halfX = Mathf.Max(sensorSize.x, 0.001f) * 0.5f, halfZ = Mathf.Max(sensorSize.y, 0.001f) * 0.5f;
        lightAtHalfwayPct = PinholeLightPct(new Vector2(halfX * 0.5f, 0.0f), a);
        lightAtEdgePct = PinholeLightPct(new Vector2(halfX, 0.0f), a);
        lightAtCornerPct = PinholeLightPct(new Vector2(halfX, halfZ), a);

        // 24_0 natural falloff (cos^4) from the lens center to the die as the light feels it
        float hFeel = Mathf.Max(_si + delta * 0.5f, 1e-4f);
        float cEdge = hFeel * hFeel / (hFeel * hFeel + halfX * halfX);
        float cCorner = hFeel * hFeel / (hFeel * hFeel + halfX * halfX + halfZ * halfZ);
        naturalFalloffEdgePct = cEdge * cEdge * 100.0f;
        naturalFalloffCornerPct = cCorner * cCorner * 100.0f;

        if (!fits) lensStatus = "Lens Diameter too large for this distance: reduce Lens Diameter or increase a.";
        else if (gapMM <= 0.0f) lensStatus = "The lens touches the surface: increase a.";
        else if (!underHeightLimit) lensStatus = "Sharp 1:1, but over the height limit: reduce a.";
        else if (floorToChipAirMM < 0.0f) lensStatus = "The pinhole floor reaches into the chip: increase a or reduce Housing Floor.";
        else lensStatus = "OK - sharp 1:1 image, under the height limit.";
    }

    // 16_0: LED placement and checks. All values are relative to the paper spot under the lens (0, 0, 0),
    // with Y = height above the paper. The LED sits on the +Z side and aims at the spot.
    private void UpdateLedDesign()
    {
        float h = Mathf.Max(ledWindowHeightMM, 0.05f);
        float ang = Mathf.Clamp(ledAngleDeg, 1.0f, 89.0f) * Mathf.Deg2Rad;
        float D = h / Mathf.Tan(ang);
        _ledWindowRel = new Vector3(0.0f, h, D);
        _ledAim = (-_ledWindowRel).normalized;                    // window normal, toward the spot
        _ledAxisL = Vector3.right;                                // body length lies sideways
        _ledAxisW = Vector3.Cross(_ledAim, _ledAxisL).normalized; // body width
        ledDistanceFromAxisMM = D;
        ledToSpotMM = _ledWindowRel.magnitude;

        // body corners: front face (window) at depth 0, back face at depth -height
        Vector3 size = new Vector3(Mathf.Max(ledBodySizeMM.x, 0.01f), Mathf.Max(ledBodySizeMM.y, 0.01f), Mathf.Max(ledBodySizeMM.z, 0.01f));
        float minY = float.MaxValue, maxY = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue, maxX = 0.0f;
        for (int i = 0; i < 8; i++)
        {
            float sl = ((i & 1) == 0 ? -0.5f : 0.5f) * size.x;
            float sw = ((i & 2) == 0 ? -0.5f : 0.5f) * size.y;
            float depth = (i & 4) == 0 ? 0.0f : -size.z;
            Vector3 p = _ledWindowRel + _ledAxisL * sl + _ledAxisW * sw + _ledAim * depth;
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            maxX = Mathf.Max(maxX, Mathf.Abs(p.x));
        }
        ledLowestPointMM = minY;
        ledHighestPointMM = maxY;
        ledNearestZMM = minZ;
        ledFarthestZMM = maxZ;

        // light angle at the patch edges (the patch the sensor sees, along Z)
        float fx = fieldOfViewMM.x * 0.5f, fz = fieldOfViewMM.y * 0.5f;
        angleAtNearEdgeDeg = Mathf.Atan2(h, D - fz) * Mathf.Rad2Deg;
        angleAtFarEdgeDeg = Mathf.Atan2(h, D + fz) * Mathf.Rad2Deg;

        // beam pattern: cos^m, half strength at half the beam angle
        float halfBeam = Mathf.Clamp(ledBeamAngleDeg, 10.0f, 170.0f) * 0.5f * Mathf.Deg2Rad;
        beamPower = Mathf.Log(0.5f) / Mathf.Log(Mathf.Cos(halfBeam));
        float minBeam = 1.0f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -fx : fx, 0.0f, (i & 2) == 0 ? -fz : fz);
            float c = Mathf.Max(Vector3.Dot((corner - _ledWindowRel).normalized, _ledAim), 0.0f);
            minBeam = Mathf.Min(minBeam, Mathf.Pow(c, beamPower));
        }
        beamAtPatchCornersPct = minBeam * 100.0f;

        // light shield = the housing wall on the LED side, from its inner face to its outer face
        float wall = Mathf.Max(housingWallMM, 0.01f);
        float zOuter = housingOutsideSizeMM.y * 0.5f - sensorOffsetMM.y;   // 23_0: the housing follows the chip, off the axis
        float zInner = zOuter - wall;
        usefulLightAtShieldMM = float.MinValue;
        foreach (float zp in new float[] { -fz, fz })             // LED -> patch edges (the highest rays)
            foreach (float zw in new float[] { zInner, zOuter })
                usefulLightAtShieldMM = Mathf.Max(usefulLightAtShieldMM, h * (zw - zp) / Mathf.Max(D - zp, 1e-4f));
        fogLightAtShieldMM = float.MaxValue;                      // LED -> lowest point of the lens
        foreach (float zw in new float[] { zInner, zOuter })
            fogLightAtShieldMM = Mathf.Min(fogLightAtShieldMM, gapMM + (h - gapMM) * zw / Mathf.Max(D, 1e-4f));
        float shield = Mathf.Max(shieldBottomHeightMM, 0.0f);
        bool hasSkirt = shield < floorUndersideHeightMM;
        shieldPassesUsefulLight = usefulLightAtShieldMM < shield;
        shieldBlocksFogLight = hasSkirt && fogLightAtShieldMM >= shield;

        // whole mouse fit (mouse centered on the sensor axis)
        float halfLimX = mouseSizeLimitMM.x * 0.5f, halfLimZ = mouseSizeLimitMM.y * 0.5f;
        mouseHighestPointMM = Mathf.Max(ledHighestPointMM, surfaceToChipTopMM);
        bool ledInside = maxX <= halfLimX && maxZ <= halfLimZ;
        bool housingInside = housingOutsideSizeMM.x * 0.5f + Mathf.Abs(sensorOffsetMM.x) <= halfLimX
                          && housingOutsideSizeMM.y * 0.5f + Mathf.Abs(sensorOffsetMM.y) <= halfLimZ;   // 23_0
        fitsInMouse = ledInside && housingInside && mouseHighestPointMM <= mouseHeightLimitMM && ledLowestPointMM > 0.0f;

        if (ledLowestPointMM <= surfaceHeightMaxMM) ledStatus = "The LED touches the paper: raise LED Window Height.";
        else if (ledNearestZMM <= zOuter) ledStatus = "The LED runs into the housing: lower LED Angle or raise LED Window Height.";
        else if (ledHighestPointMM > mouseHeightLimitMM) ledStatus = "The LED is over the mouse height limit: lower LED Window Height.";
        else if (!ledInside) ledStatus = "The LED is outside the mouse size limit: raise LED Angle or lower LED Window Height.";
        else if (!fitsInMouse) ledStatus = "The chip or housing does not fit the mouse size limit.";
        else if (!shieldPassesUsefulLight) ledStatus = "The shield blocks the useful light: raise Shield Bottom Height.";
        else if (!shieldBlocksFogLight) ledStatus = "The shield does not block the direct LED-to-lens light: lower Shield Bottom Height.";
        else ledStatus = "OK - LED fits, aimed at the patch, shield blocks the direct light.";
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        int width = source.width;
        int height = source.height;

        EnsureBuffersCreated(width, height);
        EnsureSensorImage();
        UpdateLensDesign();
        EnsureSurface();
        UpdateLedDesign();   // after EnsureSurface: the touch check uses the surface's highest point
        EnsureMarks();       // 20_0

        Vector2 mousePos = ComputeMousePosition(Time.time);
        bool mouseMoved = !_hasLastMousePos || (mousePos - _lastMousePos).sqrMagnitude > 1e-12f;
        _lastMousePos = mousePos;
        _hasLastMousePos = true;

        if (mouseMoved)
        {
            _currentSample = 0;     
            _sensorSample = 0;      
        }

        if (transform.hasChanged)
        {
            _currentSample = 0;
            transform.hasChanged = false;
        }

        _currentSample++;
        _sensorSample++;

        float surfaceRefY = patchCenter.y;   // the surface's mean height is patchCenter.y
        float sensorY = surfaceRefY + sensorToSurfaceMM;   // 11_0: = surface + 2a
        float opticalSensorY = sensorY - _glassShift;        // 23_0: where the die would be without the glass
        float lensY = opticalSensorY - _si - _lensD * 0.5f + _lensH;
        float focusPlaneY = opticalSensorY - _si - _lensDelta - _so;   // lands on the surface at 1:1
        float rearPlaneY = opticalSensorY - _si;                    // lens point on the sensor side
        float frontPlaneY = opticalSensorY - _si - _lensDelta;      // lens point on the surface side
        float sensorToLensCenter = opticalSensorY - lensY;     
        float lensCenterToFocus = lensY - focusPlaneY;  

        // 12_0 housing: floor underside on the lens top, size around the chip, height up to the chip top.
        float housingBottomY = lensY + floorAboveLensCenterMM;
        float housingHalfW = housingOutsideSizeMM.x * 0.5f;
        float housingHalfD = housingOutsideSizeMM.y * 0.5f;
        float housingWall = Mathf.Min(Mathf.Max(housingWallMM, 0.01f), Mathf.Min(housingHalfW, housingHalfD) * 0.9f);

        int kernelMain = hybridShader.FindKernel("CSMain");
        int nSub = (int)subSamplesPerSide;                                      // 22_0
        int kernelSensor = hybridShader.FindKernel("CSSensor" + nSub);

        // --- Uniform Bindings ---
        hybridShader.SetFloat("_Time", Time.time);
        hybridShader.SetInt("_SampleIndex", _currentSample);
        // 16_0 LED: window center in the scene, moving with the mouse
        Vector3 spotMM = new Vector3(mousePos.x, surfaceRefY, mousePos.y);   // paper spot under the lens
        Vector3 lightPosition = spotMM + _ledWindowRel;
        Vector3 ledSize = new Vector3(Mathf.Max(ledBodySizeMM.x, 0.01f), Mathf.Max(ledBodySizeMM.y, 0.01f), Mathf.Max(ledBodySizeMM.z, 0.01f));
        hybridShader.SetVector("_LightPosition", new Vector4(lightPosition.x * MmToUnit, lightPosition.y * MmToUnit, lightPosition.z * MmToUnit, 1.0f));
        hybridShader.SetFloat("_LightIntensity", lightIntensity);
        hybridShader.SetVector("_LedAim", _ledAim);
        hybridShader.SetVector("_LedAxisL", _ledAxisL);
        hybridShader.SetVector("_LedAxisW", _ledAxisW);
        hybridShader.SetVector("_LedHalf", new Vector4(ledSize.x * 0.5f, ledSize.z * 0.5f, ledSize.y * 0.5f, 0.0f) * MmToUnit);   // x = length, y = along the aim, z = width
        hybridShader.SetFloat("_LedBase", Mathf.Min(Mathf.Max(ledBaseMM, 0.0f), ledSize.z) * MmToUnit);
        hybridShader.SetFloat("_LedWindowRadius", Mathf.Max(ledWindowRadiusMM, 0.01f) * MmToUnit);
        hybridShader.SetFloat("_LedBeamPower", Mathf.Max(beamPower, 0.01f));
        hybridShader.SetFloat("_IntensityClamp", intensityClamp);

        int cellsX = Mathf.Clamp(gridCells.x, 1, 256);
        int cellsY = Mathf.Clamp(gridCells.y, 1, 256);
        hybridShader.SetVector("_SensorCenter", new Vector4(mousePos.x * MmToUnit, sensorY * MmToUnit, mousePos.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_SensorHalfSize", new Vector4(Mathf.Max(sensorSize.x, 0.001f) * 0.5f * MmToUnit, Mathf.Max(sensorSize.y, 0.001f) * 0.5f * MmToUnit, 0, 0));
        hybridShader.SetVector("_SensorGridCells", new Vector4(cellsX, cellsY, 0, 0));
        hybridShader.SetFloat("_SensorGridEnabled", showGrid ? 1.0f : 0.0f);
        hybridShader.SetFloat("_SensorLineWidth", gridLineWidth);
        hybridShader.SetFloat("_SensorBorderWidth", borderWidth);
        hybridShader.SetVector("_SensorColor", ColorToVector(sensorColor));
        hybridShader.SetVector("_SensorGridColor", ColorToVector(gridLineColor));
        hybridShader.SetVector("_SensorBorderColor", ColorToVector(borderColor));

        // 23_0: the chip (and the housing and holder around it) sits off the lens axis; the pinhole stays on the axis
        Vector2 chipXZ = mousePos - sensorOffsetMM;
        hybridShader.SetVector("_PinholeXZ", new Vector4(mousePos.x * MmToUnit, mousePos.y * MmToUnit, 0.0f, 0.0f));
        hybridShader.SetVector("_HousingCenter", new Vector4(chipXZ.x * MmToUnit, housingBottomY * MmToUnit, chipXZ.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_HousingSize", new Vector4(housingHalfW * MmToUnit, Mathf.Max(housingHeightMM, 0.1f) * MmToUnit, housingHalfD * MmToUnit, 0.0f));
        hybridShader.SetFloat("_HousingWall", housingWall * MmToUnit);
        hybridShader.SetFloat("_HousingFloor", Mathf.Max(housingFloorMM, 0.005f) * MmToUnit);   // 12_0
        hybridShader.SetFloat("_PinholeRadius", Mathf.Max(pinholeDiameterMM, 0.01f) * 0.5f * MmToUnit);
        // 16_0 light shield: the walls reach down to this height (never higher than the floor underside)
        hybridShader.SetFloat("_HousingWallBottom", Mathf.Min(surfaceRefY + Mathf.Max(shieldBottomHeightMM, 0.0f), housingBottomY) * MmToUnit);
        // 17_0 lens holder plate: inside of the walls, from the lens edge bottom up to the floor underside
        float holderBottomY = lensY - Mathf.Max(lensEdgeThicknessMM, 0.005f) * 0.5f;
        float holderHalfH = Mathf.Max(housingBottomY - holderBottomY, 0.0f) * 0.5f;
        hybridShader.SetVector("_HolderCenter", new Vector4(chipXZ.x * MmToUnit, (holderBottomY + holderHalfH) * MmToUnit, chipXZ.y * MmToUnit, 0.0f));
        // 23_0 chip glass: chip body footprint, from the glass bottom up to the die
        float tGl = Mathf.Max(chipGlassToWaferMM, 0.0f);
        float glassBottomY = sensorY - tGl;
        hybridShader.SetFloat("_GlassBottomY", glassBottomY * MmToUnit);
        hybridShader.SetFloat("_GlassIndex", Mathf.Max(chipGlassIndex, 1.0f));
        hybridShader.SetFloat("_GlassShift", focusShiftMM * MmToUnit);
        hybridShader.SetVector("_ChipGlassCenter", new Vector4(chipXZ.x * MmToUnit, (glassBottomY + tGl * 0.5f - 0.0005f) * MmToUnit, chipXZ.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_ChipGlassHalf", new Vector4(Mathf.Max(chipBodyWidthMM, 0.1f) * 0.5f * MmToUnit, Mathf.Max(tGl * 0.5f - 0.0005f, 0.0f) * MmToUnit, Mathf.Max(chipBodyDepthMM, 0.1f) * 0.5f * MmToUnit, 0.0f));
        hybridShader.SetVector("_HolderHalfSize", new Vector4(Mathf.Max(housingHalfW - housingWall, 0.0f) * MmToUnit, holderHalfH * MmToUnit, Mathf.Max(housingHalfD - housingWall, 0.0f) * MmToUnit, 0.0f));

        hybridShader.SetVector("_RayApexTop", new Vector4(mousePos.x * MmToUnit, rearPlaneY * MmToUnit, mousePos.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_RayApexBottom", new Vector4(mousePos.x * MmToUnit, frontPlaneY * MmToUnit, mousePos.y * MmToUnit, 0.0f));
        hybridShader.SetFloat("_FocusPlaneY", focusPlaneY * MmToUnit);
        hybridShader.SetFloat("_SurfaceRatio", _so / Mathf.Max(_si, 1e-4f));
        hybridShader.SetFloat("_VectorRadius", vectorThicknessMM * MmToUnit);

        hybridShader.SetFloat("_LensY", lensY * MmToUnit);
        hybridShader.SetFloat("_LensRadius", Mathf.Max(lensDiameterMM, 0.001f) * 0.5f * MmToUnit);
        hybridShader.SetFloat("_SensorImageDist", sensorToLensCenter * MmToUnit);
        hybridShader.SetFloat("_ObjectDist", lensCenterToFocus * MmToUnit);
        hybridShader.SetFloat("_LensCurvRadius", curvatureRadiusMM * MmToUnit);
        hybridShader.SetFloat("_LensThickness", centerThicknessMM * MmToUnit);
        hybridShader.SetFloat("_LensEdge", Mathf.Max(lensEdgeThicknessMM, 0.005f) * MmToUnit);
        hybridShader.SetFloat("_LensIndex", Mathf.Max(refractiveIndex, 1.01f));
        hybridShader.SetFloat("_UseRealLens", useRealLens ? 1.0f : 0.0f);
        hybridShader.SetFloat("_SensorOpacity", sensorOpacity);
        hybridShader.SetFloat("_SensorBlend", 1.0f / _sensorSample);
        hybridShader.SetFloat("_ShowSensorImage", showSensorImage ? 1.0f : 0.0f);
        hybridShader.SetFloat("_SensorExposure", sensorExposure);
        // 24_0 stray light
        hybridShader.SetFloat("_HousingReflectance", housingIrReflectance);
        hybridShader.SetInt("_StrayBounces", Mathf.Clamp(strayBounces, 0, 2));
        hybridShader.SetFloat("_LensGhosts", lensGhosts ? 1.0f : 0.0f);
        hybridShader.SetFloat("_NaturalFalloff", naturalFalloff ? 1.0f : 0.0f);
        hybridShader.SetInt("_SensorView", (int)sensorView);
        hybridShader.SetFloat("_ShowOptics", showOptics ? 1.0f : 0.0f);
        hybridShader.SetFloat("_EnableLensVisuals", enableLensVisuals ? 1.0f : 0.0f); // Pass toggle to compute shader

        hybridShader.SetVector("_PatchCenter", patchCenter * MmToUnit);
        hybridShader.SetFloat("_PatchHalfSize", Mathf.Max(patchSize, 0.001f) * 0.5f * MmToUnit);
        // surface (15_0: values come from the Surface file)
        float refDist2 = (lightPosition - spotMM).sqrMagnitude * MmToUnit * MmToUnit;   // LED to the paper point on the sensor axis
        hybridShader.SetFloat("_PaperHeightMin", surfaceHeightMinMM * MmToUnit);
        hybridShader.SetFloat("_PaperHeightMax", surfaceHeightMaxMM * MmToUnit);
        hybridShader.SetFloat("_PaperTileSize", Surface_SimulationSensor_Base_1_0.TileMM * MmToUnit);
        hybridShader.SetFloat("_PaperTexel", Surface_SimulationSensor_Base_1_0.TexelMM * MmToUnit);
        hybridShader.SetFloat("_PaperReflectance", surfaceReflectance);
        hybridShader.SetFloat("_PaperRefDist2", Mathf.Max(refDist2, 1e-12f));
        hybridShader.SetTexture(kernelSensor, "_PaperHeight", _surfaceTex);
        hybridShader.SetTexture(kernelMain, "_PaperHeight", _surfaceTex);
        // 20_0 surface marks: reflectance = lerp(surface, mark, coverage)
        hybridShader.SetTexture(kernelSensor, "_MarkMap", _markTex);
        hybridShader.SetTexture(kernelMain, "_MarkMap", _markTex);
        hybridShader.SetFloat("_MarkReflectance", markReflectance);
        hybridShader.SetFloat("_MarksEnabled", showMarks ? 1.0f : 0.0f);

        hybridShader.SetTexture(kernelSensor, "SensorImage", _sensorImage);
        // 22_0: one thread group per pixel, one thread per ray
        hybridShader.Dispatch(kernelSensor, _sensorImage.width, _sensorImage.height, 1);
        raysPerFrame = _sensorImage.width * _sensorImage.height * nSub * nSub * (strayBounces > 0 ? 2 : 1);   // 24_0: + one stray ray each

        // 24_0: read the sensor image back now and then to measure the stray light share (g = clean, b = stray)
        if (!_strayReadPending && Time.frameCount % 30 == 0)
        {
            _strayReadPending = true;
            AsyncGPUReadback.Request(_sensorImage, 0, TextureFormat.RGBAFloat, req =>
            {
                _strayReadPending = false;
                if (req.hasError) return;
                var px = req.GetData<Color>();
                double clean = 0.0, stray = 0.0;
                for (int i = 0; i < px.Length; i++) { clean += px[i].g; stray += px[i].b; }
                strayLightPct = clean + stray > 0.0 ? (float)(100.0 * stray / (clean + stray)) : 0.0f;
            });
        }

        hybridShader.SetTexture(kernelMain, "SensorImage", _sensorImage);
        hybridShader.SetTexture(kernelMain, "AccumulationBuffer", _accumulationBuffer);
        hybridShader.SetTexture(kernelMain, "Result", _targetBuffer);

        hybridShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        hybridShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);

        int threadGroupsX = Mathf.CeilToInt(width / 32f);
        int threadGroupsY = Mathf.CeilToInt(height / 32f);
        hybridShader.Dispatch(kernelMain, threadGroupsX, threadGroupsY, 1);

        Graphics.Blit(_targetBuffer, destination);
    }

    private static Vector4 ColorToVector(Color c)
    {
        return new Vector4(c.r, c.g, c.b, c.a);
    }

    private void EnsureSensorImage()
    {
        int w = Mathf.Clamp(gridCells.x, 1, 256);
        int h = Mathf.Clamp(gridCells.y, 1, 256);
        if (_sensorImage == null || _sensorImage.width != w || _sensorImage.height != h)
        {
            if (_sensorImage != null) _sensorImage.Release();
            _sensorImage = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point
            };
            _sensorImage.Create();
            _sensorSample = 0;
        }
    }

    private void EnsureBuffersCreated(int width, int height)
    {
        if (_targetBuffer == null || _targetBuffer.width != width || _targetBuffer.height != height)
        {
            if (_targetBuffer != null) _targetBuffer.Release();
            _targetBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true };
            _targetBuffer.Create();
        }

        if (_accumulationBuffer == null || _accumulationBuffer.width != width || _accumulationBuffer.height != height)
        {
            if (_accumulationBuffer != null) _accumulationBuffer.Release();
            _accumulationBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { enableRandomWrite = true };
            _accumulationBuffer.Create();
            _currentSample = 0;
        }
    }

    private void OnDisable()
    {
        if (_targetBuffer != null) _targetBuffer.Release();
        if (_accumulationBuffer != null) _accumulationBuffer.Release();
        if (_sensorImage != null) _sensorImage.Release();
        if (_defaultSurface != null) { Destroy(_defaultSurface); _defaultSurface = null; }
        if (_markTex != null) { DestroyImmediate(_markTex); _markTex = null; }
        _marksDirty = true;
    }
}