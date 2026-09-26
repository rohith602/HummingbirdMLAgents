using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One canonical CanvasScaler setup, shared by every canvas in the project.
///
/// This exists because getting it wrong is invisible until it is too late. Five separate canvases
/// build themselves at runtime here (the toggle bar, the race scoreboard, the nectar counter and
/// one debug readout per bird), and a bare `AddComponent&lt;CanvasScaler&gt;()` leaves them on
/// ConstantPixelSize -- which looks fine in a 1920-wide Editor Game view and is unreadably small on
/// a 3120-wide phone.
///
/// Worse, even two canvases that both say "ScaleWithScreenSize, 1920x1080" resolve to *different*
/// canvas sizes if their matchWidthOrHeight differs: the authored DemoCanvas uses 0.5, the default
/// is 0, and at a 1920x862 viewport that produced canvases of 2150x965 and 1920x862. The same
/// y coordinate then lands in two different places, which is why buttons meant to share one row
/// ended up off the bottom of the screen.
///
/// So: every canvas goes through here, and nothing sets these fields by hand.
/// </summary>
public static class UiScaling
{
    public const float ReferenceWidth = 1920f;
    public const float ReferenceHeight = 1080f;

    // Matches the authored DemoCanvas. Balancing width and height keeps the layout sane across the
    // Editor's letterboxed aspect and the phone's much wider one.
    private const float MatchWidthOrHeight = 0.5f;

    public static void Apply(CanvasScaler scaler)
    {
        if (scaler == null) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = MatchWidthOrHeight;
    }

    /// <summary>
    /// Anchors a rect to the bottom edge of the screen, a fixed distance up.
    ///
    /// Anything that must stay on screen has to be anchored to an edge rather than positioned from
    /// the centre: the canvas's height changes with the viewport's aspect ratio, so a centre-relative
    /// y that sits just above the bottom in the Editor drops off the bottom on a phone. This is the
    /// bug that made the button row invisible.
    /// </summary>
    public static void AnchorToBottom(RectTransform rect, float x, float distanceFromBottom)
    {
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, distanceFromBottom);
    }
}
