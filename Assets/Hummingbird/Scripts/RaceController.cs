using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Runs the two trained models against each other, side by side, on a keypress.
///
/// The point is to turn "41.29 versus 96.58 mean reward" from a number into something a viewer
/// watches. Each bird gets its own island so the race is fair -- two birds in one arena would be
/// competing for the same flowers, and the winner would partly be whoever arrived first.
///
/// Why the second island is created at load and left disabled rather than spawned on the keypress:
/// one island is ~3,837 GameObjects, and instantiating that many in a single frame visibly freezes
/// the screen. Doing it on the keypress would put that freeze at the exact moment of the demo. A
/// disabled GameObject runs no physics, renders nothing and does not tick, so it costs essentially
/// nothing while it waits, and the cost hides inside the scene load instead. The trade is memory
/// held for the session -- untick enableRaceMode and the scene is exactly as it was before this
/// component existed.
///
/// That disabled start is also required for correctness, not just for smoothness. ML-Agents builds
/// its sensors once in Agent.InitializeSensors() and assigning BehaviorParameters fields does not
/// rebuild them; only Agent.OnDisable() -> CleanupSensors() clears them. So the second bird's model
/// and stacking are set while it is disabled, and enabling it is what constructs the right sensor.
/// Model and stacking are always set together in ConfigureSecondBird, because pairing a model with
/// the wrong stacking feeds it 30 numbers when it wants 10 and the bird flies brain-dead with no
/// error logged anywhere.
/// </summary>
public class RaceController : MonoBehaviour
{
    [Tooltip("Untick to skip the pre-spawn entirely. The scene then behaves exactly as it did "
           + "before this component was added -- one island, nothing extra in memory.")]
    [SerializeField] private bool enableRaceMode = true;

    [Tooltip("Press to start/stop the race. R is already taken by the debug ray view.")]
    [SerializeField] private KeyCode raceKey = KeyCode.T;

    [Header("Scene references")]
    [Tooltip("FloatingIsland.prefab -- the same one IslandSpawner clones.")]
    [SerializeField] private GameObject islandPrefab;

    [Tooltip("The island already in the scene, carrying the RentedGPU_Final bird.")]
    [SerializeField] private GameObject mainIsland;

    [Tooltip("OnDevice_Final.onnx -- the HB_01 baseline, reward 41.29. Needs stacking 1.")]
    [SerializeField] private ModelAsset onDeviceModel;

    [Header("Layout")]
    [Tooltip("How far to the side the challenger island sits. FlowerArea.AreaDiameter is 20, so "
           + "25 keeps one island's colliders clear of the other's.")]
    [SerializeField] private float spacing = 25f;

    [Header("Trails")]
    [SerializeField] private Color rentedTrailColor = new Color(0.3f, 1f, 0.4f, 0.9f);
    [SerializeField] private Color onDeviceTrailColor = new Color(1f, 0.5f, 0.1f, 0.9f);

    private GameObject challengerHolder;
    private GameObject challengerIsland;
    private HummingbirdAgent rentedAgent;
    private HummingbirdAgent onDeviceAgent;
    private Camera rentedCamera;
    private Camera onDeviceCamera;

    private GameObject scoreboardRoot;
    private Text scoreLeft;
    private Text scoreRight;
    private bool racing;

    // Start, not Awake: IslandSpawner repositions the existing island in its own Awake to centre
    // the grid, so reading mainIsland's position any earlier gives the pre-move value and the
    // challenger lands in the wrong place.
    private void Start()
    {
        if (!enableRaceMode) return;

        if (islandPrefab == null || mainIsland == null || onDeviceModel == null)
        {
            Debug.LogError("RaceController: islandPrefab, mainIsland and onDeviceModel must all be "
                         + "assigned. Race mode disabled.");
            enableRaceMode = false;
            return;
        }

        rentedAgent = mainIsland.GetComponentInChildren<HummingbirdAgent>(true);
        if (rentedAgent == null)
        {
            Debug.LogError("RaceController: no HummingbirdAgent found under mainIsland.");
            enableRaceMode = false;
            return;
        }

        // The camera that actually renders this scene is Main Camera, carrying CameraFollowBird --
        // NOT the Camera child on the bird prefab. That child exists and is enabled as a component,
        // but its GameObject is inactive in Training.unity, so setting its viewport does nothing
        // visible. Splitting the wrong camera is exactly the bug this line fixes.
        rentedCamera = Camera.main;
        if (rentedCamera == null)
        {
            Debug.LogError("RaceController: no camera tagged MainCamera. Race mode disabled.");
            enableRaceMode = false;
            return;
        }

        SetTrailColor(rentedAgent, rentedTrailColor);

        // Instantiate INTO an already-inactive holder rather than spawning the island active and
        // switching it off afterwards. Unity skips Awake entirely for objects that are inactive in
        // the hierarchy, and that matters for three separate reasons:
        //
        //  1. Correctness. Spawning it active runs the clone's Awakes in an order Unity does not
        //     guarantee, and the agent's OnEnable -> LazyInitialize -> OnEpisodeBegin ->
        //     FlowerArea.ResetFlowers chain reached nectarFlowerDictionary.Add() before
        //     FlowerArea.Awake() had created that dictionary -- a NullReferenceException every
        //     time. Waking the whole subtree in one go instead lets every Awake run before any
        //     OnEnable.
        //  2. Cost. Otherwise the challenger builds sensors and allocates an inference worker at
        //     scene load purely to have them thrown away a line later, which showed up as
        //     undisposed-tensor warnings in the console.
        //  3. The model swap below then lands on an agent that has never initialised, so enabling
        //     it builds the right sensors the first time rather than rebuilding them.
        challengerHolder = new GameObject("RaceChallenger");
        challengerHolder.SetActive(false);

        challengerIsland = Instantiate(islandPrefab, challengerHolder.transform);
        challengerIsland.name = "FloatingIsland_Challenger";
        challengerIsland.transform.position = mainIsland.transform.position + Vector3.right * spacing;

        onDeviceAgent = challengerIsland.GetComponentInChildren<HummingbirdAgent>(true);
        if (onDeviceAgent == null)
        {
            Debug.LogError("RaceController: the spawned island has no HummingbirdAgent.");
            Destroy(challengerHolder);
            enableRaceMode = false;
            return;
        }

        // Hold the bird off even within the holder. When the holder is activated the island must
        // be allowed to complete its own Awake/Start pass BEFORE any agent starts an episode --
        // see EnableChallengerWhenReady for why. The bird is switched back on a frame later.
        onDeviceAgent.gameObject.SetActive(false);

        SetTrailColor(onDeviceAgent, onDeviceTrailColor);
        ConfigureSecondBird();
        onDeviceCamera = CreateChallengerCamera();
    }

    /// <summary>
    /// Builds the right-hand camera. Created rather than reused because the only camera rendering
    /// this scene is Main Camera, and it is already following the rented-GPU bird.
    /// </summary>
    private Camera CreateChallengerCamera()
    {
        var go = new GameObject("ChallengerCamera");
        var cam = go.AddComponent<Camera>();

        // Take field of view, clear flags, culling mask and clipping planes straight off the
        // camera already rendering the scene, so the two halves of the split look identical and
        // no setting has to be kept in sync by hand. Note this copies components' *settings* only:
        // no second AudioListener is created, which Unity would warn about.
        cam.CopyFrom(rentedCamera);
        cam.rect = new Rect(0.5f, 0f, 0.5f, 1f);

        // Added explicitly rather than letting URP lazily attach a default, so this is
        // unambiguously a Base camera drawing its own half of the screen and never an Overlay --
        // an Overlay camera ignores its viewport rect entirely, which would look like this fix
        // silently failing.
        go.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;

        // Reuse the scene's own follow behaviour instead of duplicating its maths, pinned by
        // SetTarget so it cannot latch onto the wrong bird -- its fallback lookup is
        // FindAnyObjectByType, which has no ordering guarantee once two birds exist.
        //
        // CopyFramingFrom matters as much as CopyFrom above: distance/height/smoothSpeed are
        // serialized per instance, and Main Camera is tuned to 0.8 / 0.3 / 8 in this scene while
        // the code defaults are 2 / 1 / 6. Without this the right half sits two and a half times
        // further back and reads as a wide establishing shot next to a tight chase view.
        var follow = go.AddComponent<CameraFollowBird>();
        follow.CopyFramingFrom(rentedCamera.GetComponent<CameraFollowBird>());
        follow.SetTarget(onDeviceAgent.transform);

        go.SetActive(false);
        return cam;
    }

    /// <summary>
    /// Points the challenger at the weaker model. Model and stacking are deliberately set in one
    /// place so they can never be changed apart: OnDevice_Final was trained at stacking 1
    /// (obs_3 [batch, 10]) while the prefab default is RentedGPU_Final at stacking 3
    /// (obs_3 [batch, 30]). Runs while the island is disabled, so enabling it rebuilds the sensors
    /// to match.
    /// </summary>
    private void ConfigureSecondBird()
    {
        var behaviourParameters = onDeviceAgent.GetComponent<BehaviorParameters>();

        behaviourParameters.BrainParameters.NumStackedVectorObservations = 1;
        behaviourParameters.Model = onDeviceModel;

        // The prefab default is BehaviorType.Default, which would try for a trainer connection
        // first. Training.unity pins its own bird to InferenceOnly; match that here so the
        // challenger behaves identically.
        behaviourParameters.BehaviorType = BehaviorType.InferenceOnly;
    }

    /// <summary>Whether the race is currently running. Read by the phone's Split button label.</summary>
    public bool IsRacing { get { return racing; } }

    /// <summary>Start/stop the race. Hooked to the phone's Split button and to the key below.</summary>
    public void ToggleRace()
    {
        if (!enableRaceMode) return;

        if (racing) StopRace();
        else StartRace();
    }

    private void Update()
    {
        if (!enableRaceMode) return;

        if (Input.GetKeyDown(raceKey)) ToggleRace();

        UpdateScoreboard();
    }

    private void StartRace()
    {
        // In Demo.unity the main bird stays disabled until START is pressed, and EndEpisode below
        // throws on an agent that has never initialised. Refusing here rather than crashing means
        // the Split button is simply inert until there is something to race against.
        if (rentedAgent == null || !rentedAgent.gameObject.activeInHierarchy)
        {
            Debug.Log("RaceController: press Start before beginning a race.");
            return;
        }

        racing = true;

        // Activating the holder wakes the whole island in one pass: every Awake runs, then every
        // OnEnable, which is what keeps FlowerArea initialised before the agent's episode starts.
        challengerHolder.SetActive(true);

        // Each visualizer builds its own Canvas anchored top-left, so without this the two birds'
        // observation readouts print on top of each other.
        SetReadoutSide(rentedAgent, 0f);
        SetReadoutSide(onDeviceAgent, 0.5f);

        // NectarCounterUI is a single static Instance shared by both birds, so during a race it
        // would show their combined nectar and read as one bird doing implausibly well. The
        // scoreboard reports the two separately.
        if (NectarCounterUI.Instance != null) NectarCounterUI.Instance.SetVisible(false);

        // Main Camera keeps following the rented-GPU bird, just squeezed into the left half; the
        // challenger camera built in Start takes the right.
        rentedCamera.rect = new Rect(0f, 0f, 0.5f, 1f);
        if (onDeviceCamera != null) onDeviceCamera.gameObject.SetActive(true);

        // Restart both episodes together. NectarObtained resets in OnEpisodeBegin and both agents
        // run the same MaxStep, so starting them level is what makes the two scores comparable
        // rather than being an accident of how long each had already been flying. The challenger
        // joins one frame later, which is immaterial against a 5000-step episode.
        rentedAgent.EndEpisode();
        StartCoroutine(EnableChallengerWhenReady());

    }

    /// <summary>
    /// Brings the challenger bird in one frame after its island, and never during the island's own
    /// activation walk.
    ///
    /// The reason is subtle and cost real debugging time. Every Flower assigns its nectarCollider
    /// in Flower.Awake() (transform.Find("FlowerNectarCollider")) -- it is not a serialized
    /// reference. FlowerArea's one-shot scan reads that collider into a dictionary keyed by it. So
    /// if an agent's OnEnable -> OnEpisodeBegin -> ResetFlowers chain fires while Unity is still
    /// walking the newly activated hierarchy, the scan runs against flowers whose Awake has not
    /// happened yet, every key is null, the second one throws
    /// "An item with the same key has already been added. Key: null", and the scan aborts having
    /// registered 2 flowers out of 360 -- with flowersFound already latched true, so it never
    /// retries. The island then looks almost empty and the challenger has nothing to feed on.
    ///
    /// Yielding a frame lets the island finish waking, so FlowerArea.Start() performs the scan with
    /// every Flower already awake and every collider assigned.
    /// </summary>
    private System.Collections.IEnumerator EnableChallengerWhenReady()
    {
        yield return null;

        // The race may already have been toggled off again during that frame.
        if (!racing) yield break;

        onDeviceAgent.gameObject.SetActive(true);
        onDeviceAgent.EndEpisode();
    }

    private void StopRace()
    {
        racing = false;

        // Switched off before the holder so the next StartRace activates the island with the bird
        // already held back, exactly as the first run did. Without this, a second race would wake
        // the bird together with the island and reintroduce the early-scan bug above.
        onDeviceAgent.gameObject.SetActive(false);
        challengerHolder.SetActive(false);

        rentedCamera.rect = new Rect(0f, 0f, 1f, 1f);
        if (onDeviceCamera != null) onDeviceCamera.gameObject.SetActive(false);

        // The scoreboard deliberately stays up: UpdateScoreboard switches it to a single centred
        // label naming the loaded model. Hiding it here would leave nothing on screen saying which
        // model you are watching, which is what the removed yellow caption used to do.

        // Back to one bird, so the remaining readout belongs at the left edge of a full-width view.
        SetReadoutSide(rentedAgent, 0f);
    }

    /// <summary>
    /// Drives the score display in both modes: two labels side by side during a race, and a single
    /// centred one the rest of the time naming whichever model is loaded.
    ///
    /// Running in both modes is what lets the yellow model caption go away entirely. Previously
    /// three separate things told you roughly the same thing -- that caption, the nectar counter,
    /// and this scoreboard -- each in a different place and only some of the time.
    /// </summary>
    private void UpdateScoreboard()
    {
        // Nothing to report before Start is pressed in the phone demo, where the bird is disabled.
        bool birdFlying = rentedAgent != null && rentedAgent.gameObject.activeInHierarchy;
        if (!birdFlying)
        {
            if (scoreboardRoot != null) scoreboardRoot.SetActive(false);
            return;
        }

        if (scoreboardRoot == null) BuildScoreboard();
        if (!scoreboardRoot.activeSelf) scoreboardRoot.SetActive(true);

        // The nectar counter would sit in the same place saying less -- a cumulative touch count
        // rather than this episode's nectar -- so the scoreboard supersedes it.
        if (NectarCounterUI.Instance != null) NectarCounterUI.Instance.SetVisible(false);

        float rented = rentedAgent.NectarObtained;

        if (!racing)
        {
            // One centred label. It names whichever model is actually loaded, so it stays correct
            // after the On-Device / Rented GPU buttons are used.
            SetScoreAnchorX(scoreLeft, 0.5f);
            scoreRight.gameObject.SetActive(false);

            var behaviour = rentedAgent.GetComponent<BehaviorParameters>();
            bool isRented = behaviour.Model != onDeviceModel;

            scoreLeft.color = isRented ? rentedTrailColor : onDeviceTrailColor;
            scoreLeft.text = (isRented ? "RENTED GPU (96.58)" : "ON-DEVICE (41.29)")
                           + "\n" + rented.ToString("F2") + "\n ";
            return;
        }

        SetScoreAnchorX(scoreLeft, 0.25f);
        scoreRight.gameObject.SetActive(true);
        scoreLeft.color = rentedTrailColor;

        float onDevice = onDeviceAgent.NectarObtained;

        // The trailing line is always present, blank for the loser, so the layout does not jump
        // every time the lead changes.
        scoreLeft.text = "RENTED GPU (96.58)\n" + rented.ToString("F2")
                       + "\n" + (rented >= onDevice ? "LEADING" : " ");
        scoreRight.text = "ON-DEVICE (41.29)\n" + onDevice.ToString("F2")
                        + "\n" + (onDevice > rented ? "LEADING" : " ");
    }

    private void SetScoreAnchorX(Text label, float anchorX)
    {
        var rt = label.GetComponent<RectTransform>();
        if (Mathf.Approximately(rt.anchorMin.x, anchorX)) return;

        rt.anchorMin = new Vector2(anchorX, 1f);
        rt.anchorMax = new Vector2(anchorX, 1f);
    }

    private void SetReadoutSide(HummingbirdAgent agent, float anchorX)
    {
        if (agent == null) return;

        var viz = agent.GetComponent<RaySensorVisualizer>();
        if (viz != null) viz.SetReadoutAnchorX(anchorX);
    }

    private void SetTrailColor(HummingbirdAgent agent, Color color)
    {
        var trail = agent.GetComponent<FlightTrail>();
        if (trail != null) trail.SetColor(color);
    }

    /// <summary>
    /// Builds its own Canvas at runtime, the same way NectarCounterUI does, because Training.unity
    /// has no Canvas and no EventSystem of its own. Plain text needs no EventSystem.
    /// </summary>
    private void BuildScoreboard()
    {
        scoreboardRoot = new GameObject("RaceScoreboardCanvas");
        var canvas = scoreboardRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;

        UiScaling.Apply(scoreboardRoot.AddComponent<CanvasScaler>());

        // One label centred over each half of the split, each in its own bird's trail colour, so
        // the score and the ribbon on screen identify the same model without a legend.
        scoreLeft = BuildScoreLabel("ScoreRented", 0.25f, rentedTrailColor);
        scoreRight = BuildScoreLabel("ScoreOnDevice", 0.75f, onDeviceTrailColor);
    }

    private Text BuildScoreLabel(string name, float anchorX, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(scoreboardRoot.transform, false);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 34;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.UpperCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        // The scoreboard sits at the top of the frame, which is where the near-white sky is, and
        // coloured text alone washes out against it. A hard black outline keeps it readable over
        // sky, grass and rock alike without putting a panel behind it and hiding the gameplay.
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorX, 1f);
        rt.anchorMax = new Vector2(anchorX, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -20f);
        rt.sizeDelta = new Vector2(500f, 120f);

        return text;
    }
}
