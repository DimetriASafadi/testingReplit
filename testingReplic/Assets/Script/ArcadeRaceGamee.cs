using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ArcadeRaceGame : MonoBehaviour
{
    private const int TrackSegments = 36;
    private const float TrackRadiusX = 30f;
    private const float TrackRadiusZ = 22f;
    private const float TrackWidth = 11f;

    [SerializeField] private int totalLaps = 3;

    private ArcadeCarController car;
    private Transform carTransform;
    private Text lapText;
    private Text speedText;
    private Text statusText;
    private float lastProgress;
    private int completedLaps;
    private bool raceFinished;

    private Material grassMaterial;
    private Material asphaltMaterial;
    private Material edgeMaterial;
    private Material lineMaterial;
    private Material redMaterial;
    private Material glassMaterial;
    private Material tireMaterial;
    private Material whiteMaterial;
    private Font uiFont;

    private void Start()
    {
        Application.targetFrameRate = 60;
        BuildMaterials();
        BuildWorld();
        BuildCar();
        BuildCamera();
        BuildHud();
        UpdateHud();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetRace();
        }

        if (car == null || carTransform == null || raceFinished)
        {
            UpdateHud();
            return;
        }

        float angle = Mathf.Atan2(
            carTransform.position.z / TrackRadiusZ,
            carTransform.position.x / TrackRadiusX) * Mathf.Rad2Deg;
        float progress = Mathf.Repeat(angle + 90f, 360f);

        if (lastProgress > 300f && progress < 60f && car.CurrentSpeed > 2f)
        {
            completedLaps++;
            if (completedLaps >= totalLaps)
            {
                raceFinished = true;
                car.SetRaceFinished(true);
                statusText.text = "FINISH!  Press R to race again";
            }
        }

        lastProgress = progress;
        UpdateHud();
    }

    private void BuildMaterials()
    {
        grassMaterial = CreateMaterial("Grass", new Color(0.08f, 0.24f, 0.12f), 0f, 0.15f);
        asphaltMaterial = CreateMaterial("Asphalt", new Color(0.055f, 0.065f, 0.08f), 0.05f, 0.45f);
        edgeMaterial = CreateMaterial("Safety Red", new Color(0.72f, 0.06f, 0.045f), 0.05f, 0.3f);
        lineMaterial = CreateMaterial("Track Markings", new Color(0.95f, 0.82f, 0.28f), 0f, 0.25f);
        redMaterial = CreateMaterial("Car Red", new Color(0.9f, 0.04f, 0.035f), 0.35f, 0.7f);
        glassMaterial = CreateMaterial("Car Glass", new Color(0.03f, 0.15f, 0.22f), 0.1f, 0.8f);
        tireMaterial = CreateMaterial("Tire", new Color(0.012f, 0.012f, 0.015f), 0f, 0.15f);
        whiteMaterial = CreateMaterial("White", new Color(0.95f, 0.95f, 0.92f), 0f, 0.3f);
        uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        RenderSettings.ambientLight = new Color(0.22f, 0.25f, 0.32f);
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.035f, 0.055f, 0.09f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 190f;
    }

    private void BuildWorld()
    {
        CreatePrimitive("Ground", PrimitiveType.Plane, new Vector3(0f, -0.25f, 0f),
            new Vector3(15f, 1f, 15f), Quaternion.identity, grassMaterial, true);

        float segmentLength = 2f * Mathf.PI * Mathf.Sqrt(
            (TrackRadiusX * TrackRadiusX + TrackRadiusZ * TrackRadiusZ) / 2f) / TrackSegments;

        for (int i = 0; i < TrackSegments; i++)
        {
            float degrees = i * 360f / TrackSegments;
            float radians = degrees * Mathf.Deg2Rad;
            Vector3 center = new Vector3(
                TrackRadiusX * Mathf.Cos(radians), 0f,
                TrackRadiusZ * Mathf.Sin(radians));
            Vector3 tangent = new Vector3(
                -TrackRadiusX * Mathf.Sin(radians), 0f,
                TrackRadiusZ * Mathf.Cos(radians)).normalized;
            Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);

            CreatePrimitive("Road", PrimitiveType.Cube, center,
                new Vector3(TrackWidth, 0.18f, segmentLength * 1.28f),
                rotation, asphaltMaterial, true);

            CreatePrimitive("Lane Marker", PrimitiveType.Cube, center + Vector3.up * 0.105f,
                new Vector3(0.24f, 0.025f, segmentLength * 0.42f),
                rotation, lineMaterial, false);

            CreateBarrier(degrees, TrackRadiusX + TrackWidth * 0.62f,
                TrackRadiusZ + TrackWidth * 0.62f, i % 2 == 0);
            CreateBarrier(degrees, TrackRadiusX - TrackWidth * 0.62f,
                TrackRadiusZ - TrackWidth * 0.62f, i % 2 != 0);
        }

        BuildStartLine();
        BuildTrackDecorations();
        CreateDirectionalLight();
    }

    private void CreateBarrier(float degrees, float radiusX, float radiusZ, bool red)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector3 center = new Vector3(radiusX * Mathf.Cos(radians), 0.55f,
            radiusZ * Mathf.Sin(radians));
        Vector3 tangent = new Vector3(-radiusX * Mathf.Sin(radians), 0f,
            radiusZ * Mathf.Cos(radians)).normalized;
        Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);
        CreatePrimitive("Safety Barrier", PrimitiveType.Cube, center,
            new Vector3(0.55f, 1.1f, 3.2f), rotation,
            red ? edgeMaterial : whiteMaterial, true);
    }

    private void BuildStartLine()
    {
        float startZ = -TrackRadiusZ;
        for (int i = 0; i < 8; i++)
        {
            CreatePrimitive("Start Grid", PrimitiveType.Cube,
                new Vector3(-4.375f + i * 1.25f, 0.12f, startZ),
                new Vector3(1.25f, 0.035f, 0.9f), Quaternion.identity,
                i % 2 == 0 ? whiteMaterial : asphaltMaterial, false);
        }

        CreatePrimitive("Start Post Left", PrimitiveType.Cube,
            new Vector3(-6f, 2.2f, startZ), new Vector3(0.4f, 4.4f, 0.4f),
            Quaternion.identity, edgeMaterial, true);
        CreatePrimitive("Start Post Right", PrimitiveType.Cube,
            new Vector3(6f, 2.2f, startZ), new Vector3(0.4f, 4.4f, 0.4f),
            Quaternion.identity, edgeMaterial, true);
        CreatePrimitive("Start Banner", PrimitiveType.Cube,
            new Vector3(0f, 4.15f, startZ), new Vector3(12.4f, 0.45f, 0.45f),
            Quaternion.identity, lineMaterial, true);
    }

    private void BuildTrackDecorations()
    {
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            Vector3 position = new Vector3(10.5f * Mathf.Cos(angle), 0.65f,
                7.5f * Mathf.Sin(angle));
            CreatePrimitive("Inner Marker", PrimitiveType.Cylinder, position,
                new Vector3(0.42f, 0.65f, 0.42f), Quaternion.identity,
                i % 2 == 0 ? lineMaterial : edgeMaterial, true);
        }
    }

    private void BuildCar()
    {
        GameObject carObject = new GameObject("Player Car");
        carObject.transform.position = new Vector3(0f, 0.6f, -TrackRadiusZ);
        carObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        carTransform = carObject.transform;

        Rigidbody body = carObject.AddComponent<Rigidbody>();
        body.mass = 1100f;
        body.linearDamping = 0.45f;
        body.angularDamping = 4f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        BoxCollider collider = carObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.1f, 0f);
        collider.size = new Vector3(1.8f, 0.8f, 3.6f);

        car = carObject.AddComponent<ArcadeCarController>();
        car.SetStartingPosition(carTransform.position, carTransform.rotation);
        BuildCarVisuals(carObject.transform);
    }

    private void BuildCarVisuals(Transform parent)
    {
        CreatePrimitive("Car Body", PrimitiveType.Cube, parent.position,
            new Vector3(1.8f, 0.55f, 3.6f), parent.rotation, redMaterial, false, parent);
        CreatePrimitive("Car Cabin", PrimitiveType.Cube, parent.position + parent.up * 0.58f,
            new Vector3(1.42f, 0.5f, 1.65f), parent.rotation, glassMaterial, false, parent);
        CreatePrimitive("Front Bumper", PrimitiveType.Cube, parent.position + parent.forward * 1.78f,
            new Vector3(1.9f, 0.22f, 0.18f), parent.rotation, whiteMaterial, false, parent);
        CreatePrimitive("Rear Spoiler", PrimitiveType.Cube, parent.position - parent.forward * 1.55f + parent.up * 0.6f,
            new Vector3(1.9f, 0.16f, 0.3f), parent.rotation, edgeMaterial, false, parent);

        Vector3[] wheelPositions =
        {
            new Vector3(-0.98f, -0.12f, 1.15f),
            new Vector3(0.98f, -0.12f, 1.15f),
            new Vector3(-0.98f, -0.12f, -1.15f),
            new Vector3(0.98f, -0.12f, -1.15f)
        };

        foreach (Vector3 localPosition in wheelPositions)
        {
            GameObject wheel = CreatePrimitive("Wheel", PrimitiveType.Cylinder,
                parent.TransformPoint(localPosition), new Vector3(0.48f, 0.18f, 0.48f),
                parent.rotation * Quaternion.Euler(0f, 0f, 90f), tireMaterial, false, parent);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    private void BuildCamera()
    {
        GameObject cameraObject = new GameObject("Race Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 64f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;
        cameraObject.AddComponent<AudioListener>();
        RaceCameraFollow follow = cameraObject.AddComponent<RaceCameraFollow>();
        follow.SetTarget(carTransform);
    }

    private void BuildHud()
    {
        GameObject canvasObject = new GameObject("Race HUD");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280f, 720f);
        canvasObject.AddComponent<GraphicRaycaster>();

        CreatePanel(canvasObject.transform, new Vector2(22f, -20f),
            new Vector2(380f, 98f), new Color(0.015f, 0.025f, 0.055f, 0.88f));
        CreatePanel(canvasObject.transform, new Vector2(-22f, -20f),
            new Vector2(230f, 84f), new Color(0.015f, 0.025f, 0.055f, 0.88f), true);

        CreateText(canvasObject.transform, "NEON CIRCUIT", 28, Color.white,
            new Vector2(22f, -20f), new Vector2(350f, 42f), TextAnchor.UpperLeft, out _);
        CreateText(canvasObject.transform, "WASD / ARROWS  DRIVE     SPACE  BRAKE     R  RESET",
            16, new Color(0.65f, 0.75f, 0.86f), new Vector2(22f, -65f),
            new Vector2(365f, 28f), TextAnchor.UpperLeft, out _);
        CreateText(canvasObject.transform, "LAP 1 / 3", 24, lineMaterial.color,
            new Vector2(-22f, -20f), new Vector2(210f, 36f), TextAnchor.UpperRight,
            out lapText, true);
        CreateText(canvasObject.transform, "0 KM/H", 17, Color.white,
            new Vector2(-22f, -55f), new Vector2(210f, 28f), TextAnchor.UpperRight,
            out speedText, true);
        CreateText(canvasObject.transform, "GO!", 32, Color.white,
            new Vector2(0f, -140f), new Vector2(600f, 55f), TextAnchor.UpperCenter,
            out statusText, false, true);
    }

    private void UpdateHud()
    {
        if (lapText == null || speedText == null || car == null)
        {
            return;
        }

        int displayLap = Mathf.Min(completedLaps + 1, totalLaps);
        lapText.text = raceFinished ? "FINISHED" : $"LAP {displayLap} / {totalLaps}";
        speedText.text = $"{Mathf.RoundToInt(car.CurrentSpeed * 3.6f):000} KM/H";
        if (!raceFinished)
        {
            statusText.text = completedLaps == 0 ? "GO!" : "KEEP PUSHING!";
        }
    }

    private void ResetRace()
    {
        completedLaps = 0;
        lastProgress = 0f;
        raceFinished = false;
        car.SetRaceFinished(false);
        car.ResetCar();
        statusText.text = "GO!";
    }

    private GameObject CreatePrimitive(string objectName, PrimitiveType primitiveType,
        Vector3 position, Vector3 scale, Quaternion rotation, Material material,
        bool keepCollider, Transform parent = null)
    {
        GameObject result = GameObject.CreatePrimitive(primitiveType);
        result.name = objectName;
        result.transform.position = position;
        result.transform.rotation = rotation;
        result.transform.localScale = scale;
        if (parent != null)
        {
            result.transform.SetParent(parent);
        }

        Renderer renderer = result.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        if (!keepCollider)
        {
            Collider collider = result.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        return result;
    }

    private GameObject CreatePanel(Transform parent, Vector2 anchoredPosition,
        Vector2 size, Color color, bool right = false)
    {
        GameObject panel = new GameObject("HUD Panel");
        panel.transform.SetParent(parent, false);
        Image image = panel.AddComponent<Image>();
        image.color = color;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = right ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        return panel;
    }

    private void CreateText(Transform parent, string text, int fontSize, Color color,
        Vector2 anchoredPosition, Vector2 size, TextAnchor alignment, out Text output,
        bool right = false, bool center = false)
    {
        GameObject textObject = new GameObject("HUD Text");
        textObject.transform.SetParent(parent, false);
        output = textObject.AddComponent<Text>();
        output.text = text;
        output.font = uiFont;
        output.fontSize = fontSize;
        output.fontStyle = FontStyle.Bold;
        output.color = color;
        output.alignment = alignment;
        output.horizontalOverflow = HorizontalWrapMode.Overflow;
        output.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = center ? new Vector2(0.5f, 1f) :
            (right ? new Vector2(1f, 1f) : new Vector2(0f, 1f));
        rect.anchorMax = rect.anchorMin;
        rect.pivot = center ? new Vector2(0.5f, 1f) :
            (right ? new Vector2(1f, 1f) : new Vector2(0f, 1f));
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private Material CreateMaterial(string materialName, Color color,
        float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private void CreateDirectionalLight()
    {
        GameObject lightObject = new GameObject("Sun");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.color = new Color(0.68f, 0.8f, 1f);
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
    }
}

public class ArcadeCarController : MonoBehaviour
{
    [SerializeField] private float maxSpeed = 24f;
    [SerializeField] private float reverseSpeed = 8f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float braking = 28f;
    [SerializeField] private float steering = 78f;
    [SerializeField] private float grip = 8f;

    private Rigidbody body;
    private Vector3 startingPosition;
    private Quaternion startingRotation;
    private bool raceFinished;

    public float CurrentSpeed => body == null ? 0f : body.linearVelocity.magnitude;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (raceFinished || Keyboard.current == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        float throttle = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
            (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
        float steer = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        bool handbrake = keyboard.spaceKey.isPressed;

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        float targetSpeed = throttle >= 0f ? throttle * maxSpeed : throttle * reverseSpeed;
        float rate = throttle == 0f ? braking * 0.45f :
            (Mathf.Abs(targetSpeed) < Mathf.Abs(forwardSpeed) ? braking : acceleration);
        forwardSpeed = Mathf.MoveTowards(forwardSpeed, targetSpeed, rate * Time.fixedDeltaTime);

        float steerAmount = steering * steer * Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 3f) *
            Time.fixedDeltaTime * (forwardSpeed < 0f ? -1f : 1f);
        body.MoveRotation(body.rotation * Quaternion.Euler(0f, steerAmount, 0f));

        Vector3 desiredVelocity = transform.forward * forwardSpeed;
        float currentGrip = handbrake ? grip * 0.2f : grip;
        body.linearVelocity = Vector3.Lerp(body.linearVelocity, desiredVelocity,
            currentGrip * Time.fixedDeltaTime);
    }

    public void SetStartingPosition(Vector3 position, Quaternion rotation)
    {
        startingPosition = position;
        startingRotation = rotation;
    }

    public void ResetCar()
    {
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = startingPosition;
        body.rotation = startingRotation;
    }

    public void SetRaceFinished(bool finished)
    {
        raceFinished = finished;
        if (finished)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}

public class RaceCameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0f, 7.5f, -11f);
    [SerializeField] private float followSpeed = 7f;
    private Transform target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        transform.position = target.TransformPoint(offset);
        transform.LookAt(target.position + Vector3.up * 0.7f);
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 wantedPosition = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, wantedPosition,
            followSpeed * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 0.65f);
    }
}