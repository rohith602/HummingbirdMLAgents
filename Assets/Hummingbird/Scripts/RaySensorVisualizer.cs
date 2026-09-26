using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A debug view of everything the hummingbird perceives, toggled with a key. Press it once and
/// you see both halves of the agent's input at the same time:
///
///   * the 9 ray sensors, drawn as coloured lines  -- what it uses to avoid the arena wall
///   * a thick line to the flower it is hunting, plus the 10 vector observations as numbers
///     -- what it actually uses to find nectar
///
/// That split is the point of this component. The ray sensors' DetectableTags list is
/// ["", "boundary"], so a ray can only ever tag-match the arena boundary -- the rays cannot see
/// flowers at all. Every bit of flower-seeking comes from the vector observations, which are
/// invisible without something like this.
///
/// Do NOT add entries to DetectableTags to make flowers ray-visible: observations per ray are
/// numDetectableTags + 2, so changing that list changes the observation width and breaks both
/// trained models (they expect 4 floats per ray).
///
/// Why not the built-in "Debug Gizmos" checkbox on the sensor components: gizmos only draw
/// through the Gizmos pipeline (Scene view, or Game view with its Gizmos toggle on) and never
/// appear in a build. LineRenderers and a runtime Canvas are real objects, so this same
/// component will work unchanged if it is later added to the phone demo scene -- that port only
/// needs an on-screen button in place of the key, since a phone has no keyboard.
///
/// This bird carries three sensors -- RayForward (RaysPerDirection 3, so 2*3+1 = 7 rays fanned
/// over +/-60 degrees), RayUp and RayDown (1 ray each) -- 9 rays total, all 20 units long.
///
/// Nothing here is built or drawn until the key is pressed, so the scene behaves exactly as it
/// does today until you ask for the overlay.
/// </summary>
public class RaySensorVisualizer : MonoBehaviour
{
    [Tooltip("Press this to show/hide the whole debug view. Off until pressed.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.R;

    [Header("Ray sensors")]
    // Sized for the scene's actual chase camera, which sits ~0.85 units behind the bird. At that
    // range the original 0.015 rendered like a plank and the nine rays swallowed the frame.
    [SerializeField] private float rayWidth = 0.004f;

    [Tooltip("Ray reached its full length without hitting anything")]
    [SerializeField] private Color missColor = new Color(0.25f, 0.85f, 1f, 0.35f);

    [Tooltip("Ray hit a collider tagged 'boundary' -- the edge of the arena")]
    [SerializeField] private Color boundaryColor = new Color(1f, 0.25f, 0.25f, 0.9f);

    [Tooltip("Ray hit untagged geometry (island, flower meshes, props)")]
    [SerializeField] private Color geometryColor = new Color(1f, 0.85f, 0.2f, 0.9f);

    [Header("Vector observations")]
    // Kept clearly the thickest of the three so the flower line still reads as the important one,
    // but scaled to the same close camera as rayWidth above.
    [SerializeField] private float targetWidth = 0.012f;
    [SerializeField] private float arrowWidth = 0.008f;

    [Tooltip("World-space length of the two short direction arrows. Tune to taste -- the right "
           + "value depends on how close the demo camera sits to the bird.")]
    [SerializeField] private float arrowLength = 0.3f;

    [Tooltip("Beak is on the flower's open side (observation 8 = +1)")]
    [SerializeField] private Color alignedColor = new Color(0.2f, 1f, 0.35f, 0.95f);

    [Tooltip("Beak is behind the flower (observation 8 = -1)")]
    [SerializeField] private Color misalignedColor = new Color(1f, 0.3f, 0.15f, 0.95f);

    [Tooltip("The flower's own axis -- the direction the bird has to come in along")]
    [SerializeField] private Color flowerAxisColor = new Color(1f, 0.4f, 0.85f, 0.95f);

    [Tooltip("Where the beak is actually pointing")]
    [SerializeField] private Color beakForwardColor = new Color(0.4f, 0.75f, 1f, 0.95f);

    private RayPerceptionSensorComponentBase[] sensors;
    private HummingbirdAgent agent;

    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private Material lineMaterial;

    private Text readout;

    /// <summary>
    /// Whether the debug view is showing, shared by every bird, so the phone's Debug button turns
    /// it on for both racers at once. Static for the same reason as FlightTrail.Visible: the
    /// challenger does not exist until split screen is switched on, and must adopt the current
    /// state when it wakes rather than starting at its own default.
    /// </summary>
    public static bool Visible;

    // Every bird sees the same keypress in the same frame; without this guard two birds would each
    // flip the shared flag and it would never change. Same trap as FlightTrail.
    private static int lastToggleFrame = -1;

    // Where this bird's numeric readout sits, as a normalised screen x. Two birds both anchored at
    // 0 would print on top of each other, so RaceController moves the challenger's to 0.5.
    private float readoutAnchorX;

    private void Start()
    {
        // true = include inactive, so this still works if the sensors sit on disabled children
        sensors = GetComponentsInChildren<RayPerceptionSensorComponentBase>(true);
        agent = GetComponent<HummingbirdAgent>();

        // Sprites/Default respects LineRenderer's start/end colours and renders under URP.
        lineMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey) && lastToggleFrame != Time.frameCount)
        {
            lastToggleFrame = Time.frameCount;
            ToggleAll();
        }

        if (!Visible) return;

        // Built on first display rather than in Start, so a scene that never turns the debug view
        // on never gets an extra Canvas.
        if (readout == null) BuildReadout();
        if (!readout.gameObject.activeSelf) readout.gameObject.SetActive(true);

        int lineIndex = DrawRays(0);
        lineIndex = DrawObservations(lineIndex);

        // Any renderer left over from a frame that drew more lines stays hidden.
        for (int i = lineIndex; i < lines.Count; i++) lines[i].enabled = false;
    }

    /// <summary>Flips the debug view on every bird. Used by the key above and the Debug button.</summary>
    public static void ToggleAll()
    {
        SetAll(!Visible);
    }

    public static void SetAll(bool visible)
    {
        Visible = visible;

        var all = FindObjectsByType<RaySensorVisualizer>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++) all[i].HideIfOff();
    }

    /// <summary>
    /// Turning off has to be pushed to each bird; turning on does not, because Update redraws.
    /// </summary>
    private void HideIfOff()
    {
        if (Visible) return;

        for (int i = 0; i < lines.Count; i++) lines[i].enabled = false;
        if (readout != null) readout.gameObject.SetActive(false);
    }

    /// <summary>
    /// Moves this bird's readout to a normalised screen x (0 = left edge, 0.5 = middle). Every
    /// visualizer builds its own Canvas anchored top-left, so in split screen the two would print
    /// on top of each other unless the challenger's is moved to its own half.
    /// </summary>
    public void SetReadoutAnchorX(float normalizedX)
    {
        readoutAnchorX = normalizedX;
        if (readout == null) return;

        var rt = readout.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(readoutAnchorX, 1f);
        rt.anchorMax = new Vector2(readoutAnchorX, 1f);
    }

    /// <summary>Draws every ray of every sensor. Returns the next free line index.</summary>
    private int DrawRays(int lineIndex)
    {
        if (sensors == null) return lineIndex;

        for (int s = 0; s < sensors.Length; s++)
        {
            var sensor = sensors[s];
            if (sensor == null) continue;

            var input = sensor.GetRayPerceptionInput();

            // false = do the raycasts immediately rather than batching them across frames;
            // this is a handful of rays and we need the result within this frame to draw it.
            var output = RayPerceptionSensor.Perceive(input, false);

            for (int i = 0; i < input.Angles.Count; i++)
            {
                var extents = input.RayExtents(i);
                var ray = output.RayOutputs[i];

                // Stop the line at the hit point rather than drawing through the obstacle,
                // which is what makes "the bird can see this wall" read clearly on screen.
                Vector3 end = ray.HasHit
                    ? Vector3.Lerp(extents.StartPositionWorld, extents.EndPositionWorld, ray.HitFraction)
                    : extents.EndPositionWorld;

                Color c = !ray.HasHit ? missColor
                        : (ray.HitTagIndex >= 0 ? boundaryColor : geometryColor);

                Draw(lineIndex++, extents.StartPositionWorld, end, c, rayWidth);
            }
        }

        return lineIndex;
    }

    /// <summary>
    /// Draws the three lines that make the vector observations legible, and fills in the numeric
    /// readout. Returns the next free line index.
    ///
    /// ponytail: recomputes the observations from the beak and flower transforms rather than
    /// reading the live VectorSensor. Reading the real buffer means calling ISensor.Write() a
    /// second time, which can clear the buffer the agent is about to send -- corrupting the run
    /// in order to observe it. The values are identical, but this has to be kept in step with
    /// HummingbirdAgent.CollectObservations if that ever changes.
    /// </summary>
    private int DrawObservations(int lineIndex)
    {
        if (agent == null || agent.beakTip == null)
        {
            SetReadout("no agent");
            return lineIndex;
        }

        Transform beakTip = agent.beakTip;
        Flower flower = agent.NearestFlower;

        // Null on the first frames of an episode, and again once every flower is drained.
        if (flower == null)
        {
            SetReadout(string.Format(
                "DEBUG VIEW   ({0} to hide)\n\nrays      9   red = boundary, yellow = geometry\n"
                + "target    none -- no flower with nectar left\n\n"
                + "OBSERVATIONS (10)\n  all zero (the agent observes an empty array)",
                toggleKey));
            return lineIndex;
        }

        // The same five quantities CollectObservations feeds the network, in the same order.
        Quaternion rotation = agent.transform.localRotation.normalized;
        Vector3 toFlower = flower.FlowerCenterPosition - beakTip.position;
        Vector3 toFlowerDir = toFlower.normalized;
        Vector3 flowerAxis = -flower.FlowerUpVector.normalized;
        float approachDot = Vector3.Dot(toFlowerDir, flowerAxis);
        float beakDot = Vector3.Dot(beakTip.forward.normalized, flowerAxis);
        float relativeDistance = toFlower.magnitude / FlowerArea.AreaDiameter;

        // Observations 5-7 and 10: where the flower is, and how far. Green when the bird is on
        // the flower's open side, red when it is coming at it from behind.
        Color targetColor = Color.Lerp(misalignedColor, alignedColor, (approachDot + 1f) * 0.5f);
        Draw(lineIndex++, beakTip.position, flower.FlowerCenterPosition, targetColor, targetWidth);

        // The axis both dot products are measured against -- drawing it is what makes the two
        // numbers mean something rather than being bare floats.
        Draw(lineIndex++,
             flower.FlowerCenterPosition,
             flower.FlowerCenterPosition + flowerAxis * arrowLength,
             flowerAxisColor, arrowWidth);

        // Observation 9: line this up with the pink flower axis and the beak dot reads +1.
        Draw(lineIndex++,
             beakTip.position,
             beakTip.position + beakTip.forward.normalized * arrowLength,
             beakForwardColor, arrowWidth);

        SetReadout(string.Format(
            "DEBUG VIEW   ({0} to hide)\n\n"
            + "rays      9   red = boundary, yellow = geometry\n"
            + "target    nectar {1:F2} at {2:F2} m\n\n"
            + "OBSERVATIONS (10)\n"
            + " 1-4  rotation    ({3,6:F2},{4,6:F2},{5,6:F2},{6,6:F2})\n"
            + " 5-7  to flower   ({7,6:F2},{8,6:F2},{9,6:F2})\n"
            + " 8    approach    {10,6:F2}   {11}\n"
            + " 9    beak align  {12,6:F2}   {13}\n"
            + "10    distance    {14,6:F3}   ({2:F2} m / {15:F0})",
            toggleKey,
            flower.NectarAmount, toFlower.magnitude,
            rotation.x, rotation.y, rotation.z, rotation.w,
            toFlowerDir.x, toFlowerDir.y, toFlowerDir.z,
            approachDot, approachDot >= 0f ? "on the open side" : "behind the flower",
            beakDot, beakDot >= 0f ? "pointing in" : "pointing away",
            relativeDistance, FlowerArea.AreaDiameter));

        return lineIndex;
    }

    private void Draw(int index, Vector3 start, Vector3 end, Color color, float width)
    {
        LineRenderer lr = GetLine(index);
        lr.enabled = true;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
    }

    /// <summary>Pooled: renderers are created once on first use, then reused every frame.</summary>
    private LineRenderer GetLine(int index)
    {
        while (lines.Count <= index)
        {
            var go = new GameObject("DebugLine_" + lines.Count);
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.material = lineMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;

            lines.Add(lr);
        }

        return lines[index];
    }

    private void SetReadout(string text)
    {
        if (readout != null) readout.text = text;
    }

    /// <summary>
    /// Builds its own Canvas at runtime, the same way NectarCounterUI does, so this works in
    /// Training.unity -- which has no Canvas and no EventSystem of its own. Plain text needs no
    /// EventSystem; only interactive UI does.
    /// </summary>
    private void BuildReadout()
    {
        var canvasObj = new GameObject("DebugViewCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above the nectar counter's canvas, so the overlay is never hidden behind it.
        canvas.sortingOrder = 200;

        UiScaling.Apply(canvasObj.AddComponent<CanvasScaler>());

        var textObj = new GameObject("DebugViewText");
        textObj.transform.SetParent(canvasObj.transform, false);

        readout = textObj.AddComponent<Text>();
        readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        readout.fontSize = 22;
        readout.color = Color.white;
        readout.alignment = TextAnchor.UpperLeft;

        // LegacyRuntime.ttf is proportional, so the padded columns above line up roughly rather
        // than perfectly. Good enough to read at a glance, and it avoids shipping a font asset.
        readout.horizontalOverflow = HorizontalWrapMode.Overflow;
        readout.verticalOverflow = VerticalWrapMode.Overflow;

        // White text over sunlit grass and pale rock is close to invisible. Same outline the
        // scoreboard uses, for the same reason -- and cheaper than putting a panel behind it,
        // which would cover the gameplay this readout is meant to explain.
        var outline = textObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(readoutAnchorX, 1f);
        rt.anchorMax = new Vector2(readoutAnchorX, 1f);
        rt.pivot = new Vector2(0f, 1f);
        // Below the race scoreboard, which occupies roughly the top 150 units of each half. At
        // -20 the two overlapped and both became unreadable.
        rt.anchoredPosition = new Vector2(24f, -190f);
        rt.sizeDelta = new Vector2(600f, 320f);
    }
}
