using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using realvirtual.MCP;

/// <summary>
/// Automated Conveyor & Sorting Cell Digital Twin
/// Built for realvirtual.io MCP ecosystem in Unity 6.
/// Features:
/// - Infeed conveyor with aluminum extruded framing, drive motor, and guide rails.
/// - Photoelectric optical detection gate & Machine Vision scanner with laser line indicator.
/// - Pneumatic sorting pusher actuator with realistic extension/retraction curves.
/// - Dual routing: Pass accumulation buffer and 90-degree reject divert chute with scrap tote.
/// - 3-Tier industrial Andon stack light (Red/Amber/Green).
/// - Comprehensive live SCADA telemetry HUD with clickable workpiece inspection.
/// - Real-time AI integration via exposed [McpTool] endpoints for realvirtual MCP.
/// </summary>
public class AutomatedSortingCellTwin : MonoBehaviour
{
    public static AutomatedSortingCellTwin Instance { get; private set; }

    // --- Configuration ---
    [Header("Conveyor Settings")]
    public float conveyorSpeed = 1.8f;
    public bool isRunning = true;
    public bool isEmergencyStopped = false;

    [Header("Pusher Settings")]
    public float pusherStrokeLength = 1.35f;
    public float pusherCycleDuration = 0.55f; // Extension + retraction total time

    [Header("Production & Quality")]
    public float spawnInterval = 2.4f;
    public float defectProbability = 0.25f; // 25% nominal defect rate

    // --- State & Telemetry ---
    public int totalSpawned = 0;
    public int passCount = 0;
    public int rejectCount = 0;
    public float defectRate => totalSpawned > 0 ? ((float)rejectCount / totalSpawned) * 100f : 0f;
    public float partsPerMinute => conveyorSpeed > 0 ? (60f / Mathf.Max(spawnInterval, 0.5f)) : 0f;

    public bool infeedSensorActive = false;
    public bool visionSensorActive = false;
    public bool pusherSensorActive = false;
    public bool isPusherExtending = false;
    public float pusherCurrentStroke = 0f; // 0 to 1

    private bool queueNextDefect = false;
    private string nextDefectType = "Surface";

    // --- Workpiece Tracking ---
    public class Workpiece
    {
        public GameObject gameObject;
        public string serialId;
        public bool isDefective;
        public string defectType;
        public float qualityScore; // 0.0 to 1.0
        public bool inspected;
        public bool rejected;
        public bool processed;
        public Vector3 velocity;
        public Renderer renderer;
    }

    private readonly List<Workpiece> activeWorkpieces = new List<Workpiece>();
    private Workpiece selectedWorkpiece = null;
    private float spawnTimer = 0f;
    private float pusherTimer = -1f;

    // --- Positions along X ---
    private const float InfeedStartX = -5.8f;
    private const float InfeedSensorX = -3.2f;
    private const float VisionScannerX = -0.6f;
    private const float PusherStationX = 1.8f;
    private const float RejectChuteZ = -1.8f;
    private const float PassEndX = 5.2f;

    // --- Visual Components ---
    private Camera mainCamera;
    private Transform cameraRig;
    private Vector3 orbitTarget = new Vector3(0.5f, 0.8f, -0.2f);
    private float camYaw = 35f;
    private float camPitch = 24f;
    private float camDistance = 11.5f;
    private int currentCameraPreset = 1;

    // Push rod transform for animation
    private Transform pusherRodTransform;
    private Transform pusherPaddleTransform;

    // Stack light materials
    private Material andonRedMat;
    private Material andonAmberMat;
    private Material andonGreenMat;
    private Light andonRedLight;
    private Light andonAmberLight;
    private Light andonGreenLight;

    // Laser beam visual
    private LineRenderer visionLaserLine;
    private Material sensorBeamMat;

    // Shared materials
    private Material floorMat;
    private Material frameMetalMat;
    private Material darkBeltMat;
    private Material guardRailYellowMat;
    private Material cylinderMat;
    private Material chromeMat;
    private Material goodPartMat;
    private Material defectivePartMat;
    private Material toteBinMat;

    // --- UI References ---
    private Text uiStatusText;
    private Text uiKpiText;
    private Text uiSensorText;
    private Text uiSelectedPartText;
    private Image uiAndonRedLed;
    private Image uiAndonAmberLed;
    private Image uiAndonGreenLed;
    private Slider uiPusherGauge;

    // --- Auto Bootstrap ---
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void AutoBootstrap()
    {
        if (Instance == null && FindFirstObjectByType<AutomatedSortingCellTwin>() == null)
        {
            GameObject cellObj = new GameObject("AutomatedSortingCellTwin");
            cellObj.AddComponent<AutomatedSortingCellTwin>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        InitializeMaterials();
        BuildCellGeometry();
        SetupCamera();
        SetupDashboardUI();
        UpdateDashboardUI();
    }

    private void Update()
    {
        HandleCameraControls();
        HandleWorkpieceRaycast();

        if (isRunning && !isEmergencyStopped)
        {
            // Spawning
            spawnTimer += Time.deltaTime;
            if (spawnTimer >= spawnInterval)
            {
                spawnTimer = 0f;
                SpawnWorkpiece();
            }

            // Transport & Sorting Logic
            UpdatePusherActuator();
            UpdateWorkpieces();
        }

        UpdateSensorVisuals();
        UpdateAndonStackLight();
        UpdateDashboardUI();
    }

    // =========================================================================
    // PHYSICAL CELL GEOMETRY GENERATION
    // =========================================================================

    private void InitializeMaterials()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        floorMat = CreateMaterial(litShader, new Color(0.14f, 0.16f, 0.19f), 0.1f, 0.3f);
        frameMetalMat = CreateMaterial(litShader, new Color(0.38f, 0.42f, 0.46f), 0.7f, 0.4f);
        darkBeltMat = CreateMaterial(litShader, new Color(0.08f, 0.09f, 0.10f), 0.05f, 0.7f);
        guardRailYellowMat = CreateMaterial(litShader, new Color(0.96f, 0.68f, 0.08f), 0.2f, 0.4f);
        cylinderMat = CreateMaterial(litShader, new Color(0.18f, 0.22f, 0.26f), 0.8f, 0.3f);
        chromeMat = CreateMaterial(litShader, new Color(0.85f, 0.88f, 0.92f), 0.95f, 0.1f);
        goodPartMat = CreateMaterial(litShader, new Color(0.12f, 0.52f, 0.92f), 0.75f, 0.35f);
        defectivePartMat = CreateMaterial(litShader, new Color(0.88f, 0.22f, 0.18f), 0.4f, 0.6f);
        toteBinMat = CreateMaterial(litShader, new Color(0.72f, 0.28f, 0.12f), 0.1f, 0.5f);

        // Stack light materials
        andonRedMat = CreateEmissiveMaterial(litShader, new Color(0.95f, 0.1f, 0.1f), new Color(0.2f, 0.02f, 0.02f));
        andonAmberMat = CreateEmissiveMaterial(litShader, new Color(0.98f, 0.65f, 0.05f), new Color(0.2f, 0.13f, 0.01f));
        andonGreenMat = CreateEmissiveMaterial(litShader, new Color(0.1f, 0.95f, 0.3f), new Color(0.02f, 0.2f, 0.05f));

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
        sensorBeamMat = new Material(unlitShader);
        sensorBeamMat.color = new Color(0f, 0.95f, 1f, 0.75f);
    }

    private Material CreateMaterial(Shader s, Color col, float metallic, float smoothness)
    {
        Material m = new Material(s);
        m.color = col;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    private Material CreateEmissiveMaterial(Shader s, Color emitColor, Color baseColor)
    {
        Material m = new Material(s);
        m.color = baseColor;
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        return m;
    }

    private void BuildCellGeometry()
    {
        GameObject root = new GameObject("Cell_Structures");
        root.transform.SetParent(transform);

        // 1. Foundation Base (Omitted to prevent Z-fighting with realvirtual factory floor)
        // Conveyor legs rest directly on the factory floor plane.

        // 2. Main Infeed Conveyor Line (-5.8 to +5.2 along X)
        float totalLength = 11.2f;
        CreateBox("Conveyor_MainBed", root.transform, new Vector3(0f, 0.58f, 0f), new Vector3(totalLength, 0.18f, 1.4f), frameMetalMat);
        CreateBox("Conveyor_MainBelt", root.transform, new Vector3(0f, 0.68f, 0f), new Vector3(totalLength - 0.1f, 0.04f, 1.1f), darkBeltMat);

        // Extruded Support Legs & Levelers
        for (int i = 0; i < 6; i++)
        {
            float legX = -5.0f + i * 2.0f;
            CreateConveyorLeg(root.transform, legX, 0f);
        }

        // Conveyor End Rollers & SEW Eurodrive Geared Motor
        CreateRoller(root.transform, InfeedStartX, 0.67f, 0f, 1.15f);
        CreateRoller(root.transform, PassEndX, 0.67f, 0f, 1.15f);

        GameObject driveMotor = CreateBox("SEW_DriveMotor", root.transform, new Vector3(PassEndX + 0.25f, 0.55f, 0.75f), new Vector3(0.55f, 0.45f, 0.45f), frameMetalMat);
        CreateBox("MotorTerminalBox", driveMotor.transform, new Vector3(0f, 0.26f, 0f), new Vector3(0.25f, 0.12f, 0.25f), cylinderMat);

        // Dual Safety Guard Rails
        CreateBox("GuardRail_Front", root.transform, new Vector3(0f, 0.84f, 0.62f), new Vector3(totalLength, 0.28f, 0.06f), guardRailYellowMat);
        // Rear guard rail has a cutout for the sorting pusher station between X=1.1 and X=2.5
        CreateBox("GuardRail_Rear_Infeed", root.transform, new Vector3(-2.0f, 0.84f, -0.62f), new Vector3(6.8f, 0.28f, 0.06f), guardRailYellowMat);
        CreateBox("GuardRail_Rear_Outfeed", root.transform, new Vector3(3.8f, 0.84f, -0.62f), new Vector3(totalLength - 8.4f, 0.28f, 0.06f), guardRailYellowMat);

        // 3. Photoelectric Infeed Optical Sensor (X = -3.2)
        CreatePhotoEyeSensor(root.transform, InfeedSensorX, "InfeedSensor");

        // 4. Quality & Machine Vision Inspection Gantry (X = -0.6)
        BuildVisionInspectionGantry(root.transform, VisionScannerX);

        // 5. Pneumatic Pusher Sorting Actuator Station (X = 1.8)
        BuildPneumaticPusherStation(root.transform, PusherStationX);

        // 6. Divert Chute & Reject Scrap Tote (Z = -1.8)
        BuildRejectChuteAndTote(root.transform, PusherStationX);

        // 7. Pass Outfeed Accumulation Table (X = 5.2)
        BuildPassAccumulationTable(root.transform, PassEndX);

        // 8. Industrial 3-Tier Andon Stack Light (X = -0.6, Z = 1.4)
        BuildAndonStackLight(root.transform, new Vector3(VisionScannerX, 0f, 1.35f));
    }

    private void CreateConveyorLeg(Transform parent, float x, float z)
    {
        GameObject legGroup = new GameObject($"LegPair_{x:F1}");
        legGroup.transform.SetParent(parent);

        // Left & right extruded uprights
        CreateBox("Leg_Front", legGroup.transform, new Vector3(x, 0.28f, z + 0.65f), new Vector3(0.1f, 0.58f, 0.1f), frameMetalMat);
        CreateBox("Leg_Rear", legGroup.transform, new Vector3(x, 0.28f, z - 0.65f), new Vector3(0.1f, 0.58f, 0.1f), frameMetalMat);
        // Cross brace
        CreateBox("CrossBrace", legGroup.transform, new Vector3(x, 0.22f, z), new Vector3(0.08f, 0.08f, 1.25f), frameMetalMat);
        // Leveling feet
        CreateCylinder("Foot_Front", legGroup.transform, new Vector3(x, 0.02f, z + 0.65f), new Vector3(0.16f, 0.04f, 0.16f), cylinderMat);
        CreateCylinder("Foot_Rear", legGroup.transform, new Vector3(x, 0.02f, z - 0.65f), new Vector3(0.16f, 0.04f, 0.16f), cylinderMat);
    }

    private void CreateRoller(Transform parent, float x, float y, float z, float width)
    {
        GameObject roller = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        roller.name = $"Roller_{x:F1}";
        roller.transform.SetParent(parent);
        roller.transform.position = new Vector3(x, y, z);
        roller.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        roller.transform.localScale = new Vector3(0.14f, width * 0.5f, 0.14f);
        roller.GetComponent<Renderer>().sharedMaterial = chromeMat;
    }

    private void CreatePhotoEyeSensor(Transform parent, float x, string name)
    {
        GameObject sensorGroup = new GameObject(name);
        sensorGroup.transform.SetParent(parent);

        // Emitter post (Front)
        CreateBox("EmitterPost", sensorGroup.transform, new Vector3(x, 0.88f, 0.72f), new Vector3(0.08f, 0.35f, 0.08f), frameMetalMat);
        GameObject emitterHead = CreateBox("EmitterHead", sensorGroup.transform, new Vector3(x, 0.95f, 0.64f), new Vector3(0.1f, 0.1f, 0.12f), cylinderMat);
        GameObject emitterLens = CreateCylinder("Lens", emitterHead.transform, new Vector3(0f, 0f, -0.06f), new Vector3(0.04f, 0.02f, 0.04f), andonAmberMat);
        emitterLens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Retro-reflector post (Rear)
        CreateBox("ReflectorPost", sensorGroup.transform, new Vector3(x, 0.88f, -0.72f), new Vector3(0.08f, 0.35f, 0.08f), frameMetalMat);
        CreateBox("PrismReflector", sensorGroup.transform, new Vector3(x, 0.95f, -0.64f), new Vector3(0.09f, 0.09f, 0.03f), andonAmberMat);
    }

    private void BuildVisionInspectionGantry(Transform parent, float x)
    {
        GameObject gantry = new GameObject("InspectionGantry");
        gantry.transform.SetParent(parent);

        // Overhead archway uprights & crossbar
        CreateBox("Arch_Front", gantry.transform, new Vector3(x, 1.25f, 0.85f), new Vector3(0.12f, 1.2f, 0.12f), frameMetalMat);
        CreateBox("Arch_Rear", gantry.transform, new Vector3(x, 1.25f, -0.85f), new Vector3(0.12f, 1.2f, 0.12f), frameMetalMat);
        CreateBox("Arch_Bridge", gantry.transform, new Vector3(x, 1.82f, 0f), new Vector3(0.14f, 0.12f, 1.85f), frameMetalMat);

        // Cognex/Keyence Machine Vision Camera Housing
        GameObject camHousing = CreateBox("VisionCamera", gantry.transform, new Vector3(x, 1.62f, 0f), new Vector3(0.24f, 0.28f, 0.24f), cylinderMat);
        GameObject lensBarrel = CreateCylinder("OpticsLens", camHousing.transform, new Vector3(0f, -0.16f, 0f), new Vector3(0.16f, 0.06f, 0.16f), chromeMat);
        GameObject ledRing = CreateCylinder("LEDRingIlluminator", camHousing.transform, new Vector3(0f, -0.18f, 0f), new Vector3(0.22f, 0.02f, 0.22f), andonGreenMat);

        // Projected laser sheet line visual across conveyor
        GameObject laserObj = new GameObject("VisionLaserSheet");
        laserObj.transform.SetParent(gantry.transform);
        visionLaserLine = laserObj.AddComponent<LineRenderer>();
        visionLaserLine.material = sensorBeamMat;
        visionLaserLine.startWidth = 0.025f;
        visionLaserLine.endWidth = 0.025f;
        visionLaserLine.positionCount = 2;
        visionLaserLine.SetPosition(0, new Vector3(x, 0.72f, -0.55f));
        visionLaserLine.SetPosition(1, new Vector3(x, 0.72f, 0.55f));
    }

    private void BuildPneumaticPusherStation(Transform parent, float x)
    {
        GameObject pusherGroup = new GameObject("PneumaticPusherStation");
        pusherGroup.transform.SetParent(parent);

        // Mounting Bracket on the front side (pushes towards -Z reject lane)
        CreateBox("MountingPedestal", pusherGroup.transform, new Vector3(x, 0.55f, 1.15f), new Vector3(0.4f, 0.6f, 0.35f), frameMetalMat);
        CreateBox("MountingPlate", pusherGroup.transform, new Vector3(x, 0.88f, 1.0f), new Vector3(0.35f, 0.18f, 0.15f), cylinderMat);

        // Pneumatic Cylinder Barrel (Festo style)
        GameObject cylinderBarrel = CreateBox("PneumaticCylinder", pusherGroup.transform, new Vector3(x, 0.88f, 1.55f), new Vector3(0.22f, 0.22f, 0.85f), cylinderMat);
        CreateCylinder("FrontEndCap", cylinderBarrel.transform, new Vector3(0f, 0f, -0.45f), new Vector3(0.2f, 0.06f, 0.2f), frameMetalMat);
        CreateCylinder("RearEndCap", cylinderBarrel.transform, new Vector3(0f, 0f, 0.45f), new Vector3(0.2f, 0.06f, 0.2f), frameMetalMat);

        // Piston Rod (Translates along Z)
        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = "PistonRod";
        rod.transform.SetParent(pusherGroup.transform);
        rod.transform.position = new Vector3(x, 0.88f, 0.95f);
        rod.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        rod.transform.localScale = new Vector3(0.07f, 0.45f, 0.07f);
        rod.GetComponent<Renderer>().sharedMaterial = chromeMat;
        pusherRodTransform = rod.transform;

        // Pusher Face Paddle
        GameObject paddle = CreateBox("PusherPaddle", pusherGroup.transform, new Vector3(x, 0.88f, 0.54f), new Vector3(0.48f, 0.24f, 0.06f), guardRailYellowMat);
        // Rubber contact bumper
        CreateBox("RubberFace", paddle.transform, new Vector3(0f, 0f, -0.04f), new Vector3(0.46f, 0.22f, 0.02f), darkBeltMat);
        pusherPaddleTransform = paddle.transform;
    }

    private void BuildRejectChuteAndTote(Transform parent, float x)
    {
        GameObject rejectGroup = new GameObject("RejectStation");
        rejectGroup.transform.SetParent(parent);

        // 45-degree diverter guide fence
        GameObject guideFence = CreateBox("DivertGuideFence", rejectGroup.transform, new Vector3(x + 0.35f, 0.86f, -0.35f), new Vector3(0.65f, 0.24f, 0.04f), guardRailYellowMat);
        guideFence.transform.rotation = Quaternion.Euler(0f, -38f, 0f);

        // Slanted Gravity Roller / Sheet Slide leading to Tote
        GameObject slide = CreateBox("RejectSlide", rejectGroup.transform, new Vector3(x, 0.52f, -1.15f), new Vector3(0.75f, 0.05f, 0.95f), frameMetalMat);
        slide.transform.rotation = Quaternion.Euler(-18f, 0f, 0f);

        // Slide side lips
        CreateBox("SlideLip_Left", slide.transform, new Vector3(-0.38f, 0.08f, 0f), new Vector3(0.04f, 0.14f, 0.95f), frameMetalMat);
        CreateBox("SlideLip_Right", slide.transform, new Vector3(0.38f, 0.08f, 0f), new Vector3(0.04f, 0.14f, 0.95f), frameMetalMat);

        // Industrial Defect / Scrap Tote Bin
        GameObject tote = new GameObject("ScrapToteBin");
        tote.transform.SetParent(rejectGroup.transform);
        tote.transform.position = new Vector3(x, 0.22f, RejectChuteZ);

        CreateBox("ToteBottom", tote.transform, Vector3.zero, new Vector3(0.9f, 0.05f, 0.9f), toteBinMat);
        CreateBox("ToteWall_F", tote.transform, new Vector3(0f, 0.25f, 0.43f), new Vector3(0.9f, 0.45f, 0.05f), toteBinMat);
        CreateBox("ToteWall_B", tote.transform, new Vector3(0f, 0.25f, -0.43f), new Vector3(0.9f, 0.45f, 0.05f), toteBinMat);
        CreateBox("ToteWall_L", tote.transform, new Vector3(-0.43f, 0.25f, 0f), new Vector3(0.05f, 0.45f, 0.86f), toteBinMat);
        CreateBox("ToteWall_R", tote.transform, new Vector3(0.43f, 0.25f, 0f), new Vector3(0.05f, 0.45f, 0.86f), toteBinMat);
    }

    private void BuildPassAccumulationTable(Transform parent, float x)
    {
        GameObject passGroup = new GameObject("PassAccumulationBuffer");
        passGroup.transform.SetParent(parent);

        // Roller collection table
        CreateBox("BufferTableBed", passGroup.transform, new Vector3(x + 1.1f, 0.58f, 0f), new Vector3(1.8f, 0.14f, 1.3f), frameMetalMat);
        CreateBox("BufferStopEndBumper", passGroup.transform, new Vector3(x + 2.0f, 0.82f, 0f), new Vector3(0.08f, 0.25f, 1.25f), guardRailYellowMat);

        // Buffer table legs
        CreateConveyorLeg(passGroup.transform, x + 1.0f, 0f);
        CreateConveyorLeg(passGroup.transform, x + 1.8f, 0f);
    }

    private void BuildAndonStackLight(Transform parent, Vector3 pos)
    {
        GameObject andon = new GameObject("AndonStackLight");
        andon.transform.SetParent(parent);
        andon.transform.position = pos;

        // Base & Upright Mast
        CreateCylinder("BaseFlange", andon.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.24f, 0.04f, 0.24f), cylinderMat);
        CreateCylinder("MastPole", andon.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.06f, 1.05f, 0.06f), frameMetalMat);
        CreateCylinder("LightBodyBase", andon.transform, new Vector3(0f, 2.15f, 0f), new Vector3(0.15f, 0.06f, 0.15f), cylinderMat);

        // Three Tier Lenses
        GameObject greenSeg = CreateCylinder("GreenTier", andon.transform, new Vector3(0f, 2.25f, 0f), new Vector3(0.14f, 0.08f, 0.14f), andonGreenMat);
        GameObject amberSeg = CreateCylinder("AmberTier", andon.transform, new Vector3(0f, 2.42f, 0f), new Vector3(0.14f, 0.08f, 0.14f), andonAmberMat);
        GameObject redSeg = CreateCylinder("RedTier", andon.transform, new Vector3(0f, 2.59f, 0f), new Vector3(0.14f, 0.08f, 0.14f), andonRedMat);
        CreateCylinder("Cap", andon.transform, new Vector3(0f, 2.70f, 0f), new Vector3(0.15f, 0.03f, 0.15f), cylinderMat);

        // Point lights for realistic scene illumination
        andonGreenLight = CreatePointLight(greenSeg.transform, new Color(0.1f, 1f, 0.3f), 1.8f);
        andonAmberLight = CreatePointLight(amberSeg.transform, new Color(1f, 0.7f, 0.1f), 1.8f);
        andonRedLight = CreatePointLight(redSeg.transform, new Color(1f, 0.15f, 0.15f), 2.2f);
    }

    private Light CreatePointLight(Transform parent, Color col, float intensity)
    {
        GameObject lo = new GameObject("BulbLight");
        lo.transform.SetParent(parent, false);
        Light l = lo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = col;
        l.intensity = intensity;
        l.range = 3.5f;
        l.enabled = false;
        return l;
    }

    private GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private GameObject CreateCylinder(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // =========================================================================
    // WORKPIECE SPAWNING & LIFECYCLE
    // =========================================================================

    public void SpawnWorkpiece(bool forceDefect = false, string specificDefect = "")
    {
        totalSpawned++;
        string serial = $"WP-60{totalSpawned:D3}";

        bool isDefect = forceDefect || queueNextDefect || (UnityEngine.Random.value < defectProbability);
        string defect = isDefect ? (string.IsNullOrEmpty(specificDefect) ? (queueNextDefect ? nextDefectType : "Surface Flaw") : specificDefect) : "None";
        queueNextDefect = false;

        float quality = isDefect ? UnityEngine.Random.Range(0.25f, 0.68f) : UnityEngine.Random.Range(0.92f, 0.99f);

        GameObject boxObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        boxObj.name = $"Workpiece_{serial}";
        boxObj.transform.SetParent(transform);

        // Size: machined billet 0.45 x 0.22 x 0.45
        float sizeY = isDefect && defect.Contains("Dimension") ? 0.32f : 0.22f;
        boxObj.transform.localScale = new Vector3(0.42f, sizeY, 0.42f);
        boxObj.transform.position = new Vector3(InfeedStartX, 0.70f + (sizeY * 0.5f), 0f);

        Renderer rend = boxObj.GetComponent<Renderer>();
        rend.material = new Material(isDefect ? defectivePartMat : goodPartMat);

        // Add a visual top marker / QR code plate
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "BarcodeTag";
        plate.transform.SetParent(boxObj.transform, false);
        plate.transform.localPosition = new Vector3(0f, 0.51f, 0f);
        plate.transform.localScale = new Vector3(0.6f, 0.04f, 0.6f);
        plate.GetComponent<Renderer>().sharedMaterial = chromeMat;

        Workpiece wp = new Workpiece
        {
            gameObject = boxObj,
            serialId = serial,
            isDefective = isDefect,
            defectType = defect,
            qualityScore = quality,
            inspected = false,
            rejected = false,
            processed = false,
            velocity = new Vector3(conveyorSpeed, 0f, 0f),
            renderer = rend
        };

        activeWorkpieces.Add(wp);
    }

    private void UpdateWorkpieces()
    {
        infeedSensorActive = false;
        visionSensorActive = false;
        pusherSensorActive = false;

        for (int i = activeWorkpieces.Count - 1; i >= 0; i--)
        {
            Workpiece wp = activeWorkpieces[i];
            if (wp.gameObject == null)
            {
                activeWorkpieces.RemoveAt(i);
                continue;
            }

            Vector3 pos = wp.gameObject.transform.position;

            // 1. Infeed Optical Sensor Detection
            if (Mathf.Abs(pos.x - InfeedSensorX) < 0.45f)
            {
                infeedSensorActive = true;
            }

            // 2. Machine Vision Camera Inspection Zone
            if (!wp.inspected && Mathf.Abs(pos.x - VisionScannerX) < 0.35f)
            {
                wp.inspected = true;
                visionSensorActive = true;

                // Visual flash on inspection
                if (wp.isDefective)
                {
                    wp.renderer.material.color = new Color(1f, 0.35f, 0.15f);
                }
            }
            else if (Mathf.Abs(pos.x - VisionScannerX) < 0.25f)
            {
                visionSensorActive = true;
            }

            // 3. Pusher Sorting Station Interaction
            if (wp.inspected && wp.isDefective && !wp.rejected)
            {
                // In sorting window
                if (pos.x >= PusherStationX - 0.35f && pos.x <= PusherStationX + 0.55f && pos.z > -0.6f)
                {
                    pusherSensorActive = true;

                    // Trigger pneumatic actuator if not already moving
                    if (!isPusherExtending && pusherTimer < 0f)
                    {
                        TriggerPusherActuation();
                    }
                }
            }

            // Physical movement
            if (wp.rejected)
            {
                // Sliding down the reject chute into the scrap tote
                pos += wp.velocity * Time.deltaTime;
                pos.y -= 0.65f * Time.deltaTime; // Gravity slide slope
                if (pos.y < 0.35f) pos.y = 0.35f;

                wp.gameObject.transform.position = pos;

                // Landed in scrap tote
                if (pos.z <= RejectChuteZ - 0.15f)
                {
                    if (!wp.processed)
                    {
                        wp.processed = true;
                        rejectCount++;
                    }

                    // Keep in tote for a moment, then cleanup
                    if (pos.x < PusherStationX + 0.1f)
                    {
                        wp.velocity = Vector3.zero;
                    }
                }
            }
            else
            {
                // Normal conveyor transport along +X
                pos.x += conveyorSpeed * Time.deltaTime;
                wp.gameObject.transform.position = pos;

                // Passed outfeed collection buffer
                if (pos.x >= PassEndX + 1.8f)
                {
                    if (!wp.processed)
                    {
                        wp.processed = true;
                        passCount++;
                    }

                    if (selectedWorkpiece == wp) selectedWorkpiece = null;
                    Destroy(wp.gameObject);
                    activeWorkpieces.RemoveAt(i);
                    continue;
                }
            }

            // Cleanup older rejected parts if too many in tote
            if (wp.processed && wp.rejected && activeWorkpieces.Count > 18)
            {
                if (selectedWorkpiece == wp) selectedWorkpiece = null;
                Destroy(wp.gameObject);
                activeWorkpieces.RemoveAt(i);
            }
        }
    }

    // =========================================================================
    // PNEUMATIC ACTUATOR CONTROL & MOTION
    // =========================================================================

    public void TriggerPusherActuation()
    {
        if (pusherTimer < 0f)
        {
            pusherTimer = 0f;
            isPusherExtending = true;
        }
    }

    private void UpdatePusherActuator()
    {
        if (pusherTimer >= 0f)
        {
            pusherTimer += Time.deltaTime;
            float halfCycle = pusherCycleDuration * 0.5f;

            if (pusherTimer <= halfCycle)
            {
                // Smooth extension stroke: 0 -> 1
                float t = pusherTimer / halfCycle;
                pusherCurrentStroke = Mathf.SmoothStep(0f, 1f, t);
                isPusherExtending = true;
            }
            else if (pusherTimer <= pusherCycleDuration)
            {
                // Rapid retraction stroke: 1 -> 0
                float t = (pusherTimer - halfCycle) / halfCycle;
                pusherCurrentStroke = Mathf.SmoothStep(1f, 0f, t);
                isPusherExtending = false;
            }
            else
            {
                // Cycle complete
                pusherTimer = -1f;
                pusherCurrentStroke = 0f;
                isPusherExtending = false;
            }

            // Update physical rod & paddle positions
            float currentZOffset = -pusherCurrentStroke * pusherStrokeLength;
            if (pusherRodTransform != null)
            {
                pusherRodTransform.localPosition = new Vector3(PusherStationX, 0.88f, 0.95f + (currentZOffset * 0.5f));
            }
            if (pusherPaddleTransform != null)
            {
                pusherPaddleTransform.localPosition = new Vector3(PusherStationX, 0.88f, 0.54f + currentZOffset);
            }

            // Check contact with workpieces in the pusher line
            float paddleZ = 0.54f + currentZOffset;
            for (int i = 0; i < activeWorkpieces.Count; i++)
            {
                Workpiece wp = activeWorkpieces[i];
                if (wp.gameObject == null || wp.rejected) continue;

                Vector3 wpPos = wp.gameObject.transform.position;
                if (Mathf.Abs(wpPos.x - PusherStationX) < 0.45f)
                {
                    if (wpPos.z >= paddleZ - 0.28f && wpPos.z <= paddleZ + 0.15f)
                    {
                        // Pusher contact! Divert workpiece into reject chute
                        wp.rejected = true;
                        wp.velocity = new Vector3(conveyorSpeed * 0.35f, 0f, -conveyorSpeed * 1.6f);
                    }
                }
            }
        }
    }

    // =========================================================================
    // SENSORS & ANDON LIGHTS
    // =========================================================================

    private void UpdateSensorVisuals()
    {
        if (visionLaserLine != null)
        {
            Color laserCol = visionSensorActive ? new Color(0f, 1f, 0.4f, 0.95f) : new Color(0f, 0.85f, 1f, 0.45f);
            visionLaserLine.startColor = laserCol;
            visionLaserLine.endColor = laserCol;
        }
    }

    private void UpdateAndonStackLight()
    {
        bool redOn = isEmergencyStopped;
        bool amberOn = !isEmergencyStopped && (pusherTimer >= 0f || visionSensorActive);
        bool greenOn = !isEmergencyStopped && isRunning && !amberOn;

        SetLightSegment(andonRedMat, andonRedLight, redOn, new Color(1f, 0.1f, 0.1f));
        SetLightSegment(andonAmberMat, andonAmberLight, amberOn, new Color(1f, 0.7f, 0.05f));
        SetLightSegment(andonGreenMat, andonGreenLight, greenOn, new Color(0.1f, 1f, 0.3f));

        if (uiAndonRedLed != null) uiAndonRedLed.color = redOn ? new Color(1f, 0.2f, 0.2f) : new Color(0.3f, 0.1f, 0.1f);
        if (uiAndonAmberLed != null) uiAndonAmberLed.color = amberOn ? new Color(1f, 0.8f, 0.1f) : new Color(0.3f, 0.25f, 0.05f);
        if (uiAndonGreenLed != null) uiAndonGreenLed.color = greenOn ? new Color(0.2f, 1f, 0.4f) : new Color(0.08f, 0.3f, 0.12f);
    }

    private void SetLightSegment(Material mat, Light light, bool active, Color col)
    {
        if (mat != null)
        {
            if (active)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", col * 1.5f);
                mat.color = col;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.color = col * 0.25f;
            }
        }
        if (light != null) light.enabled = active;
    }

    // =========================================================================
    // USER CONTROLS, CAMERA PRESETS & WORKPIECE INSPECTION
    // =========================================================================

    private void SetupCamera()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject camObj = new GameObject("DigitalTwin_Camera");
            mainCamera = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
            cameraRig = mainCamera.transform;
            ApplyCameraPreset(1);
        }
        else
        {
            cameraRig = mainCamera.transform;
            // Respect the user's current camera position saved in the scene.
        }
    }

    public void ApplyCameraPreset(int preset)
    {
        currentCameraPreset = preset;
        switch (preset)
        {
            case 1: // Overview Orbit
                orbitTarget = new Vector3(0.5f, 0.8f, -0.2f);
                camYaw = 35f;
                camPitch = 24f;
                camDistance = 11.5f;
                break;
            case 2: // Vision Inspection Close-up
                orbitTarget = new Vector3(VisionScannerX, 1.1f, 0f);
                camYaw = 55f;
                camPitch = 18f;
                camDistance = 4.2f;
                break;
            case 3: // Pusher Station Close-up
                orbitTarget = new Vector3(PusherStationX, 0.9f, 0f);
                camYaw = 115f;
                camPitch = 22f;
                camDistance = 4.8f;
                break;
            case 4: // Top-Down Chute View
                orbitTarget = new Vector3(1.0f, 0.5f, -0.8f);
                camYaw = 0f;
                camPitch = 68f;
                camDistance = 8.5f;
                break;
        }

        Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
        cameraRig.position = orbitTarget - rot * Vector3.forward * camDistance;
        cameraRig.rotation = rot;
    }

    private void HandleCameraControls()
    {
        // If realvirtual's own SceneMouseNavigation is active on the camera, yield control to prevent jitter/fighting
        if (mainCamera != null && mainCamera.GetComponent("SceneMouseNavigation") != null)
            return;

        bool hasInput = false;
        if (Input.GetMouseButton(1)) // Right click drag orbit
        {
            camYaw += Input.GetAxis("Mouse X") * 3.5f;
            camPitch -= Input.GetAxis("Mouse Y") * 2.2f;
            camPitch = Mathf.Clamp(camPitch, 10f, 75f);
            hasInput = true;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            camDistance -= scroll * 0.8f;
            camDistance = Mathf.Clamp(camDistance, 3.5f, 22f);
            hasInput = true;
        }

        if (hasInput && cameraRig != null)
        {
            Quaternion rot = Quaternion.Euler(camPitch, camYaw, 0f);
            cameraRig.position = orbitTarget - rot * Vector3.forward * camDistance;
            cameraRig.rotation = rot;
        }
    }

    private void HandleWorkpieceRaycast()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                foreach (var wp in activeWorkpieces)
                {
                    if (wp.gameObject == hit.collider.gameObject || hit.collider.transform.IsChildOf(wp.gameObject.transform))
                    {
                        selectedWorkpiece = wp;
                        return;
                    }
                }
            }
        }
    }

    // =========================================================================
    // SCADA DASHBOARD UI OVERLAY
    // =========================================================================

    private void SetupDashboardUI()
    {
        GameObject canvasObj = new GameObject("TwinViewSCADA_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        if (EventSystem.current == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // Main SCADA Monitoring Glass Panel
        GameObject panel = CreateUIBox("SCADA_Panel", canvasObj.transform, new Vector2(28, -28), new Vector2(420, 680), new Color(0.06f, 0.08f, 0.11f, 0.94f));

        // Header Title
        CreateUILabel("HeaderTitle", panel.transform, "DIGITAL TWIN  /  SORTING CELL", 20, -22, 380, 32, 20, true, Color.white);
        CreateUILabel("HeaderSub", panel.transform, "REALVIRTUAL.IO MCP INTEGRATION  |  LINE 01", 20, -54, 380, 20, 12, false, new Color(0.2f, 0.85f, 1f));

        // Andon Light Mini Indicator in UI
        GameObject andonGroup = CreateUIBox("AndonLEDs", panel.transform, new Vector2(300, -22), new Vector2(90, 30), new Color(0.12f, 0.15f, 0.18f));
        uiAndonRedLed = CreateUICircle("RedLED", andonGroup.transform, new Vector2(16, -15), 18, Color.red);
        uiAndonAmberLed = CreateUICircle("AmberLED", andonGroup.transform, new Vector2(45, -15), 18, Color.yellow);
        uiAndonGreenLed = CreateUICircle("GreenLED", andonGroup.transform, new Vector2(74, -15), 18, Color.green);

        // Status Banner
        uiStatusText = CreateUILabel("StatusText", panel.transform, "SYSTEM STATUS", 20, -90, 380, 34, 18, true, Color.green);

        // Production KPI Panel Section
        uiKpiText = CreateUILabel("KpiText", panel.transform, "KPIS LOADING...", 20, -135, 380, 140, 14, false, Color.white);

        // Actuator Gauge
        CreateUILabel("ActuatorLabel", panel.transform, "PNEUMATIC ACTUATOR STROKE", 20, -285, 380, 20, 12, true, new Color(0.6f, 0.7f, 0.8f));
        uiPusherGauge = CreateProgressBar("PusherGauge", panel.transform, new Vector2(20, -310), new Vector2(380, 14));

        // Sensor Live Signals
        uiSensorText = CreateUILabel("SensorSignals", panel.transform, "SENSORS", 20, -340, 380, 70, 13, false, new Color(0.8f, 0.9f, 1f));

        // Selected Workpiece Telemetry Card
        uiSelectedPartText = CreateUILabel("SelectedPart", panel.transform, "Click a part to view pedigree", 20, -425, 380, 85, 13, false, new Color(0.95f, 0.8f, 0.4f));

        // Control Buttons
        float btnY = -525;
        CreateUIButton("BtnRun", panel.transform, "RUN", new Vector2(20, btnY), new Vector2(85, 38), new Color(0.1f, 0.55f, 0.35f), () =>
        {
            isRunning = true;
            isEmergencyStopped = false;
        });

        CreateUIButton("BtnPause", panel.transform, "PAUSE", new Vector2(115, btnY), new Vector2(85, 38), new Color(0.45f, 0.35f, 0.15f), () =>
        {
            isRunning = false;
        });

        CreateUIButton("BtnDefect", panel.transform, "+ DEFECT", new Vector2(210, btnY), new Vector2(95, 38), new Color(0.65f, 0.25f, 0.15f), () =>
        {
            queueNextDefect = true;
            nextDefectType = "Surface Flaw";
        });

        CreateUIButton("BtnPush", panel.transform, "ACTUATE", new Vector2(315, btnY), new Vector2(85, 38), new Color(0.2f, 0.45f, 0.65f), () =>
        {
            TriggerPusherActuation();
        });

        // Second Row: Speed & Reset
        float btnY2 = -572;
        CreateUIButton("BtnSpeedDown", panel.transform, "SPEED -", new Vector2(20, btnY2), new Vector2(85, 34), new Color(0.2f, 0.25f, 0.3f), () =>
        {
            conveyorSpeed = Mathf.Max(conveyorSpeed - 0.4f, 0.4f);
        });

        CreateUIButton("BtnSpeedUp", panel.transform, "SPEED +", new Vector2(115, btnY2), new Vector2(85, 34), new Color(0.2f, 0.25f, 0.3f), () =>
        {
            conveyorSpeed = Mathf.Min(conveyorSpeed + 0.4f, 4.0f);
        });

        CreateUIButton("BtnReset", panel.transform, "RESET STATS", new Vector2(210, btnY2), new Vector2(95, 34), new Color(0.25f, 0.3f, 0.35f), () =>
        {
            totalSpawned = 0;
            passCount = 0;
            rejectCount = 0;
        });

        CreateUIButton("BtnEStop", panel.transform, "E-STOP", new Vector2(315, btnY2), new Vector2(85, 34), new Color(0.75f, 0.12f, 0.12f), () =>
        {
            isEmergencyStopped = !isEmergencyStopped;
            if (isEmergencyStopped) isRunning = false;
        });

        // Third Row: Camera Views
        float btnY3 = -618;
        CreateUIButton("Cam1", panel.transform, "CAM 1: ORBIT", new Vector2(20, btnY3), new Vector2(90, 32), new Color(0.15f, 0.2f, 0.25f), () => ApplyCameraPreset(1));
        CreateUIButton("Cam2", panel.transform, "CAM 2: VISION", new Vector2(115, btnY3), new Vector2(90, 32), new Color(0.15f, 0.2f, 0.25f), () => ApplyCameraPreset(2));
        CreateUIButton("Cam3", panel.transform, "CAM 3: PUSHER", new Vector2(210, btnY3), new Vector2(90, 32), new Color(0.15f, 0.2f, 0.25f), () => ApplyCameraPreset(3));
        CreateUIButton("Cam4", panel.transform, "CAM 4: CHUTE", new Vector2(305, btnY3), new Vector2(95, 32), new Color(0.15f, 0.2f, 0.25f), () => ApplyCameraPreset(4));
    }

    private void UpdateDashboardUI()
    {
        if (uiStatusText == null) return;

        if (isEmergencyStopped)
        {
            uiStatusText.text = "SYSTEM  /  EMERGENCY STOPPED";
            uiStatusText.color = new Color(1f, 0.25f, 0.25f);
        }
        else if (!isRunning)
        {
            uiStatusText.text = "SYSTEM  /  PAUSED";
            uiStatusText.color = new Color(1f, 0.85f, 0.3f);
        }
        else
        {
            uiStatusText.text = "SYSTEM  /  ACTIVE (AUTO-SORTING)";
            uiStatusText.color = new Color(0.25f, 1f, 0.5f);
        }

        uiKpiText.text =
            $"CONVEYOR SPEED        : {conveyorSpeed:F1} m/s\n" +
            $"THROUGHPUT RATE       : {partsPerMinute:F0} PPM\n" +
            $"TOTAL INFEED PARTS    : {totalSpawned}\n" +
            $"ACCEPTED (PASSED)     : {passCount}\n" +
            $"DEFECTS REJECTED      : {rejectCount}\n" +
            $"CURRENT DEFECT RATE   : {defectRate:F1} %";

        if (uiPusherGauge != null) uiPusherGauge.value = pusherCurrentStroke;

        uiSensorText.text =
            $"PHOTO-EYE SENSOR  : {(infeedSensorActive ? "<color=#00e5ff>TRIGGERED</color>" : "<color=#888888>IDLE</color>")}\n" +
            $"VISION INSPECTION : {(visionSensorActive ? "<color=#00ff66>SCANNING</color>" : "<color=#888888>READY</color>")}\n" +
            $"PUSHER PROXIMITY  : {(pusherSensorActive ? "<color=#ffaa00>PART PRESENT</color>" : "<color=#888888>CLEAR</color>")}\n" +
            $"ACTUATOR STROKE   : {(pusherCurrentStroke * 100f):F0} %";

        if (selectedWorkpiece != null && selectedWorkpiece.gameObject != null)
        {
            uiSelectedPartText.text =
                $"<color=#00e5ff>SELECTED WORKPIECE</color>\n" +
                $"SERIAL ID    : {selectedWorkpiece.serialId}\n" +
                $"STATUS       : {(selectedWorkpiece.rejected ? "<color=#ff3d00>REJECTED</color>" : (selectedWorkpiece.inspected ? "<color=#00e676>PASSED</color>" : "IN TRANSPORT"))}\n" +
                $"QUALITY      : {(selectedWorkpiece.qualityScore * 100f):F1}%\n" +
                $"DEFECT TYPE  : {selectedWorkpiece.defectType}";
        }
        else
        {
            uiSelectedPartText.text = "<color=#888888>Click any workpiece to inspect digital pedigree</color>";
        }
    }

    private GameObject CreateUIBox(string name, Transform parent, Vector2 pos, Vector2 size, Color col)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = col;
        return go;
    }

    private Image CreateUICircle(string name, Transform parent, Vector2 pos, float size, Color col)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);
        Image img = go.GetComponent<Image>();
        img.color = col;
        return img;
    }

    private Text CreateUILabel(string name, Transform parent, string text, float x, float y, float w, float h, int fontSize, bool bold, Color col)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);

        Text t = go.GetComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.color = col;
        t.alignment = TextAnchor.MiddleLeft;
        t.supportRichText = true;
        return t;
    }

    private Slider CreateProgressBar(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        GameObject sliderObj = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderObj.transform.SetParent(parent, false);
        RectTransform rt = sliderObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Slider slider = sliderObj.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.18f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = Vector2.zero;
        faRt.anchorMax = Vector2.one;
        faRt.sizeDelta = Vector2.zero;

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.sizeDelta = Vector2.zero;
        Image fillImg = fill.GetComponent<Image>();
        fillImg.color = new Color(0.2f, 0.7f, 1f);

        slider.fillRect = fillRt;
        slider.interactable = false;
        return slider;
    }

    private void CreateUIButton(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color col, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = col;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        Text t = CreateUILabel("Text", go.transform, label, 0, 0, size.x, size.y, 11, true, Color.white);
        t.alignment = TextAnchor.MiddleCenter;
    }

    // =========================================================================
    // EXPOSED AI REALVIRTUAL MCP TOOLS
    // =========================================================================

    [McpTool("Get complete digital twin telemetry of the Automated Sorting Cell including speeds, counts, sensor signals, and defect rate")]
    public static string CellGetTelemetry()
    {
        if (Instance == null)
            return "{\"error\":\"AutomatedSortingCellTwin instance not found. Run simulation first.\"}";

        return string.Format(
            "{{\"status\":\"ok\",\"conveyorSpeed\":{0:F2},\"isRunning\":{1},\"isEmergencyStopped\":{2},\"totalSpawned\":{3},\"passCount\":{4},\"rejectCount\":{5},\"defectRate\":{6:F2},\"throughputPpm\":{7:F1},\"infeedSensor\":{8},\"visionSensor\":{9},\"pusherSensor\":{10},\"pusherStroke\":{11:F2}}}",
            Instance.conveyorSpeed,
            Instance.isRunning ? "true" : "false",
            Instance.isEmergencyStopped ? "true" : "false",
            Instance.totalSpawned,
            Instance.passCount,
            Instance.rejectCount,
            Instance.defectRate,
            Instance.partsPerMinute,
            Instance.infeedSensorActive ? "true" : "false",
            Instance.visionSensorActive ? "true" : "false",
            Instance.pusherSensorActive ? "true" : "false",
            Instance.pusherCurrentStroke);
    }

    [McpTool("Set conveyor line transport speed in meters per second (0.4 to 4.0 m/s)")]
    public static string CellSetSpeed([McpParam("Desired speed in m/s")] float speed)
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.conveyorSpeed = Mathf.Clamp(speed, 0.4f, 4.0f);
        return string.Format("{{\"status\":\"ok\",\"newSpeed\":{0:F2}}}", Instance.conveyorSpeed);
    }

    [McpTool("Inject an intentional defect into the next spawned workpiece for sorting validation")]
    public static string CellInjectDefect([McpParam("Defect type: Dimension, Surface Flaw, or Random")] string defectType = "Surface Flaw")
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.queueNextDefect = true;
        Instance.nextDefectType = defectType;
        return string.Format("{{\"status\":\"ok\",\"queuedDefect\":\"{0}\"}}", defectType);
    }

    [McpTool("Manually fire the pneumatic sorting pusher actuator")]
    public static string CellTriggerPusher()
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.TriggerPusherActuation();
        return "{\"status\":\"ok\",\"action\":\"pusher_actuated\"}";
    }

    [McpTool("Set emergency stop state of the sorting cell")]
    public static string CellToggleEmergencyStop([McpParam("True to engage E-Stop, False to release")] bool engage)
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.isEmergencyStopped = engage;
        if (engage) Instance.isRunning = false;
        return string.Format("{{\"status\":\"ok\",\"emergencyStop\":{0}}}", engage ? "true" : "false");
    }

    [McpTool("Reset all part counters and statistics")]
    public static string CellResetStats()
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.totalSpawned = 0;
        Instance.passCount = 0;
        Instance.rejectCount = 0;
        return "{\"status\":\"ok\",\"action\":\"stats_reset\"}";
    }

    [McpTool("Switch camera view preset: overview, vision, pusher, or chute")]
    public static string CellSetCamera([McpParam("Camera preset name: overview, vision, pusher, or chute")] string preset)
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        int p = 1;
        string lower = preset.ToLower();
        if (lower.Contains("vision") || lower.Contains("inspect")) p = 2;
        else if (lower.Contains("push")) p = 3;
        else if (lower.Contains("chute") || lower.Contains("reject") || lower.Contains("top")) p = 4;

        Instance.ApplyCameraPreset(p);
        return string.Format("{{\"status\":\"ok\",\"activePreset\":{0}}}", p);
    }

    [McpTool("Spawn a workpiece immediately into the cell infeed")]
    public static string CellSpawnWorkpiece([McpParam("Whether part is defective")] bool defective, [McpParam("Defect description")] string defectType = "Manual")
    {
        if (Instance == null)
            return "{\"error\":\"Cell instance not running\"}";

        Instance.SpawnWorkpiece(defective, defectType);
        return string.Format("{{\"status\":\"ok\",\"spawned\":true,\"isDefective\":{0}}}", defective ? "true" : "false");
    }
}
