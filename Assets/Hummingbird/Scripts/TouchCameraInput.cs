using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Lets the viewer look around with their fingers: drag to swing the view around the bird, pinch
/// to zoom, double-tap to snap back to the default over-the-shoulder shot.
///
/// This only ever moves cameras. The agent has no camera sensor -- its whole input is three ray
/// sensors plus ten numbers derived from transforms -- so nothing here can change how the bird
/// flies or what it scores. The viewer can reframe freely mid-run without invalidating the
/// comparison between the two models.
///
/// Each gesture is routed to whichever camera's viewport contains it, rather than to a single
/// camera. That needs no special case for split screen: with one camera its viewport covers the
/// whole screen and takes every touch, and in split screen the two half-viewports partition the
/// screen between them, so a drag on the left half moves only the left bird's view.
///
/// Uses the legacy Input class, which is available because the project's activeInputHandler is 2
/// ("Both"). The mouse path is compiled for the Editor and the browser build only: on Android
/// Input.mousePosition is also fed by touches, so shipping both there would process every gesture
/// twice. In a phone browser the touch branch in Update() wins for as long as a finger is down.
/// </summary>
public class TouchCameraInput : MonoBehaviour
{
    [Tooltip("Degrees of orbit for a drag across the full width of the screen. Expressed per "
           + "screen-fraction rather than per pixel so it feels the same on any resolution.")]
    [SerializeField] private float orbitDegreesPerScreen = 180f;

    [Tooltip("Seconds within which a second tap counts as a double-tap")]
    [SerializeField] private float doubleTapWindow = 0.3f;

    [Tooltip("How far apart two taps may be and still count as a double-tap, as a fraction of "
           + "screen width. Fingers are imprecise, so this is deliberately generous.")]
    [SerializeField] private float doubleTapSlop = 0.08f;

    [SerializeField] private float scrollZoomSensitivity = 0.1f;

    private Vector2 lastSingleTouch;
    private bool draggingSingle;

    private float lastPinchDistance;

    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;

    private void Update()
    {
        if (Input.touchCount > 0) HandleTouch();
#if UNITY_EDITOR || UNITY_WEBGL
        else HandleMouse();
#endif
    }

    private void HandleTouch()
    {
        if (Input.touchCount >= 2)
        {
            draggingSingle = false;
            HandlePinch(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Began)
        {
            // A drag that starts on a button must not also spin the camera, or every tap of START
            // would fling the view.
            if (IsOverUI(touch.fingerId)) { draggingSingle = false; return; }

            draggingSingle = true;
            lastSingleTouch = touch.position;
            CheckDoubleTap(touch.position);
            return;
        }

        if (!draggingSingle) return;

        if (touch.phase == TouchPhase.Moved)
        {
            OrbitAt(touch.position, touch.position - lastSingleTouch);
            lastSingleTouch = touch.position;
        }
        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
        {
            draggingSingle = false;
        }
    }

    private void HandlePinch(Touch a, Touch b)
    {
        float distance = Vector2.Distance(a.position, b.position);

        // A pinch that starts mid-gesture (a second finger landing during a drag) has no previous
        // distance to compare against, so seed it rather than jumping the zoom.
        if (a.phase == TouchPhase.Began || b.phase == TouchPhase.Began || lastPinchDistance <= 0f)
        {
            lastPinchDistance = distance;
            return;
        }

        if (distance > 0f)
        {
            // Fingers apart -> distance grows -> factor below 1 -> the view moves closer, which is
            // the direction people expect from a pinch-to-zoom.
            ZoomAt((a.position + b.position) * 0.5f, lastPinchDistance / distance);
        }

        lastPinchDistance = distance;
    }

#if UNITY_EDITOR || UNITY_WEBGL
    private void HandleMouse()
    {
        lastPinchDistance = 0f;

        if (Input.GetMouseButtonDown(0))
        {
            if (IsOverUI(-1)) { draggingSingle = false; return; }

            draggingSingle = true;
            lastSingleTouch = Input.mousePosition;
            CheckDoubleTap(Input.mousePosition);
        }
        else if (Input.GetMouseButton(0) && draggingSingle)
        {
            Vector2 position = Input.mousePosition;
            OrbitAt(position, position - lastSingleTouch);
            lastSingleTouch = position;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            draggingSingle = false;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (!Mathf.Approximately(scroll, 0f))
        {
            ZoomAt(Input.mousePosition, 1f - scroll * scrollZoomSensitivity);
        }
    }
#endif

    private void CheckDoubleTap(Vector2 position)
    {
        bool near = Vector2.Distance(position, lastTapPosition) < Screen.width * doubleTapSlop;

        if (Time.unscaledTime - lastTapTime < doubleTapWindow && near)
        {
            var cam = CameraAt(position);
            if (cam != null) cam.ResetView();

            // Consume it, so a third tap does not immediately count as another double.
            lastTapTime = -1f;
            draggingSingle = false;
            return;
        }

        lastTapTime = Time.unscaledTime;
        lastTapPosition = position;
    }

    private void OrbitAt(Vector2 screenPosition, Vector2 delta)
    {
        var cam = CameraAt(screenPosition);
        if (cam == null) return;

        // Screen-relative so the feel is resolution independent. Pitch is inverted so dragging
        // down looks down, which is what a drag on a physical object would do.
        float yaw = delta.x / Screen.width * orbitDegreesPerScreen;
        float pitch = -delta.y / Screen.height * orbitDegreesPerScreen;
        cam.Orbit(yaw, pitch);
    }

    private void ZoomAt(Vector2 screenPosition, float factor)
    {
        var cam = CameraAt(screenPosition);
        if (cam != null) cam.Zoom(factor);
    }

    /// <summary>
    /// Finds the follow camera whose viewport contains this screen point. Camera.rect is in
    /// normalised screen space, so this works unchanged for a full-screen camera and for the two
    /// half-width cameras used by split screen.
    /// </summary>
    private CameraFollowBird CameraAt(Vector2 screenPosition)
    {
        var viewportPoint = new Vector2(screenPosition.x / Screen.width, screenPosition.y / Screen.height);

        var follows = FindObjectsByType<CameraFollowBird>(FindObjectsInactive.Exclude);
        for (int i = 0; i < follows.Length; i++)
        {
            var cam = follows[i].GetComponent<Camera>();
            if (cam != null && cam.enabled && cam.rect.Contains(viewportPoint)) return follows[i];
        }

        return null;
    }

    /// <summary>
    /// EventSystem.current is null in scenes with no UI (Training.unity), so this must not assume
    /// one exists.
    /// </summary>
    private bool IsOverUI(int fingerId)
    {
        if (EventSystem.current == null) return false;
        return fingerId < 0
            ? EventSystem.current.IsPointerOverGameObject()
            : EventSystem.current.IsPointerOverGameObject(fingerId);
    }
}
