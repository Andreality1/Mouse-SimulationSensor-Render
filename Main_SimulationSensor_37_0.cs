using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class Main_SimulationSensor_37_0 : MonoBehaviour
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
    public enum SubSamples { Grid4x4 = 4, Grid8x8 = 8, Grid16x16 = 16 }
    [Tooltip("22_0: rays per pixel in ONE frame = N x N grid inside the pixel, one GPU thread per ray. 8 x 8 = 64, 16 x 16 = 256.")]
    [SerializeField] private SubSamples subSamplesPerSide = SubSamples.Grid8x8;
    [Tooltip("22_0 result: rays traced for the sensor in one frame (30 x 30 x N x N).")]
    [SerializeField] private int raysPerFrame;   // calculated - edits are ignored
    [SerializeField] private float sensorExposure = 1.0f;
    [Tooltip("25_0: most bounces of a ray in the main 3D view (was 8). 3 keeps nearly the same look and is much faster. The sensor image is not affected.")]
    [SerializeField, Range(1, 8)] private int mainViewBounces = 3;

    // 24_0 (stage 6.3): light that reaches the sensor without the clean path paper -> lens -> pinhole -> glass -> die.
    // 27_0: each object of the main 3D view can be hidden. DRAWING ONLY: the sensor image always uses every object
    // (lens, pinhole, housing, glass, surface, marks), so hiding one never changes the physics.
    [Header("Main View Objects (27_0, drawing only)")]
    [SerializeField] private bool showSurface = true;
    [Tooltip("Hides the marks in the main view only. The sensor still sees them (see Marks Active).")]
    [SerializeField] private bool showMarks = true;
    [SerializeField] private bool showGroundPlane = true;
    [SerializeField] private bool showLedBody = true;
    [Tooltip("The red stand-in glow of the window. The LED keeps lighting the surface when hidden.")]
    [SerializeField] private bool showLedGlow = true;
    [SerializeField] private bool showLens = true;
    [SerializeField] private bool showLensHolder = true;
    [SerializeField] private bool showHousingFloor = true;
    [Tooltip("The four walls and the light shield below them.")]
    [SerializeField] private bool showHousingWalls = true;
    [SerializeField] private bool showChipGlass = true;
    [Tooltip("The dark chip body above the glass (the top cover). Hide it to see the sensor from above.")]
    [SerializeField] private bool showSiliconDie = true;
    [SerializeField] private bool showSensorPlane = true;
    [Tooltip("The 30 x 30 sensor picture drawn on the sensor plane.")]
    [SerializeField] private bool showSensorImage = true;
    [SerializeField] private bool showSensorGrid = true;
    [Tooltip("Chief-ray arrows from the lens up to the sensor pixels.")]
    [SerializeField] private bool showArrowsUp = true;
    [Tooltip("Chief-ray arrows from the lens down to the surface.")]
    [SerializeField] private bool showArrowsDown = true;
    [SerializeField] private bool showAxes = true;

    // 31_0: the sensor case drawn like a real molded optical-mouse sensor package (user photo: grey molded body,
    // black base frame on the optical side, gold pins on two sides, mold dimples and laser marking on top, gold ring
    // around the aperture). DRAWING ONLY: the sensor image, stray light and every size check are unchanged.
    // The package style is REPRESENTATIVE: the PAW3220LU-TJDU sizes are kept, only the look comes from the photo.
    [Header("Sensor Case Look (31_0, drawing only)")]
    [Tooltip("ON: housing drawn as a real sensor package (grey molded plastic, black base, bevelled edges, gold aperture ring). OFF: plain matte black, as in 30_0.")]
    [SerializeField] private bool realCaseLook = true;
    [Tooltip("Closed top face with mold dimples and laser marking, flush with the chip top. Hide it to see the chip inside.")]
    [SerializeField] private bool showCaseTop = true;
    [Tooltip("4 gold pins on each X side, running up to the PCB. Drawing only: not in the mouse size or height checks.")]
    [SerializeField] private bool showCasePins = true;
    [Tooltip("Pin width (along Z). REPRESENTATIVE: photo pin scaled to this case size.")]
    [SerializeField, Min(0.01f)] private float pinWidthMM = 0.12f;
    [Tooltip("Pin metal thickness. REPRESENTATIVE.")]
    [SerializeField, Min(0.005f)] private float pinThicknessMM = 0.05f;
    [Tooltip("How far the pins stick out from the side walls before bending up.")]
    [SerializeField, Min(0.0f)] private float pinOutMM = 0.12f;
    [Tooltip("How far the pins rise above the case top (into the PCB room, 0.714 mm in the stack drawing).")]
    [SerializeField, Min(0.0f)] private float pinRiseMM = 0.40f;
    [Tooltip("Pin exit height below the case top.")]
    [SerializeField, Min(0.0f)] private float pinExitBelowTopMM = 0.12f;
    [Tooltip("Rounded look of the outer case edges (shading only, the box shape is unchanged).")]
    [SerializeField, Range(0.0f, 0.1f)] private float caseEdgeRoundMM = 0.05f;   // 34_0: real edge radius when Real Geometry is on

    // 32_0: the LED drawn like a real 1206 chip LED (3.2 x 1.6 mm): white substrate base with gold end terminals,
    // water-clear epoxy top, and inside it the gold pads, the small die and its bond wire, seen through the epoxy.
    // DRAWING ONLY: the light on the paper and the sensor image are unchanged (same point light, same beam).
    // The inside layout (pad shapes, die 0.30 mm, wire) is REPRESENTATIVE of this LED type: the datasheet does not draw it.
    [Header("LED Look (32_0, drawing only)")]
    [Tooltip("ON: LED drawn as a real chip LED (substrate, gold terminals, clear epoxy, die, bond wire). OFF: plain two-color box with a glowing disc, as in 31_0.")]
    [SerializeField] private bool realLedLook = true;
    [Tooltip("Brightness of the red glow on the die and around it (needs Show Led Glow). A real 850 nm LED is almost invisible to the eye: only a faint deep-red dot on the die. 0.3 = close to real, higher = easier to see.")]
    [SerializeField, Range(0.0f, 20.0f)] private float ledDieGlow = 3.0f;

    // 33_0: beauty renderer for the main view (phases 1-4: display pipeline, studio lighting, physical materials,
    // light transport). DRAWING ONLY: the sensor image and every physics result are unchanged.
    [System.Serializable]
    public class StudioLight
    {
        public bool on = true;
        [Tooltip("Direction the light sits in, seen from above: 0 = +X, 90 = +Z (the LED side).")]
        public float azimuthDeg;
        [Tooltip("Height angle above the paper.")]
        [Range(5.0f, 89.0f)] public float elevationDeg = 45.0f;
        [Tooltip("Distance from the sensor area to the light.")]
        [Min(1.0f)] public float distanceMM = 40.0f;
        [Tooltip("Side of the square light. Larger = softer shadows.")]
        [Min(0.1f)] public float sizeMM = 30.0f;
        [Min(0.0f)] public float intensity = 5.0f;
        [Tooltip("Color temperature: 3200 = warm, 5600 = daylight, 7500 = cool.")]
        [Range(2000.0f, 12000.0f)] public float kelvin = 5600.0f;
        public StudioLight() { }
        public StudioLight(float az, float el, float dist, float size, float inten, float k)
        { azimuthDeg = az; elevationDeg = el; distanceMM = dist; sizeMM = size; intensity = inten; kelvin = k; }
    }
    public enum ToneMapper { AgX = 0, ACES = 1, Reinhard = 2 }
    public enum Background { SolidColor = 0, Environment = 1 }

    [Header("Beauty Render - Quality (33_0, drawing only)")]
    [Tooltip("Camera rays per pixel in one frame. The picture keeps getting cleaner while nothing moves. 1-2 = fast, 4-8 = cleaner per frame but slower.")]
    [SerializeField, Range(1, 16)] private int samplesPerFrame = 2;
    [Tooltip("Most surface bounces of one light path. 1 = direct light only, 4-6 = full bounced light.")]
    [SerializeField, Range(1, 12)] private int pathBounces = 4;
    [Tooltip("ON: the paper fibers cast LED shadows in the main view (slow). OFF: the paper is lit without fiber shadows, as in 32_0.")]
    [SerializeField] private bool paperShadows = false;
    [Tooltip("Limit on the brightness one bounced path may add. Removes rare very bright speckles. Lower = cleaner but slightly darker bounced light.")]
    [SerializeField, Min(0.1f)] private float fireflyClamp = 20.0f;

    [Header("Beauty Render - Display (33_0)")]
    [Tooltip("AgX = the default look in Blender. ACES = film / car-render look, more contrast. Reinhard = the old curve.")]
    [SerializeField] private ToneMapper toneMapper = ToneMapper.AgX;
    [Tooltip("Picture brightness in stops: +1 = twice as bright, -1 = half.")]
    [SerializeField, Range(-6.0f, 6.0f)] private float exposureEV = 0.0f;
    [Tooltip("6500 = neutral. Lower makes the picture cooler (blue), higher makes it warmer.")]
    [SerializeField, Range(2500.0f, 12000.0f)] private float whiteBalanceK = 6500.0f;

    [Header("Beauty Render - Lighting (33_0)")]
    [Tooltip("Optional HDRI (equirectangular .hdr / .exr, Texture Shape 2D, Wrap Mode Repeat). Empty = built-in studio (soft box overhead, two side strips).")]
    [SerializeField] private Texture2D environmentMap;
    [SerializeField, Min(0.0f)] private float environmentIntensity = 1.0f;
    [SerializeField, Range(0.0f, 360.0f)] private float environmentRotationDeg = 0.0f;
    [Tooltip("What the camera sees where there is no object. The environment lights the scene and shows in reflections either way.")]
    [SerializeField] private Background background = Background.SolidColor;
    [SerializeField] private Color backdropColor = new Color(0.045f, 0.047f, 0.052f, 1.0f);
    [Tooltip("Base color of the ground plane under the paper.")]
    [SerializeField] private Color floorColor = new Color(0.20f, 0.20f, 0.21f, 1.0f);
    [Tooltip("Three soft square lights. They light the main view only: the sensor still sees only the LED. They are not drawn themselves, but show in reflections.")]
    [SerializeField] private bool studioLights = true;
    [SerializeField] private StudioLight keyLight  = new StudioLight(40.0f, 55.0f, 40.0f, 30.0f, 6.0f, 5600.0f);
    [SerializeField] private StudioLight fillLight = new StudioLight(-70.0f, 30.0f, 50.0f, 40.0f, 2.0f, 6500.0f);
    [SerializeField] private StudioLight rimLight  = new StudioLight(200.0f, 35.0f, 45.0f, 20.0f, 5.0f, 7500.0f);
    [Tooltip("Strength of the red stand-in light of the LED in the main view. 1 = same paper brightness as 32_0.")]
    [SerializeField, Min(0.0f)] private float ledViewStrength = 1.0f;

    [Header("Beauty Render - Materials (33_0)")]
    [Tooltip("Roughness: 0 = mirror, 1 = fully matte. Grey molded case.")]
    [SerializeField, Range(0.05f, 1.0f)] private float caseRoughness = 0.55f;
    [Tooltip("Gold pins and LED terminals (metal).")]
    [SerializeField, Range(0.05f, 1.0f)] private float goldRoughness = 0.25f;
    [Tooltip("Chip top (silicon die).")]
    [SerializeField, Range(0.05f, 1.0f)] private float dieRoughness = 0.20f;
    [Tooltip("Clear LED epoxy surface.")]
    [SerializeField, Range(0.05f, 1.0f)] private float epoxyRoughness = 0.06f;
    // 34_0 phase 5: geometry detail (drawing only)
    [Header("Beauty Render - Geometry (34_0, drawing only)")]
    [Tooltip("ON: the case shell, the LED body and the pins have real rounded edges (Case Edge Round is the radius). OFF: sharp boxes, as in 33_0.")]
    [SerializeField] private bool realGeometry = true;
    [Tooltip("Edge radius of the LED body. Molded chip LEDs have soft edges. REPRESENTATIVE.")]
    [SerializeField, Range(0.0f, 0.3f)] private float ledEdgeRoundMM = 0.08f;
    [Tooltip("Pin edge radius as a share of the pin thickness (1 = fully round wire).")]
    [SerializeField, Range(0.0f, 1.0f)] private float pinRoundness = 0.8f;
    [Tooltip("ON: real characters laser-marked on the case top. OFF: dot pattern, as in 31_0.")]
    [SerializeField] private bool realMarkingText = true;
    [Tooltip("Marking, line 1 (A-Z, 0-9, '-', '.', space). Your text: the real PAW3220 marking is not published.")]
    [SerializeField] private string markingLine1 = "PAW3220";
    [Tooltip("Marking, line 2.")]
    [SerializeField] private string markingLine2 = "LU-TJDU";
    [Tooltip("Circuit board above the chip (green solder mask, copper traces, white outline, gold pads where the pins go through). It blocks studio light from above, so the scene under it gets darker.")]
    [SerializeField] private bool showPcb = false;
    [Tooltip("Board width (X) x depth (Z), centered on the chip.")]
    [SerializeField] private Vector2 pcbSizeMM = new Vector2(12.0f, 12.0f);
    [Tooltip("Board thickness. The PCB room in the stack drawing is 0.714 mm.")]
    [SerializeField, Min(0.05f)] private float pcbThicknessMM = 0.6f;
    [Tooltip("Air between the case top and the board underside.")]
    [SerializeField, Min(0.0f)] private float pcbGapMM = 0.05f;

    // 34_0 phase 6: camera and picture effects (drawing only)
    [Header("Beauty Render - Camera (34_0, drawing only)")]
    [Tooltip("ON: a real camera lens, sharp only at the focus distance, as in a macro photo.")]
    [SerializeField] private bool depthOfField = true;
    [Tooltip("Camera lens opening. Larger = shallower focus. 0.2 = almost all sharp, 1-2 = very shallow.")]
    [SerializeField, Range(0.0f, 5.0f)] private float apertureDiameterMM = 0.6f;
    [Tooltip("ON: focus on whatever is at the center of the screen. OFF: use Focus Distance.")]
    [SerializeField] private bool autoFocus = true;
    [Tooltip("Distance from the camera to the sharp plane (when Auto Focus is off).")]
    [SerializeField, Min(0.01f)] private float focusDistanceMM = 20.0f;
    [Tooltip("Soft glow around very bright spots (LED die, highlights on gold). 0 = off.")]
    [SerializeField, Range(0.0f, 1.0f)] private float bloomStrength = 0.10f;
    [Tooltip("How bright a spot must be (after exposure) before it glows. Lower = more glow.")]
    [SerializeField, Range(0.0f, 10.0f)] private float bloomThreshold = 1.5f;
    [Tooltip("Darker picture corners, as in a real lens. 0 = off.")]
    [SerializeField, Range(0.0f, 1.0f)] private float vignette = 0.25f;
    [Tooltip("Red / blue color fringe toward the picture corners, in pixels. 0 = off, 1-3 = subtle.")]
    [SerializeField, Range(0.0f, 8.0f)] private float chromaticAberration = 0.0f;

    // 35_0 phase 7: clean pictures and speed (drawing only)
    public enum PreviewScale { Full = 1, Half = 2, Quarter = 4 }
    [Header("Beauty Render - Clean Picture (35_0, drawing only)")]
    [Tooltip("ON: removes the grain. Edges, text and printed patterns stay sharp; only the lighting noise is smoothed. It fades out by itself as the picture gets clean.")]
    [SerializeField] private bool denoise = true;
    [Tooltip("1 = normal. Higher = smoother but may soften fine shading. Lower = keeps more grain.")]
    [SerializeField, Range(0.1f, 4.0f)] private float denoiseStrength = 1.0f;
    [Tooltip("Number of filter passes (each one twice as wide). 4 = good default, 5 = smoothest.")]
    [SerializeField, Range(1, 5)] private int denoisePasses = 4;
    [Tooltip("ON: while the camera or the mouse moves, render fast (1 ray per pixel block, fewer bounces). When everything is still, full quality starts.")]
    [SerializeField] private bool fastPreviewWhileMoving = true;
    [Tooltip("Picture resolution while moving.")]
    [SerializeField] private PreviewScale previewResolution = PreviewScale.Half;
    [Tooltip("Light bounces while moving.")]
    [SerializeField, Range(1, 12)] private int previewBounces = 2;
    [Tooltip("How long everything must be still before full quality starts (seconds).")]
    [SerializeField, Range(0.0f, 2.0f)] private float stillDelaySec = 0.15f;
    [Tooltip("Shows the mode, the sample count and screenshot messages in the top left corner of the Game view.")]
    [SerializeField] private bool showStats = true;

    [Header("Beauty Render - Screenshot (35_0)")]
    [Tooltip("Press this key in Play mode (or use the component menu: Save Beauty Screenshot). The picture is saved when it reaches Screenshot Samples, so keep the camera still.")]
    [SerializeField] private KeyCode screenshotKey = KeyCode.F12;
    [Tooltip("Camera rays per pixel before the picture is saved. 256 = quick, 1024 = very clean.")]
    [SerializeField, Min(1)] private int screenshotSamples = 512;
    [Tooltip("Also save the full-range picture (before the tone curve) as .exr, for editing in Blender / Photoshop.")]
    [SerializeField] private bool saveExr = false;

    // 36_0: movable light (drawing only: the sensor image still sees only the LED)
    public enum MovableLightMode { Off, Position, FollowObject, Orbit }
    [Header("Movable Light (36_0; spot and directional 37_0; drawing only)")]
    [Tooltip("Off. Position = the position below (arrow keys and Page Up / Page Down move it in Play mode). Follow Object = follows the object below: drag it in the Scene view. Orbit = circles the sensor by itself.")]
    [SerializeField] private MovableLightMode movableLight = MovableLightMode.Position;
    public enum MovableLightType { Spot, Directional, PointBall }
    public enum SpotAim { AtAimPoint, ObjectRotation, Angles }
    [Tooltip("Spot = a lamp that sends light only inside a cone (like a stage spotlight). Directional = parallel light from one direction, like the sun. Point Ball = the glowing ball of 36_0, light in every direction.")]
    [SerializeField] private MovableLightType lightType = MovableLightType.Spot;
    [Tooltip("ON: the scene is lit by this light only (environment and studio lights off). The device LED keeps its own red light: set Led View Strength to 0 to remove it too. OFF: this light is added to the others.")]
    [SerializeField] private bool spotlightOnly = true;
    [Tooltip("Where the spot / directional light points. At Aim Point = always at the point below, wherever the light moves. Object Rotation = the forward (blue Z) arrow of the Light Object: rotate it in the Scene view (Follow Object mode). Angles = the yaw and pitch below.")]
    [SerializeField] private SpotAim spotAim = SpotAim.AtAimPoint;
    [Tooltip("At Aim Point: the point the light aims at, from the paper spot under the sensor at the start (Y = height). (0, 1, 0) = the middle of the chip stack.")]
    [SerializeField] private Vector3 aimPointMM = new Vector3(0.0f, 1.0f, 0.0f);
    [Tooltip("Angles: turn around the vertical axis. 0 = toward +Z, 90 = toward +X.")]
    [SerializeField, Range(-180.0f, 180.0f)] private float aimYawDeg = 0.0f;
    [Tooltip("Angles: up / down. -90 = straight down, 0 = level.")]
    [SerializeField, Range(-90.0f, 90.0f)] private float aimPitchDeg = -45.0f;
    [Tooltip("Spot: full cone angle (as the Spot Angle of a Unity light). Smaller = a tighter pool of light.")]
    [SerializeField, Range(1.0f, 170.0f)] private float spotAngleDeg = 30.0f;
    [Tooltip("Spot: how much of the cone fades out toward its edge. 0 = hard edge, 1 = fades all the way from the center.")]
    [SerializeField, Range(0.0f, 1.0f)] private float spotSoftEdge = 0.25f;
    [Tooltip("Directional: angular size of the light. 0.5 = sharp shadows like the sun, 5 or more = soft shadows like an overcast sky window.")]
    [SerializeField, Range(0.01f, 30.0f)] private float directionalSoftnessDeg = 1.0f;
    [Tooltip("Position mode: X, height above the paper (Y), Z, measured from the paper spot under the sensor at the start.")]
    [SerializeField] private Vector3 lightPositionMM = new Vector3(-8.0f, 10.0f, -6.0f);
    [Tooltip("Follow Object mode: any GameObject (an empty one is fine). Use the component menu 'Create Movable Light Object' to make one.")]
    [SerializeField] private Transform lightObject;
    [Tooltip("Orbit mode: distance from the sensor axis.")]
    [SerializeField, Min(0.1f)] private float orbitRadiusMM = 12.0f;
    [Tooltip("Orbit mode: height above the paper.")]
    [SerializeField] private float orbitHeightMM = 8.0f;
    [Tooltip("Orbit mode: degrees per second. Negative = the other way.")]
    [SerializeField] private float orbitSpeedDegPerSec = 30.0f;
    [Tooltip("Spot: diameter of the lamp lens. Point Ball: diameter of the ball. Larger = softer shadows and broader highlights; the brightness on the parts stays the same. (Directional: see Directional Softness.)")]
    [SerializeField, Range(0.05f, 10.0f)] private float lightSizeMM = 1.0f;
    [Tooltip("Brightness. Spot and Point Ball: light falls off with distance squared, as a real lamp (8 = about as bright as the studio key light at 14-20 mm). Directional: same brightness everywhere, equal to a spot of the same value 14 mm away.")]
    [SerializeField, Min(0.0f)] private float movableLightIntensity = 8.0f;
    [Tooltip("Color temperature: 2700 = warm bulb, 5600 = daylight, 9000 = cool blue.")]
    [SerializeField, Range(1500.0f, 12000.0f)] private float lightKelvin = 5600.0f;
    [Tooltip("ON: the camera sees the lamp (the glowing ball, or the spot lens in its dark rim, bright only from inside the cone). OFF: only its light.")]
    [SerializeField] private bool showLightBall = true;
    [Tooltip("Position mode, in Play mode: arrow keys move the light along X / Z, Page Up / Page Down move it up / down.")]
    [SerializeField] private bool moveLightWithKeys = true;
    [Tooltip("Speed of the arrow-key movement.")]
    [SerializeField, Min(0.1f)] private float lightKeySpeedMMPerSec = 10.0f;

    [Tooltip("33_0 result: camera rays added to each pixel since the view last changed.")]
    [SerializeField] private int samplesAccumulated;   // calculated - edits are ignored

    // 30_0 (stage 7): how light behaves inside the paper
    [Header("Surface Light (30_0, stage 7)")]
    [Tooltip("ON: light that enters the paper comes back out a little to the side, as in real paper. OFF: as in 29_0.")]
    [SerializeField] private bool useSpread = true;
    [Tooltip("Mean distance light travels inside the paper before coming back out (exponential). 0.07 mm = plain paper: Arney et al., J. Imaging Sci. Technol. 43(4) 1999, Table I, kp = 0.23-0.29 mm (wavelength independent), converted with d = kp x sqrt(3) / (2 pi). Paper value: set your own for other surfaces.")]
    [SerializeField, Range(0.0f, 0.5f)] private float spreadDistanceMM = 0.07f;
    [Tooltip("30_0 result: spread distance in sensor pixels.")]
    [SerializeField] private float spreadInPixels;   // calculated - edits are ignored

    [Header("Stray Light (24_0, stage 6.3)")]
    [Tooltip("Matte reflectance of the black housing at 850 nm. 0.04 = carbon black, near-infrared reflectance 0.04 (LBNL Cool Colors pigment database, lamp black; paints with carbon black: 4-5 % total solar reflectance). Only true for carbon-black plastic: black DYES can be bright or clear at 850 nm.")]
    [SerializeField, Range(0.0f, 1.0f)] private float housingIrReflectance = 0.04f;
    [Tooltip("Housing bounces followed for stray light. 0 = off (housing is pure black, as in 23_0).")]
    [SerializeField, Range(0, 2)] private int strayBounces = 1;
    [Tooltip("25_0: trace the stray ray every N frames, keep the last value in between. 1 = every frame (full fidelity). 2-4 = faster; the stray haze changes slowly, so it looks the same.")]
    [SerializeField, Range(1, 8)] private int strayEveryNFrames = 1;
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
    [Tooltip("27_0: physics. OFF removes the marks from the surface for the sensor too. To only hide them in the main view, use Main View Objects > Show Marks.")]
    [SerializeField] private bool marksActive = true;
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
    private Vector4 _vigKeyA = new Vector4(-1, -1, -1, -1), _vigKeyB, _vigKeyC;   // 26_0: inputs of the last vignetting check
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
        // 26_0: this check costs about a million small steps (glass bend search for 3 x 64 x 64 lens points), and
        // UpdateLensDesign runs every frame. Since 23_0 that dropped the frame rate. Now it runs only when an input changes.
        Vector4 vigKeyA = new Vector4(a, lensDiameterMM, pinholeDiameterMM, sensorToSurfaceMM);
        Vector4 vigKeyB = new Vector4(floorUndersideHeightMM, floorTopHeightMM, glassBottomHeightMM, chipGlassIndex);
        Vector4 vigKeyC = new Vector4(halfX, halfZ, chipGlassToWaferMM, 0.0f);
        if (vigKeyA != _vigKeyA || vigKeyB != _vigKeyB || vigKeyC != _vigKeyC)
        {
            _vigKeyA = vigKeyA; _vigKeyB = vigKeyB; _vigKeyC = vigKeyC;
            lightAtHalfwayPct = PinholeLightPct(new Vector2(halfX * 0.5f, 0.0f), a);
            lightAtEdgePct = PinholeLightPct(new Vector2(halfX, 0.0f), a);
            lightAtCornerPct = PinholeLightPct(new Vector2(halfX, halfZ), a);
        }

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

        bool camMoved = transform.hasChanged;   // 35_0
        if (transform.hasChanged)
        {
            _currentSample = 0;
            transform.hasChanged = false;
        }

        // 36_0: movable light - keys, orbit or object. Any change restarts the picture (and counts as moving).
        UpdateMovableLightKeys();
        _moveLightMM = ComputeMovableLightMM();
        _moveLightAim = ComputeMovableLightAim(_moveLightMM);   // 37_0
        _onlyMovableLight = spotlightOnly && movableLight != MovableLightMode.Off && movableLightIntensity > 0.0f;
        bool lightMoved = (_moveLightMM - _lastMoveLightMM).sqrMagnitude > 1e-10f || movableLight != _lastLightMode
                          || (_moveLightAim - _lastMoveLightAim).sqrMagnitude > 1e-10f || lightType != _lastLightType;
        _lastMoveLightMM = _moveLightMM;
        _lastLightMode = movableLight;
        _lastMoveLightAim = _moveLightAim;
        _lastLightType = lightType;
        if (lightMoved) _currentSample = 0;

        // 35_0: fast preview while anything moves, full quality once everything has been still for Still Delay
        if (mouseMoved || camMoved || lightMoved) _lastMoveTime = Time.unscaledTime;
        bool previewNow = fastPreviewWhileMoving && (Time.unscaledTime - _lastMoveTime) < Mathf.Max(stillDelaySec, 0.0f);
        if (_previewNow && !previewNow) _currentSample = 0;   // back to full quality: start the clean picture fresh
        _previewNow = previewNow;
        if (Input.GetKeyDown(screenshotKey)) RequestScreenshot();

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
        hybridShader.SetVector("_ChipGlassCenter", new Vector4(chipXZ.x * MmToUnit, (glassBottomY + tGl * 0.5f) * MmToUnit, chipXZ.y * MmToUnit, 0.0f));
        // 25_0 silicon die (main view): chip footprint, from just above the die face up to the chip top
        float dieTopY = sensorY + Mathf.Max(chipHeightMM - chipGlassToWaferMM, 0.01f);
        float dieBottomY = sensorY + 0.002f;   // 29_0: drawing only - 2 x the hit tolerance above the sensor, so the die box never
                                               // ties with the sensor (flicker). The glass top still lands on the die (28_0).
        hybridShader.SetVector("_ChipDieCenter", new Vector4(chipXZ.x * MmToUnit, (dieBottomY + dieTopY) * 0.5f * MmToUnit, chipXZ.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_ChipDieHalf", new Vector4(Mathf.Max(chipBodyWidthMM, 0.1f) * 0.5f * MmToUnit, (dieTopY - dieBottomY) * 0.5f * MmToUnit, Mathf.Max(chipBodyDepthMM, 0.1f) * 0.5f * MmToUnit, 0.0f));
        hybridShader.SetVector("_ChipGlassHalf", new Vector4(Mathf.Max(chipBodyWidthMM, 0.1f) * 0.5f * MmToUnit, Mathf.Max(tGl * 0.5f, 0.0f) * MmToUnit, Mathf.Max(chipBodyDepthMM, 0.1f) * 0.5f * MmToUnit, 0.0f));
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
        hybridShader.SetFloat("_SensorExposure", sensorExposure);
        // 24_0 stray light
        hybridShader.SetFloat("_HousingReflectance", housingIrReflectance);
        hybridShader.SetInt("_StrayBounces", Mathf.Clamp(strayBounces, 0, 2));
        // 25_0 speed
        hybridShader.SetInt("_MainBounces", Mathf.Clamp(mainViewBounces, 1, 8));
        bool strayNow = _sensorSample <= 1 || (Time.frameCount % Mathf.Max(strayEveryNFrames, 1)) == 0;   // always on the first frame after a reset
        hybridShader.SetFloat("_StrayThisFrame", strayNow ? 1.0f : 0.0f);
        hybridShader.SetFloat("_LensGhosts", lensGhosts ? 1.0f : 0.0f);
        hybridShader.SetFloat("_NaturalFalloff", naturalFalloff ? 1.0f : 0.0f);
        hybridShader.SetInt("_SensorView", (int)sensorView);
        // 27_0: main view objects, one bit each (drawing only)
        bool[] show = { showSurface, showMarks, showGroundPlane, showLedBody, showLedGlow, showLens, showLensHolder,
                        showHousingFloor, showHousingWalls, showChipGlass, showSiliconDie, showSensorPlane, showSensorImage,
                        showSensorGrid, showArrowsUp, showArrowsDown, showAxes,
                        showCasePins, showCaseTop, realCaseLook,     // 31_0: bits 17, 18, 19
                        realLedLook };                               // 32_0: bit 20
        int mask = 0;
        for (int i = 0; i < show.Length; i++) if (show[i]) mask |= 1 << i;
        hybridShader.SetInt("_ShowMask", mask);
        // 31_0: sensor case look (drawing only)
        float caseTopY = housingBottomY + Mathf.Max(housingHeightMM, 0.1f);
        float pinExitY = Mathf.Max(caseTopY - Mathf.Max(pinExitBelowTopMM, 0.0f), housingBottomY + Mathf.Max(housingFloorMM, 0.005f));
        float pinPitch = housingHalfD * 2.0f * 0.22f;
        hybridShader.SetVector("_PinSize", new Vector4(Mathf.Min(Mathf.Max(pinWidthMM, 0.01f), pinPitch * 0.8f), Mathf.Max(pinThicknessMM, 0.005f), Mathf.Max(pinOutMM, 0.0f), Mathf.Max(pinRiseMM, 0.0f)) * MmToUnit);
        hybridShader.SetVector("_PinPlace", new Vector4(pinExitY * MmToUnit, pinPitch * MmToUnit, caseTopY * MmToUnit, 0.0f));
        hybridShader.SetFloat("_CaseBevel", Mathf.Max(caseEdgeRoundMM, 0.0f) * MmToUnit);
        hybridShader.SetFloat("_LedDieGlow", Mathf.Max(ledDieGlow, 0.0f));   // 32_0
        // 30_0 stage 7: light spread inside the paper
        hybridShader.SetFloat("_SpreadDist", (useSpread ? Mathf.Max(spreadDistanceMM, 0.0f) : 0.0f) * MmToUnit);
        spreadInPixels = (useSpread ? spreadDistanceMM : 0.0f) / Mathf.Max(sensorSize.x / 30.0f, 1e-4f);
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
        hybridShader.SetFloat("_MarksEnabled", marksActive ? 1.0f : 0.0f);   // 27_0: physics

        hybridShader.SetTexture(kernelSensor, "SensorImage", _sensorImage);
        // 22_0: one thread group per pixel, one thread per ray
        hybridShader.Dispatch(kernelSensor, _sensorImage.width, _sensorImage.height, 1);
        raysPerFrame = _sensorImage.width * _sensorImage.height * nSub * nSub + (strayBounces > 0 && strayNow ? _sensorImage.width * _sensorImage.height * Mathf.Max(nSub * nSub / 16, 1) : 0);   // 26_0: stray on 1 of 16 grid points

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

        // ---- 33_0 beauty renderer: pass 1 = path tracer into the accumulation buffer, pass 2 = tone curve into the picture ----
        int spp = _previewNow ? 1 : Mathf.Clamp(samplesPerFrame, 1, 16);                                  // 35_0
        int bounces = _previewNow ? Mathf.Min(previewBounces, pathBounces) : pathBounces;
        int stepP = _previewNow ? (int)previewResolution : 1;
        samplesAccumulated = _currentSample * spp;
        hybridShader.SetInt("_SamplesPerFrame", spp);
        hybridShader.SetInt("_PathBounces", Mathf.Clamp(bounces, 1, 12));
        hybridShader.SetInt("_PreviewStep", stepP);
        hybridShader.SetFloat("_PaperShadows", paperShadows ? 1.0f : 0.0f);
        hybridShader.SetFloat("_FireflyClamp", Mathf.Max(fireflyClamp, 0.1f));
        hybridShader.SetFloat("_UseEnvMap", environmentMap != null ? 1.0f : 0.0f);
        hybridShader.SetFloat("_EnvIntensity", _onlyMovableLight ? 0.0f : Mathf.Max(environmentIntensity, 0.0f));   // 37_0: Spotlight Only
        hybridShader.SetFloat("_EnvRotation", environmentRotationDeg * Mathf.Deg2Rad);
        hybridShader.SetTexture(kernelMain, "_EnvMap", environmentMap != null ? (Texture)environmentMap : Texture2D.blackTexture);
        hybridShader.SetVector("_BackdropColor", ColorToVector(backdropColor.linear));
        hybridShader.SetVector("_FloorColor", ColorToVector(floorColor.linear));
        hybridShader.SetFloat("_BackgroundMode", (float)(int)background);
        hybridShader.SetFloat("_LedViewStrength", Mathf.Max(ledViewStrength, 0.0f));
        hybridShader.SetVector("_Rough", new Vector4(caseRoughness, goldRoughness, dieRoughness, epoxyRoughness));

        // 36_0: movable light. Ball radiance = intensity / (pi r^2), so the light on the parts depends on intensity and
        // distance only (intensity / distance^2 in scene units), not on the ball size.
        bool moveLightOn = movableLight != MovableLightMode.Off && movableLightIntensity > 0.0f;
        float lightR = Mathf.Max(lightSizeMM, 0.05f) * 0.5f * MmToUnit;
        Vector3 lightKel = KelvinToLinearRgb(lightKelvin);
        Vector3 lightCol;
        int lightTypeId;   // shader: 0 = ball, 1 = spot, 2 = directional
        if (lightType == MovableLightType.Spot) { lightCol = lightKel * movableLightIntensity; lightTypeId = 1; }                    // 37_0: intensity x cone / distance^2
        else if (lightType == MovableLightType.Directional) { lightCol = lightKel * (movableLightIntensity * 0.5f); lightTypeId = 2; }   // = a spot 14 mm (1.41 units) away
        else { lightCol = lightKel * (movableLightIntensity / (Mathf.PI * lightR * lightR)); lightTypeId = 0; }
        float halfOuter = Mathf.Clamp(spotAngleDeg, 1.0f, 170.0f) * 0.5f * Mathf.Deg2Rad;
        float cosOuter = Mathf.Cos(halfOuter);
        float cosInner = Mathf.Max(Mathf.Cos(halfOuter * (1.0f - Mathf.Clamp01(spotSoftEdge))), cosOuter + 1e-5f);
        float cosSun = Mathf.Cos(Mathf.Clamp(directionalSoftnessDeg, 0.01f, 30.0f) * 0.5f * Mathf.Deg2Rad);
        hybridShader.SetInt("_MoveLightType", lightTypeId);
        hybridShader.SetVector("_SpotDir", new Vector4(_moveLightAim.x, _moveLightAim.y, _moveLightAim.z, cosOuter));
        hybridShader.SetVector("_SpotParams", new Vector4(cosInner, cosSun, 0.0f, 0.0f));
        hybridShader.SetFloat("_MoveLightOn", moveLightOn ? 1.0f : 0.0f);
        hybridShader.SetVector("_MoveLightPos", new Vector4(_moveLightMM.x * MmToUnit, _moveLightMM.y * MmToUnit, _moveLightMM.z * MmToUnit, lightR));
        hybridShader.SetVector("_MoveLightColor", new Vector4(lightCol.x, lightCol.y, lightCol.z, 0.0f));
        hybridShader.SetFloat("_MoveLightVisible", showLightBall ? 1.0f : 0.0f);

        // 34_0 phase 5: geometry, marking text, board
        hybridShader.SetFloat("_RealGeo34", realGeometry ? 1.0f : 0.0f);
        hybridShader.SetFloat("_LedRound34", Mathf.Max(ledEdgeRoundMM, 0.0f) * MmToUnit);
        hybridShader.SetFloat("_PinRound34", Mathf.Clamp01(pinRoundness));
        EnsureMarkTexture();
        hybridShader.SetTexture(kernelMain, "_CaseMarkTex", _caseMarkTex);
        hybridShader.SetFloat("_MarkTexOn", realMarkingText ? 1.0f : 0.0f);
        hybridShader.SetFloat("_MarkAspect", (float)_caseMarkTex.height / _caseMarkTex.width);
        float pcbBottomY = caseTopY + Mathf.Max(pcbGapMM, 0.0f);
        float pcbT = Mathf.Max(pcbThicknessMM, 0.05f);
        hybridShader.SetFloat("_ShowPcb34", showPcb ? 1.0f : 0.0f);
        hybridShader.SetVector("_PcbCenter", new Vector4(chipXZ.x, pcbBottomY + pcbT * 0.5f, chipXZ.y, 0.0f) * MmToUnit);
        hybridShader.SetVector("_PcbHalf", new Vector4(Mathf.Max(pcbSizeMM.x, 0.5f) * 0.5f, pcbT * 0.5f, Mathf.Max(pcbSizeMM.y, 0.5f) * 0.5f, 0.0f) * MmToUnit);
        // 34_0 phase 6: camera lens
        hybridShader.SetFloat("_ApertureRadius", depthOfField ? Mathf.Max(apertureDiameterMM, 0.0f) * 0.5f * MmToUnit : 0.0f);
        hybridShader.SetFloat("_FocusDist", Mathf.Max(focusDistanceMM, 0.01f) * MmToUnit);
        hybridShader.SetFloat("_AutoFocus", autoFocus ? 1.0f : 0.0f);

        // studio lights: squares facing the sensor area (fixed in the scene, they do not move with the mouse)
        Vector3 studioTarget = new Vector3(mouseStartXZ.x, surfaceRefY + 1.0f, mouseStartXZ.y);
        StudioLight[] lights = { keyLight, fillLight, rimLight };
        int nLights = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            StudioLight sl = lights[i];
            if (!studioLights || _onlyMovableLight || sl == null || !sl.on || sl.intensity <= 0.0f) continue;   // 37_0: Spotlight Only
            float az = sl.azimuthDeg * Mathf.Deg2Rad;
            float el = Mathf.Clamp(sl.elevationDeg, 5.0f, 89.0f) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Cos(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Sin(az));
            Vector3 pos = (studioTarget + dir * Mathf.Max(sl.distanceMM, 1.0f)) * MmToUnit;
            Vector3 n = -dir;
            Vector3 u = Vector3.Cross(n, Vector3.up).normalized;
            Vector3 v = Vector3.Cross(n, u).normalized;
            float half = Mathf.Max(sl.sizeMM, 0.1f) * 0.5f * MmToUnit;
            Vector3 col = KelvinToLinearRgb(sl.kelvin) * sl.intensity;
            _studioPos[nLights] = new Vector4(pos.x, pos.y, pos.z, 0.0f);
            _studioN[nLights] = new Vector4(n.x, n.y, n.z, 0.0f);
            _studioU[nLights] = new Vector4(u.x, u.y, u.z, half);
            _studioV[nLights] = new Vector4(v.x, v.y, v.z, half);
            _studioColor[nLights] = new Vector4(col.x, col.y, col.z, 0.0f);
            nLights++;
        }
        hybridShader.SetInt("_StudioCount", nLights);
        hybridShader.SetVectorArray("_StudioPos", _studioPos);
        hybridShader.SetVectorArray("_StudioN", _studioN);
        hybridShader.SetVectorArray("_StudioU", _studioU);
        hybridShader.SetVectorArray("_StudioV", _studioV);
        hybridShader.SetVectorArray("_StudioColor", _studioColor);

        hybridShader.SetTexture(kernelMain, "SensorImage", _sensorImage);
        hybridShader.SetTexture(kernelMain, "AccumulationBuffer", _accumulationBuffer);

        hybridShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        hybridShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);

        int threadGroupsX = Mathf.CeilToInt(width / 8f);    // 33_0: 8 x 8 threads per group (was 32 x 32)
        int threadGroupsY = Mathf.CeilToInt(height / 8f);
        // 34_0: autofocus distance, found once per frame by a one-thread pass
        if (_focusBuf == null) { _focusBuf = new ComputeBuffer(1, sizeof(float)); _focusBuf.SetData(new float[] { focusDistanceMM * MmToUnit }); }
        if (depthOfField && autoFocus)
        {
            int kernelFocus = hybridShader.FindKernel("CSFocus");
            hybridShader.SetBuffer(kernelFocus, "_FocusBuf", _focusBuf);
            hybridShader.SetTexture(kernelFocus, "_PaperHeight", _surfaceTex);
            hybridShader.Dispatch(kernelFocus, 1, 1, 1);
        }
        hybridShader.SetBuffer(kernelMain, "_FocusBuf", _focusBuf);
        EnsureCleanBuffers(width, height);   // 35_0: guide pictures and denoiser buffers
        hybridShader.SetTexture(kernelMain, "GuideN", _guideN);
        hybridShader.SetTexture(kernelMain, "GuideA", _guideA);
        hybridShader.Dispatch(kernelMain, Mathf.CeilToInt(width / (8f * stepP)), Mathf.CeilToInt(height / (8f * stepP)), 1);

        // 35_0: denoiser passes (ping-pong between two buffers), step 1, 2, 4, 8, 16
        RenderTexture post = _accumulationBuffer;
        if (denoise)
        {
            int kernelDn = hybridShader.FindKernel("CSDenoise");
            int passes = Mathf.Clamp(denoisePasses, 1, 5);
            float sigma = Mathf.Max(denoiseStrength, 0.1f) * 2.0f / Mathf.Sqrt(Mathf.Max(samplesAccumulated, 1));
            hybridShader.SetFloat("_DnColorSigma", sigma);
            hybridShader.SetTexture(kernelDn, "_DnGuideN", _guideN);
            hybridShader.SetTexture(kernelDn, "_DnGuideA", _guideA);
            RenderTexture src = _accumulationBuffer;
            for (int p = 0; p < passes; p++)
            {
                RenderTexture dst = (p % 2 == 0) ? _dnA : _dnB;
                hybridShader.SetInt("_DnStep", 1 << p);
                hybridShader.SetInt("_DnFirst", p == 0 ? 1 : 0);
                hybridShader.SetInt("_DnLast", p == passes - 1 ? 1 : 0);
                hybridShader.SetTexture(kernelDn, "_DnSrc", src);
                hybridShader.SetTexture(kernelDn, "_DnDst", dst);
                hybridShader.Dispatch(kernelDn, threadGroupsX, threadGroupsY, 1);
                src = dst;
            }
            post = src;
        }

        // 34_0 phase 6: bloom (quarter size: bright parts, then blurred twice in each direction, narrow and wide)
        EnsureBloomBuffers(width, height);
        hybridShader.SetFloat("_Exposure", Mathf.Pow(2.0f, exposureEV));
        if (bloomStrength > 0.0f)
        {
            int kernelBloomPre = hybridShader.FindKernel("CSBloomPre");
            int kernelBloomBlur = hybridShader.FindKernel("CSBloomBlur");
            int bgx = Mathf.CeilToInt(_bloomA.width / 8f), bgy = Mathf.CeilToInt(_bloomA.height / 8f);
            hybridShader.SetFloat("_BloomThreshold", Mathf.Max(bloomThreshold, 0.0f));
            hybridShader.SetTexture(kernelBloomPre, "_PostSrc", post);
            hybridShader.SetTexture(kernelBloomPre, "_BloomDst", _bloomA);
            hybridShader.Dispatch(kernelBloomPre, bgx, bgy, 1);
            int[,] passes = { { 1, 0 }, { 0, 1 }, { 3, 0 }, { 0, 3 } };
            for (int p = 0; p < 4; p++)
            {
                RenderTexture src = (p % 2 == 0) ? _bloomA : _bloomB, dst = (p % 2 == 0) ? _bloomB : _bloomA;
                hybridShader.SetInts("_BlurDir", passes[p, 0], passes[p, 1]);
                hybridShader.SetTexture(kernelBloomBlur, "_BloomSrc", src);
                hybridShader.SetTexture(kernelBloomBlur, "_BloomDst", dst);
                hybridShader.Dispatch(kernelBloomBlur, bgx, bgy, 1);
            }
        }

        int kernelFinal = hybridShader.FindKernel("CSFinal");
        hybridShader.SetTexture(kernelFinal, "_BloomTex", _bloomA);
        hybridShader.SetFloat("_BloomStrength", Mathf.Max(bloomStrength, 0.0f));
        hybridShader.SetFloat("_Vignette", Mathf.Clamp01(vignette));
        hybridShader.SetFloat("_ChromAb", Mathf.Max(chromaticAberration, 0.0f));
        Vector3 wbRef = KelvinToLinearRgb(6500.0f), wbNow = KelvinToLinearRgb(whiteBalanceK);
        Vector3 wbGain = new Vector3(wbNow.x / wbRef.x, wbNow.y / wbRef.y, wbNow.z / wbRef.z);
        wbGain /= Mathf.Max(wbGain.y, 1e-4f);
        hybridShader.SetFloat("_Exposure", Mathf.Pow(2.0f, exposureEV));
        hybridShader.SetVector("_WhiteBalance", new Vector4(wbGain.x, wbGain.y, wbGain.z, 1.0f));
        hybridShader.SetInt("_ToneMapper", (int)toneMapper);
        hybridShader.SetTexture(kernelFinal, "_PostSrc", post);
        hybridShader.SetTexture(kernelFinal, "Result", _targetBuffer);
        hybridShader.Dispatch(kernelFinal, threadGroupsX, threadGroupsY, 1);

        Graphics.Blit(_targetBuffer, destination);

        // 35_0: save the screenshot once the picture is clean enough (never from a preview frame)
        if (_shotPending && !_previewNow && samplesAccumulated >= Mathf.Max(screenshotSamples, 1)) SaveScreenshot(post);
    }

    // 33_0: studio light data sent to the shader (3 slots)
    private readonly Vector4[] _studioPos = new Vector4[3], _studioN = new Vector4[3], _studioU = new Vector4[3],
                               _studioV = new Vector4[3], _studioColor = new Vector4[3];
    // 33_0: color of a glowing body at this temperature, linear RGB, brightest channel = 1 (fit by T. Helland)
    private static Vector3 KelvinToLinearRgb(float kelvin)
    {
        float t = Mathf.Clamp(kelvin, 1000.0f, 40000.0f) / 100.0f;
        float r, g, b;
        if (t <= 66.0f) { r = 255.0f; g = 99.4708025861f * Mathf.Log(t) - 161.1195681661f; }
        else { r = 329.698727446f * Mathf.Pow(t - 60.0f, -0.1332047592f); g = 288.1221695283f * Mathf.Pow(t - 60.0f, -0.0755148492f); }
        if (t >= 66.0f) b = 255.0f;
        else if (t <= 19.0f) b = 0.0f;
        else b = 138.5177312231f * Mathf.Log(t - 10.0f) - 305.0447927307f;
        Vector3 c = new Vector3(Mathf.Clamp01(r / 255.0f), Mathf.Clamp01(g / 255.0f), Mathf.Clamp01(b / 255.0f));
        c = new Vector3(Mathf.Pow(c.x, 2.2f), Mathf.Pow(c.y, 2.2f), Mathf.Pow(c.z, 2.2f));   // display values -> linear
        return c / Mathf.Max(Mathf.Max(c.x, c.y), Mathf.Max(c.z, 1e-4f));
    }

    // ---- 36_0 helpers: movable light ----
    private Vector3 _moveLightMM, _lastMoveLightMM;
    private MovableLightMode _lastLightMode;
    private Vector3 _moveLightAim = Vector3.down, _lastMoveLightAim = Vector3.down;   // 37_0
    private MovableLightType _lastLightType;
    private bool _onlyMovableLight;

    // 37_0: the aim point in scene mm
    private Vector3 AimPointMM()
    {
        return new Vector3(mouseStartXZ.x, patchCenter.y, mouseStartXZ.y) + aimPointMM;
    }

    // 37_0: the direction the spot / directional light points (unit, from the light toward the scene)
    private Vector3 ComputeMovableLightAim(Vector3 lightMM)
    {
        Vector3 a;
        if (spotAim == SpotAim.ObjectRotation && movableLight == MovableLightMode.FollowObject && lightObject != null) a = lightObject.forward;
        else if (spotAim == SpotAim.Angles)
        {
            float yaw = aimYawDeg * Mathf.Deg2Rad, pitch = aimPitchDeg * Mathf.Deg2Rad;
            a = new Vector3(Mathf.Cos(pitch) * Mathf.Sin(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Cos(yaw));
        }
        else a = AimPointMM() - lightMM;
        return a.sqrMagnitude > 1e-12f ? a.normalized : Vector3.down;
    }

    // light center in scene mm (the Inspector shows mm; the shader gets units)
    private Vector3 ComputeMovableLightMM()
    {
        Vector3 baseMM = new Vector3(mouseStartXZ.x, patchCenter.y, mouseStartXZ.y);   // paper spot under the sensor at the start
        switch (movableLight)
        {
            case MovableLightMode.FollowObject:
                if (lightObject != null) return lightObject.position / MmToUnit;
                return baseMM + lightPositionMM;
            case MovableLightMode.Orbit:
                float a = Time.time * orbitSpeedDegPerSec * Mathf.Deg2Rad;
                return baseMM + new Vector3(Mathf.Cos(a) * orbitRadiusMM, orbitHeightMM, Mathf.Sin(a) * orbitRadiusMM);
            default:
                return baseMM + lightPositionMM;
        }
    }

    private void UpdateMovableLightKeys()
    {
        if (!moveLightWithKeys || movableLight != MovableLightMode.Position || !Application.isPlaying) return;
        Vector3 d = Vector3.zero;
        if (Input.GetKey(KeyCode.RightArrow)) d.x += 1.0f;
        if (Input.GetKey(KeyCode.LeftArrow)) d.x -= 1.0f;
        if (Input.GetKey(KeyCode.UpArrow)) d.z += 1.0f;
        if (Input.GetKey(KeyCode.DownArrow)) d.z -= 1.0f;
        if (Input.GetKey(KeyCode.PageUp)) d.y += 1.0f;
        if (Input.GetKey(KeyCode.PageDown)) d.y -= 1.0f;
        if (d != Vector3.zero) lightPositionMM += d * (lightKeySpeedMMPerSec * Time.unscaledDeltaTime);
    }

    [ContextMenu("Create Movable Light Object")]
    private void CreateMovableLightObject()
    {
        var go = new GameObject("Movable Light (beauty)");
        Vector3 posMM = ComputeMovableLightMM();
        go.transform.position = posMM * MmToUnit;
        go.transform.rotation = Quaternion.LookRotation(ComputeMovableLightAim(posMM), Mathf.Abs(ComputeMovableLightAim(posMM).y) > 0.99f ? Vector3.forward : Vector3.up);   // 37_0: blue arrow = aim
        lightObject = go.transform;
        movableLight = MovableLightMode.FollowObject;
#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Create Movable Light Object");
        UnityEditor.Selection.activeGameObject = go;
#endif
    }

    // Scene view: yellow lamp, with its cone (spot), parallel rays (directional) or a line down to the paper (ball)
    private void OnDrawGizmos()
    {
        if (movableLight == MovableLightMode.Off) return;
        Vector3 pMM = Application.isPlaying ? _moveLightMM : ComputeMovableLightMM();
        Vector3 aim = Application.isPlaying ? _moveLightAim : ComputeMovableLightAim(pMM);
        Vector3 p = pMM * MmToUnit;
        Gizmos.color = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        Gizmos.DrawWireSphere(p, Mathf.Max(lightSizeMM, 0.05f) * 0.5f * MmToUnit);
        if (lightType == MovableLightType.PointBall)
        {
            Gizmos.DrawLine(p, new Vector3(p.x, patchCenter.y * MmToUnit, p.z));
            return;
        }
        float len = Mathf.Max(Vector3.Distance(pMM, AimPointMM()), 2.0f) * MmToUnit;
        Vector3 t = Vector3.Cross(aim, Mathf.Abs(aim.y) > 0.99f ? Vector3.forward : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(aim, t);
        if (lightType == MovableLightType.Directional)
        {
            float w = 0.3f * len;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    Gizmos.DrawLine(p + (t * i + b * j) * w, p + (t * i + b * j) * w + aim * len);
            return;
        }
        float rad = Mathf.Tan(Mathf.Clamp(spotAngleDeg, 1.0f, 170.0f) * 0.5f * Mathf.Deg2Rad) * len;
        Vector3 end = p + aim * len;
        Vector3 prev = end + t * rad;
        for (int k = 1; k <= 24; k++)
        {
            float a = k * Mathf.PI * 2.0f / 24.0f;
            Vector3 q = end + (t * Mathf.Cos(a) + b * Mathf.Sin(a)) * rad;
            Gizmos.DrawLine(prev, q);
            if (k % 6 == 0) Gizmos.DrawLine(p, q);
            prev = q;
        }
    }

    // ---- 35_0 helpers ----
    private RenderTexture _guideN, _guideA, _dnA, _dnB;
    private bool _previewNow;
    private float _lastMoveTime = -100.0f;
    private bool _shotPending;
    private string _shotMessage = "";
    private float _shotMessageTime = -100.0f;

    private static RenderTexture MakeRT(int w, int h, RenderTextureFormat f)
    {
        var rt = new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    private void EnsureCleanBuffers(int width, int height)
    {
        if (_guideN != null && _guideN.width == width && _guideN.height == height) return;
        if (_guideN != null) _guideN.Release();
        if (_guideA != null) _guideA.Release();
        if (_dnA != null) _dnA.Release();
        if (_dnB != null) _dnB.Release();
        _guideN = MakeRT(width, height, RenderTextureFormat.ARGBFloat);
        _guideA = MakeRT(width, height, RenderTextureFormat.ARGBHalf);
        _dnA = MakeRT(width, height, RenderTextureFormat.ARGBFloat);
        _dnB = MakeRT(width, height, RenderTextureFormat.ARGBFloat);
        _currentSample = 0;
    }

    [ContextMenu("Save Beauty Screenshot")]
    private void RequestScreenshot()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Save Beauty Screenshot works in Play mode."); return; }
        _shotPending = true;
        _shotMessage = "";
    }

    // Saves the shown picture as PNG (and the full-range picture as EXR) in <project>/Screenshots
    private void SaveScreenshot(RenderTexture hdr)
    {
        _shotPending = false;
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Screenshots"));
        System.IO.Directory.CreateDirectory(dir);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string png = System.IO.Path.Combine(dir, "Beauty_" + stamp + ".png");
        RenderTexture prev = RenderTexture.active;
        try
        {
            int w = _targetBuffer.width, h = _targetBuffer.height;
            RenderTexture.active = _targetBuffer;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
            Destroy(tex);
            if (saveExr)
            {
                RenderTexture.active = hdr;
                var texF = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                texF.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                texF.Apply(false);
                System.IO.File.WriteAllBytes(System.IO.Path.ChangeExtension(png, ".exr"), texF.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                Destroy(texF);
            }
            _shotMessage = "Saved " + png;
            Debug.Log("Beauty screenshot saved: " + png);
        }
        catch (System.Exception ex)
        {
            _shotMessage = "Screenshot failed: " + ex.Message;
            Debug.LogError(_shotMessage);
        }
        finally { RenderTexture.active = prev; }
        _shotMessageTime = Time.unscaledTime;
    }

    private void OnGUI()
    {
        if (!showStats) return;
        string line = (_previewNow ? "Preview (moving)" : "Beauty") + "   samples: " + samplesAccumulated;
        if (_shotPending) line += "   screenshot at " + screenshotSamples + " - keep the camera still";
        else if (_shotMessage.Length > 0 && Time.unscaledTime - _shotMessageTime < 6.0f) line += "   " + _shotMessage;
        GUI.color = Color.black;
        GUI.Label(new Rect(11, 11, 1400, 24), line);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, 1400, 24), line);
    }

    // ---- 34_0 helpers ----
    private RenderTexture _bloomA, _bloomB;
    private ComputeBuffer _focusBuf;
    private Texture2D _caseMarkTex;
    private string _markKey;

    private void EnsureBloomBuffers(int width, int height)
    {
        int bw = Mathf.Max(width / 4, 1), bh = Mathf.Max(height / 4, 1);
        if (_bloomA != null && _bloomA.width == bw && _bloomA.height == bh) return;
        if (_bloomA != null) _bloomA.Release();
        if (_bloomB != null) _bloomB.Release();
        _bloomA = new RenderTexture(bw, bh, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        _bloomB = new RenderTexture(bw, bh, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        _bloomA.Create();
        _bloomB.Create();
    }

    // 5 x 7 dot font, one byte per row (bit 4 = left column)
    private static readonly System.Collections.Generic.Dictionary<char, byte[]> Font5x7 = new System.Collections.Generic.Dictionary<char, byte[]>
    {
        {'0', new byte[]{0x0E,0x11,0x13,0x15,0x19,0x11,0x0E}}, {'1', new byte[]{0x04,0x0C,0x04,0x04,0x04,0x04,0x0E}},
        {'2', new byte[]{0x0E,0x11,0x01,0x02,0x04,0x08,0x1F}}, {'3', new byte[]{0x1F,0x02,0x04,0x02,0x01,0x11,0x0E}},
        {'4', new byte[]{0x02,0x06,0x0A,0x12,0x1F,0x02,0x02}}, {'5', new byte[]{0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E}},
        {'6', new byte[]{0x06,0x08,0x10,0x1E,0x11,0x11,0x0E}}, {'7', new byte[]{0x1F,0x01,0x02,0x04,0x08,0x08,0x08}},
        {'8', new byte[]{0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E}}, {'9', new byte[]{0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C}},
        {'A', new byte[]{0x0E,0x11,0x11,0x1F,0x11,0x11,0x11}}, {'B', new byte[]{0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E}},
        {'C', new byte[]{0x0E,0x11,0x10,0x10,0x10,0x11,0x0E}}, {'D', new byte[]{0x1C,0x12,0x11,0x11,0x11,0x12,0x1C}},
        {'E', new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F}}, {'F', new byte[]{0x1F,0x10,0x10,0x1E,0x10,0x10,0x10}},
        {'G', new byte[]{0x0E,0x11,0x10,0x17,0x11,0x11,0x0F}}, {'H', new byte[]{0x11,0x11,0x11,0x1F,0x11,0x11,0x11}},
        {'I', new byte[]{0x0E,0x04,0x04,0x04,0x04,0x04,0x0E}}, {'J', new byte[]{0x07,0x02,0x02,0x02,0x02,0x12,0x0C}},
        {'K', new byte[]{0x11,0x12,0x14,0x18,0x14,0x12,0x11}}, {'L', new byte[]{0x10,0x10,0x10,0x10,0x10,0x10,0x1F}},
        {'M', new byte[]{0x11,0x1B,0x15,0x15,0x11,0x11,0x11}}, {'N', new byte[]{0x11,0x11,0x19,0x15,0x13,0x11,0x11}},
        {'O', new byte[]{0x0E,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'P', new byte[]{0x1E,0x11,0x11,0x1E,0x10,0x10,0x10}},
        {'Q', new byte[]{0x0E,0x11,0x11,0x11,0x15,0x12,0x0D}}, {'R', new byte[]{0x1E,0x11,0x11,0x1E,0x14,0x12,0x11}},
        {'S', new byte[]{0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E}}, {'T', new byte[]{0x1F,0x04,0x04,0x04,0x04,0x04,0x04}},
        {'U', new byte[]{0x11,0x11,0x11,0x11,0x11,0x11,0x0E}}, {'V', new byte[]{0x11,0x11,0x11,0x11,0x11,0x0A,0x04}},
        {'W', new byte[]{0x11,0x11,0x11,0x15,0x15,0x15,0x0A}}, {'X', new byte[]{0x11,0x11,0x0A,0x04,0x0A,0x11,0x11}},
        {'Y', new byte[]{0x11,0x11,0x11,0x0A,0x04,0x04,0x04}}, {'Z', new byte[]{0x1F,0x01,0x02,0x04,0x08,0x10,0x1F}},
        {'-', new byte[]{0x00,0x00,0x00,0x1F,0x00,0x00,0x00}}, {'.', new byte[]{0x00,0x00,0x00,0x00,0x00,0x0C,0x0C}},
    };

    // 34_0: the two marking lines as a small picture (1 = ink). Rebuilt only when the text changes.
    private void EnsureMarkTexture()
    {
        string l1 = (markingLine1 ?? "").ToUpperInvariant(), l2 = (markingLine2 ?? "").ToUpperInvariant();
        string key = l1 + "\n" + l2;
        if (_caseMarkTex != null && key == _markKey) return;
        _markKey = key;
        const int S = 4;   // texels per font dot (smooth edges with bilinear filtering)
        int cols = Mathf.Max(Mathf.Max(l1.Length, l2.Length), 1);
        int wDots = 1 + cols * 6, hDots = 17;
        int w = wDots * S, h = hDots * S;
        if (_caseMarkTex == null || _caseMarkTex.width != w || _caseMarkTex.height != h)
        {
            if (_caseMarkTex != null) Destroy(_caseMarkTex);
            _caseMarkTex = new Texture2D(w, h, TextureFormat.R8, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }
        var px = new Color32[w * h];
        string[] lines = { l1, l2 };
        for (int li = 0; li < 2; li++)
            for (int ci = 0; ci < lines[li].Length; ci++)
            {
                if (!Font5x7.TryGetValue(lines[li][ci], out byte[] g)) continue;
                for (int r = 0; r < 7; r++)
                    for (int c = 0; c < 5; c++)
                    {
                        if (((g[r] >> (4 - c)) & 1) == 0) continue;
                        int dotX = 1 + ci * 6 + c, dotTop = 1 + li * 8 + r;
                        int y0 = (hDots - 1 - dotTop) * S, x0 = dotX * S;
                        for (int yy = 0; yy < S; yy++)
                            for (int xx = 0; xx < S; xx++)
                                px[(y0 + yy) * w + x0 + xx] = new Color32(255, 255, 255, 255);
                    }
            }
        _caseMarkTex.SetPixels32(px);
        _caseMarkTex.Apply(false);
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
        if (_bloomA != null) { _bloomA.Release(); _bloomA = null; }   // 34_0
        if (_focusBuf != null) { _focusBuf.Release(); _focusBuf = null; }
        if (_guideN != null) { _guideN.Release(); _guideN = null; }   // 35_0
        if (_guideA != null) { _guideA.Release(); _guideA = null; }
        if (_dnA != null) { _dnA.Release(); _dnA = null; }
        if (_dnB != null) { _dnB.Release(); _dnB = null; }
        if (_bloomB != null) { _bloomB.Release(); _bloomB = null; }
        if (_caseMarkTex != null) { Destroy(_caseMarkTex); _caseMarkTex = null; _markKey = null; }
        if (_targetBuffer != null) _targetBuffer.Release();
        if (_accumulationBuffer != null) _accumulationBuffer.Release();
        if (_sensorImage != null) _sensorImage.Release();
        if (_defaultSurface != null) { Destroy(_defaultSurface); _defaultSurface = null; }
        if (_markTex != null) { DestroyImmediate(_markTex); _markTex = null; }
        _marksDirty = true;
    }
}