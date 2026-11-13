using UnityEngine;

[RequireComponent(typeof(Camera))]
public class Main_SimulationSensor_12_0 : MonoBehaviour
{
    public enum MouseMotion { Static, Linear, Circular }

    [Header("Dependencies")]
    [SerializeField] private ComputeShader hybridShader;

    [Header("Light Source Settings (mm)")]
    [SerializeField] private Vector3 lightPosition = new Vector3(0.5f, 2.0f, 3.8f);
    [SerializeField] private float lightIntensity = 150000.0f;

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
    [Tooltip("Estimate: least blur over a ±0.1 mm surface range (similar chips), 850 nm LED.")]
    [SerializeField, Min(0.01f)] private float lensDiameterMM = 0.15f;
    [Tooltip("ON: Full optical lens refraction. OFF: Renders lens as a simple 3D shaded object with basic lighting/shadow response.")]
    [SerializeField] private bool enableLensVisuals = true;

    [Header("Lens Material & Shape (biconvex)")]
    [SerializeField, Range(1.2f, 2.0f)] private float refractiveIndex = 1.5f;
    [SerializeField, Min(0.005f)] private float lensEdgeThicknessMM = 0.05f;
    [SerializeField, Min(0.0f)] private float lensFlangeWidthMM = 0.15f;
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

    [Header("Sensor Plane (mm)")]
    [Tooltip("Estimate: 18 pixels x 30 um (PixArt patents, PAW3212 pixel count).")]
    [SerializeField] private Vector2 sensorSize = new Vector2(0.54f, 0.54f);
    [SerializeField] private Color sensorColor = new Color(0.05f, 0.35f, 0.45f, 1.0f);
    [SerializeField] private bool showSensorImage = true;
    [SerializeField, Range(1, 256)] private int sensorSamples = 32;
    [SerializeField] private float sensorExposure = 1.0f;
    [SerializeField, Range(0.0f, 1.0f)] private float sensorOpacity = 0.35f;

    [Header("Sensor Grid")]
    [SerializeField] private bool showGrid = true;
    [SerializeField] private Vector2Int gridCells = new Vector2Int(18, 18);
    [SerializeField, Range(0.0f, 0.5f)] private float gridLineWidth = 0.06f;
    [SerializeField] private Color gridLineColor = new Color(0.45f, 0.80f, 0.90f, 1.0f);
    [SerializeField, Range(0.0f, 0.5f)] private float borderWidth = 0.12f;
    [SerializeField] private Color borderColor = new Color(0.85f, 0.95f, 1.0f, 1.0f);

    [Header("Rough Surface Patch (mm)")]
    [SerializeField] private Vector3 patchCenter = new Vector3(0.0f, -1.0f, 0.0f);
    [SerializeField] private float patchSize = 10.0f;
    [SerializeField] private float patchAmplitude = 0.25f;
    [SerializeField] private float patchNoiseFrequency = 3.0f;
    [SerializeField] private Color patchLowColor = new Color(0.25f, 0.18f, 0.12f, 1.0f);
    [SerializeField] private Color patchHighColor = new Color(0.85f, 0.75f, 0.6f, 1.0f);

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

    private void Start()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnValidate()
    {
        _currentSample = 0;
        _sensorSample = 0;
        UpdateLensDesign();
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

        focalLengthMM = f;
        curvatureRadiusMM = R;
        centerThicknessMM = d;
        principalPlaneOffsetMM = h;
        lensCenterToSensorMM = _si + delta * 0.5f;
        gapMM = a - d * 0.5f;
        sensorToSurfaceMM = _so + delta + _si;
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

        if (!fits) lensStatus = "Lens Diameter too large for this distance: reduce Lens Diameter or increase a.";
        else if (gapMM <= 0.0f) lensStatus = "The lens touches the surface: increase a.";
        else if (!underHeightLimit) lensStatus = "Sharp 1:1, but over the height limit: reduce a.";
        else if (floorToChipAirMM < 0.0f) lensStatus = "The pinhole floor reaches into the chip: increase a or reduce Housing Floor.";
        else lensStatus = "OK - sharp 1:1 image, under the height limit.";
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        int width = source.width;
        int height = source.height;

        EnsureBuffersCreated(width, height);
        EnsureSensorImage();
        UpdateLensDesign();

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

        float surfaceRefY = patchCenter.y + Mathf.Max(patchAmplitude, 0.0f) * 0.875f;
        float sensorY = surfaceRefY + sensorToSurfaceMM;   // 11_0: = surface + 2a
        float lensY = sensorY - _si - _lensD * 0.5f + _lensH;
        float focusPlaneY = sensorY - _si - _lensDelta - _so;   // lands on the surface at 1:1
        float rearPlaneY = sensorY - _si;                    // lens point on the sensor side
        float frontPlaneY = sensorY - _si - _lensDelta;      // lens point on the surface side
        float sensorToLensCenter = sensorY - lensY;     
        float lensCenterToFocus = lensY - focusPlaneY;  

        // 12_0 housing: floor underside on the lens top, size around the chip, height up to the chip top.
        float housingBottomY = lensY + floorAboveLensCenterMM;
        float housingHalfW = housingOutsideSizeMM.x * 0.5f;
        float housingHalfD = housingOutsideSizeMM.y * 0.5f;
        float housingWall = Mathf.Min(Mathf.Max(housingWallMM, 0.01f), Mathf.Min(housingHalfW, housingHalfD) * 0.9f);

        int kernelMain = hybridShader.FindKernel("CSMain");
        int kernelSensor = hybridShader.FindKernel("CSSensor");

        // --- Uniform Bindings ---
        hybridShader.SetFloat("_Time", Time.time);
        hybridShader.SetInt("_SampleIndex", _currentSample);
        hybridShader.SetVector("_LightPosition", new Vector4(lightPosition.x * MmToUnit, lightPosition.y * MmToUnit, lightPosition.z * MmToUnit, 1.0f));
        hybridShader.SetFloat("_LightIntensity", lightIntensity);
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

        hybridShader.SetVector("_HousingCenter", new Vector4(mousePos.x * MmToUnit, housingBottomY * MmToUnit, mousePos.y * MmToUnit, 0.0f));
        hybridShader.SetVector("_HousingSize", new Vector4(housingHalfW * MmToUnit, Mathf.Max(housingHeightMM, 0.1f) * MmToUnit, housingHalfD * MmToUnit, 0.0f));
        hybridShader.SetFloat("_HousingWall", housingWall * MmToUnit);
        hybridShader.SetFloat("_HousingFloor", Mathf.Max(housingFloorMM, 0.005f) * MmToUnit);   // 12_0
        hybridShader.SetFloat("_PinholeRadius", Mathf.Max(pinholeDiameterMM, 0.01f) * 0.5f * MmToUnit);

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
        hybridShader.SetFloat("_LensFlange", lensFlangeWidthMM * MmToUnit);
        hybridShader.SetFloat("_UseRealLens", useRealLens ? 1.0f : 0.0f);
        hybridShader.SetFloat("_SensorOpacity", sensorOpacity);
        hybridShader.SetInt("_SensorSamples", sensorSamples);
        hybridShader.SetFloat("_SensorBlend", 1.0f / _sensorSample);
        hybridShader.SetFloat("_ShowSensorImage", showSensorImage ? 1.0f : 0.0f);
        hybridShader.SetFloat("_SensorExposure", sensorExposure);
        hybridShader.SetFloat("_ShowOptics", showOptics ? 1.0f : 0.0f);
        hybridShader.SetFloat("_EnableLensVisuals", enableLensVisuals ? 1.0f : 0.0f); // Pass toggle to compute shader

        hybridShader.SetVector("_PatchCenter", patchCenter * MmToUnit);
        hybridShader.SetFloat("_PatchHalfSize", Mathf.Max(patchSize, 0.001f) * 0.5f * MmToUnit);
        hybridShader.SetFloat("_PatchAmplitude", Mathf.Max(patchAmplitude, 0.0f) * MmToUnit);
        hybridShader.SetFloat("_PatchNoiseFrequency", patchNoiseFrequency / MmToUnit);
        hybridShader.SetVector("_PatchLowColor", ColorToVector(patchLowColor));
        hybridShader.SetVector("_PatchHighColor", ColorToVector(patchHighColor));

        hybridShader.SetTexture(kernelSensor, "SensorImage", _sensorImage);
        hybridShader.Dispatch(kernelSensor, Mathf.CeilToInt(_sensorImage.width / 8f), Mathf.CeilToInt(_sensorImage.height / 8f), 1);

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
    }
}