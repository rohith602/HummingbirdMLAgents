using Unity.MLAgents;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The phone demo's control bar: Debug, Trails, Split and Reset.
///
/// The buttons are built in code rather than authored into Demo.unity, following the pattern
/// already used by NectarCounterUI and by the scoreboard and readout elsewhere in this project.
/// That keeps a four-button layout out of the scene's YAML and means the buttons call methods
/// directly instead of through serialized UnityEvents, which are easy to wire wrong and silent
/// when they are.
///
/// It also finds its own collaborators at runtime, so adding this component to the scene is the
/// entire integration -- there are no Inspector references to forget.
/// </summary>
public class DemoToggles : MonoBehaviour
{
    [Tooltip("How often to re-check whether a bird is flying yet. The bar stays hidden until "
           + "START, and this is far cheaper than searching the 3,800-object island every frame.")]
    [SerializeField] private float visibilityCheckInterval = 0.4f;

    // One shared row of six: [On-Device] [Rented GPU] [Debug] [Trails] [Split] [Reset]. The first
    // two are authored in Demo.unity and repositioned to match these numbers; the last four are
    // built here.
    //
    // Measured UP FROM THE BOTTOM EDGE, not from the canvas centre. The canvas's height changes
    // with the viewport's aspect ratio, so a centre-relative y that sits just above the bottom in
    // the Editor falls off the bottom of a phone screen -- which is exactly what hid this row.
    public const float RowBottomOffset = 90f;
    public const float ButtonWidth = 290f;
    public const float ButtonHeight = 110f;
    private const float ButtonGap = 14f;
    private const int SlotCount = 6;

    /// <summary>Centre x of slot i (0-5) in the shared button row.</summary>
    public static float SlotX(int slot)
    {
        float step = ButtonWidth + ButtonGap;
        float firstX = -(SlotCount - 1) * step * 0.5f;
        return firstX + slot * step;
    }

    private GameObject bar;
    private Text splitLabel;
    private Text debugLabel;
    private Text trailsLabel;

    private RaceController race;
    private DemoController demo;

    private float nextVisibilityCheck;

    private void Start()
    {
        race = FindAnyObjectByType<RaceController>();
        demo = FindAnyObjectByType<DemoController>();

        BuildBar();

        // Hidden until START, matching the existing in-game panel.
        bar.SetActive(false);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextVisibilityCheck) return;
        nextVisibilityCheck = Time.unscaledTime + visibilityCheckInterval;

        // "Is a bird flying yet" is the same question as "has START been pressed" in Demo.unity,
        // and is true from the start in Training.unity -- so this needs no scene-specific wiring.
        bool anyBirdFlying = FindAnyObjectByType<HummingbirdAgent>() != null;
        if (bar.activeSelf != anyBirdFlying) bar.SetActive(anyBirdFlying);

        if (anyBirdFlying) RefreshLabels();
    }

    private void RefreshLabels()
    {
        debugLabel.text = RaySensorVisualizer.Visible ? "Debug: ON" : "Debug";
        trailsLabel.text = FlightTrail.Visible ? "Trails: ON" : "Trails";
        splitLabel.text = (race != null && race.IsRacing) ? "Split: ON" : "Split";
    }

    // ---- button actions -------------------------------------------------------------------

    public void OnDebugPressed()
    {
        RaySensorVisualizer.ToggleAll();
    }

    public void OnTrailsPressed()
    {
        FlightTrail.ToggleAll();
    }

    public void OnSplitPressed()
    {
        if (race != null) race.ToggleRace();
    }

    /// <summary>
    /// Puts the whole demo back to its opening state in one tap, for when it has been poked at:
    /// camera angles, toggles, the loaded model and both scores. Applies to both birds when split
    /// screen is on.
    ///
    /// Deliberately leaves split screen itself alone -- that is the mode the viewer chose, and
    /// turning it off here would only mean pressing Split again straight away.
    /// </summary>
    public void OnResetPressed()
    {
        var cameras = FindObjectsByType<CameraFollowBird>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cameras.Length; i++) cameras[i].ResetView();

        RaySensorVisualizer.SetAll(false);
        FlightTrail.SetAll(true);

        // Back to the better model. This also restarts the main bird's episode, because
        // SelectModel ends it -- so its score returns to zero along with everything else.
        if (demo != null) demo.SelectModel(true);

        // Any other bird (the challenger) has to be restarted separately, since SelectModel only
        // touches the one it owns.
        var agents = FindObjectsByType<HummingbirdAgent>(FindObjectsInactive.Exclude);
        for (int i = 0; i < agents.Length; i++)
        {
            if (demo != null && agents[i].gameObject == demo.Bird) continue;
            agents[i].EndEpisode();
        }
    }

    // ---- UI construction ------------------------------------------------------------------

    private void BuildBar()
    {
        var canvasObj = new GameObject("DemoTogglesCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        UiScaling.Apply(canvasObj.AddComponent<CanvasScaler>());
        canvasObj.AddComponent<GraphicRaycaster>();

        bar = new GameObject("ToggleBar");
        bar.transform.SetParent(canvasObj.transform, false);
        var barRect = bar.AddComponent<RectTransform>();

        // Pinned to the bottom edge so the row cannot slide off screen when the aspect ratio
        // changes. The authored On-Device / Rented GPU buttons are anchored the same way, to the
        // same offset, so all six share one row.
        UiScaling.AnchorToBottom(barRect, 0f, RowBottomOffset);
        barRect.sizeDelta = new Vector2(UiScaling.ReferenceWidth, ButtonHeight);

        // The two authored buttons take the first two slots; these take the remaining four.
        debugLabel = AddButton("Debug", SlotX(2), OnDebugPressed);
        trailsLabel = AddButton("Trails", SlotX(3), OnTrailsPressed);
        splitLabel = AddButton("Split", SlotX(4), OnSplitPressed);
        AddButton("Reset", SlotX(5), OnResetPressed);
    }

    private Text AddButton(string label, float x, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(label + "Button");
        go.transform.SetParent(bar.transform, false);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(go.transform, false);

        var text = textObj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 34;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = label;

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return text;
    }
}
