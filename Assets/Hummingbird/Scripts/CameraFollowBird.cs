using UnityEngine;

/// <summary>
/// Makes this camera follow the Hummingbird closely, staying zoomed in
/// so the tiny bird is always easy to see during training.
///
/// The orbit/zoom offsets below are what the phone demo's touch controls drive. They are inert
/// until something calls Orbit/Zoom/ResetView, so scenes with no touch input (Training.unity)
/// behave exactly as they always did.
///
/// This camera is a pure observer: it only ever moves its own transform. The agent has no camera
/// sensor -- its entire input is three ray sensors plus ten computed numbers -- so nothing done
/// here can change what the bird perceives, how it flies, or what it scores.
/// </summary>
public class CameraFollowBird : MonoBehaviour
{
    [Tooltip("How far behind the bird the camera stays")]
    [SerializeField] private float distance = 2f;

    [Tooltip("How high above the bird the camera stays")]
    [SerializeField] private float height = 1f;

    [Tooltip("How quickly the camera catches up to the bird (higher = snappier)")]
    [SerializeField] private float smoothSpeed = 6f;

    [Header("Orbit limits")]
    [Tooltip("How far up/down the view can be swung, in degrees. Kept short of straight up or "
           + "down, where the camera's own up-vector flips and the view rolls over.")]
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 70f;

    [Tooltip("Closest and furthest the view can be pulled, as a multiple of 'distance'. Clamped so "
           + "the viewer can neither end up inside the bird nor lose it against the scenery.")]
    [SerializeField] private float minZoom = 0.25f;
    [SerializeField] private float maxZoom = 8f;

    private Transform target;

    // Viewer-controlled offsets. Deliberately held here rather than derived from the bird, so a
    // respawn (every model switch and every 5000-step episode) moves the bird without throwing
    // away the angle and zoom the viewer chose.
    private float yaw;
    private float pitch;
    private float zoom = 1f;

    /// <summary>
    /// Locks this camera to one specific bird. Needed once more than one bird exists, because the
    /// automatic lookup below uses FindAnyObjectByType, which gives no guarantee about which of
    /// several matching birds it returns. Left unused in the single-bird case.
    /// </summary>
    public void SetTarget(Transform bird)
    {
        target = bird;

        // Snap straight to the framed position instead of lerping in from wherever this camera
        // happened to sit, so a camera enabled mid-game opens already on the bird rather than
        // swooping across the arena to reach it.
        if (bird != null) transform.position = DesiredPosition(bird.position);
    }

    /// <summary>
    /// Copies the framing from another follow camera. Used when a second camera is created at
    /// runtime: these three values are serialized per instance, so a camera built with
    /// AddComponent gets the code defaults (2 / 1 / 6) rather than whatever the scene's own camera
    /// was tuned to, and the two views end up at visibly different distances.
    /// </summary>
    public void CopyFramingFrom(CameraFollowBird other)
    {
        if (other == null) return;

        distance = other.distance;
        height = other.height;
        smoothSpeed = other.smoothSpeed;
    }

    /// <summary>Swings the view around the bird. Degrees, relative to the current angle.</summary>
    public void Orbit(float deltaYaw, float deltaPitch)
    {
        yaw += deltaYaw;
        pitch = Mathf.Clamp(pitch + deltaPitch, minPitch, maxPitch);
    }

    /// <summary>Multiplies the viewing distance. Below 1 moves closer, above 1 pulls back.</summary>
    public void Zoom(float factor)
    {
        zoom = Mathf.Clamp(zoom * factor, minZoom, maxZoom);
    }

    /// <summary>Back to the default over-the-shoulder view.</summary>
    public void ResetView()
    {
        yaw = 0f;
        pitch = 0f;
        zoom = 1f;
    }

    private Vector3 DesiredPosition(Vector3 targetPosition)
    {
        // The default offset, rotated by the viewer's orbit and scaled by their zoom. With yaw,
        // pitch and zoom at their defaults this is exactly the original
        // "position + (0, height, -distance)", so the untouched behaviour is unchanged.
        Vector3 offset = Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, height, -distance);
        return targetPosition + offset * zoom;
    }

    private void LateUpdate()
    {
        // Keep trying to find the bird if we don't have it yet
        // (handles the bird being created after this camera starts)
        if (target == null)
        {
            HummingbirdAgent agent = FindAnyObjectByType<HummingbirdAgent>();
            if (agent != null)
            {
                target = agent.transform;
            }
            else
            {
                return;
            }
        }

        transform.position = Vector3.Lerp(transform.position, DesiredPosition(target.position),
                                          smoothSpeed * Time.deltaTime);
        transform.LookAt(target.position);
    }
}
