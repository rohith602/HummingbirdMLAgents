using UnityEngine;

/// <summary>
/// Draws a fading ribbon behind the bird so its flight path is visible, not just its position.
///
/// This exists because the two trained models fail in visibly different shapes, and a still frame
/// cannot show that. RentedGPU_Final draws clean arcs between flowers. OnDevice_Final draws loops
/// and overshoots, because it was trained without any observation of its own velocity and can only
/// do proportional control on a body with real momentum (mass 1, linear damping 2). The trail turns
/// that finding into something you can see rather than something the report has to assert.
///
/// The TrailRenderer and its material are built in code rather than authored on the prefab, so no
/// Material asset has to be created and kept in the project for one debug ribbon.
/// </summary>
public class FlightTrail : MonoBehaviour
{
    /// <summary>
    /// Whether trails should be showing, shared by every bird. Static because the phone's Trails
    /// button has to affect both racers at once, and because the challenger bird does not exist
    /// until split screen is switched on -- it reads this in Start so it wakes up already matching
    /// the other bird instead of at its own default.
    /// </summary>
    public static bool Visible = true;

    [Tooltip("Whether trails start visible. Runtime state is shared across all birds and can be "
           + "changed at any time with the key below or the phone's Trails button.")]
    [SerializeField] private bool showTrailOnStart = true;

    [Tooltip("Press to show/hide trails on every bird at once.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Y;

    [Tooltip("Seconds before a point on the trail fades out")]
    [SerializeField] private float trailTime = 2.5f;

    [SerializeField] private float startWidth = 0.06f;
    [SerializeField] private float endWidth = 0.005f;

    [SerializeField] private Color trailColor = new Color(1f, 0.85f, 0.2f, 0.9f);

    [Tooltip("A single frame's movement larger than this is treated as a teleport, not flight. "
           + "The agent is repositioned anywhere in the 20-unit arena at the start of every "
           + "episode, and without this the trail would draw a straight streak across the map.")]
    [SerializeField] private float teleportDistance = 0.5f;

    // Every bird sees the same keypress in the same frame. Without this guard two birds would each
    // call ToggleAll, flipping the shared flag twice and leaving it unchanged -- a toggle that
    // silently stops working the moment split screen is switched on.
    private static int lastToggleFrame = -1;

    private static bool initialised;

    private TrailRenderer trail;
    private Vector3 lastPosition;

    private void Start()
    {
        // The first bird to start decides the opening state from its own Inspector value; every
        // bird after that adopts whatever the shared flag already holds.
        if (!initialised)
        {
            initialised = true;
            Visible = showTrailOnStart;
        }

        // Built even when hidden, so the button and key can switch it on later. It is the
        // TrailRenderer's own enabled flag, not its existence, that decides visibility.
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.startWidth = startWidth;
        trail.endWidth = endWidth;

        // Small, so tight turns render as curves rather than visibly straight segments.
        trail.minVertexDistance = 0.02f;

        // Sprites/Default respects the vertex colours below and renders under URP.
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        SetColor(trailColor);

        trail.enabled = Visible;
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (!Input.GetKeyDown(toggleKey) || lastToggleFrame == Time.frameCount) return;

        lastToggleFrame = Time.frameCount;
        ToggleAll();
    }

    /// <summary>Flips trails on every bird. Called by the key above and the phone's Trails button.</summary>
    public static void ToggleAll()
    {
        SetAll(!Visible);
    }

    public static void SetAll(bool visible)
    {
        Visible = visible;

        var trails = FindObjectsByType<FlightTrail>(FindObjectsInactive.Include);
        for (int i = 0; i < trails.Length; i++) trails[i].Apply();
    }

    private void Apply()
    {
        if (trail == null) return;

        trail.enabled = Visible;

        // A hidden TrailRenderer stops recording, so switching it back on would otherwise resume
        // from wherever the bird was when it was hidden and draw one long streak across the arena
        // to catch up. Clearing starts the ribbon fresh from the bird's current position.
        if (Visible) trail.Clear();
    }

    private void LateUpdate()
    {
        if (trail == null) return;

        // Clear on respawn. Checked in LateUpdate so it runs after the episode reset has already
        // moved the transform, and cleared before the renderer draws the frame.
        if ((transform.position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance)
        {
            trail.Clear();
        }

        lastPosition = transform.position;
    }

    /// <summary>
    /// Recolours this bird's trail. Used by RaceController to give the two racers contrasting
    /// ribbons so it stays obvious which one is which model.
    /// </summary>
    public void SetColor(Color color)
    {
        trailColor = color;
        if (trail == null) return;

        trail.startColor = color;

        // Fade the tail out rather than cutting it off, so the ribbon reads as direction of travel.
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
    }
}
