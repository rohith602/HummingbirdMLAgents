using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creates a simple on-screen counter that increases every time the
/// hummingbird touches nectar. Builds its own UI automatically at
/// runtime, so it works in both Scene view (while playing) and Game view.
/// </summary>
public class NectarCounterUI : MonoBehaviour
{
    public static NectarCounterUI Instance { get; private set; }

    private int touchCount = 0;
    private Text counterText;

    private void Awake()
    {
        Instance = this;
        BuildUI();
    }

    private void BuildUI()
    {
        GameObject canvasObj = new GameObject("NectarCounterCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        UiScaling.Apply(canvasObj.AddComponent<CanvasScaler>());

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject textObj = new GameObject("NectarCounterText");
        textObj.transform.SetParent(canvasObj.transform, false);
        counterText = textObj.AddComponent<Text>();
        counterText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        counterText.fontSize = 36;
        counterText.fontStyle = FontStyle.Bold;
        counterText.color = new Color(1f, 0f, 0.3f); // matches the flower's full-nectar color
        counterText.alignment = TextAnchor.MiddleCenter;
        counterText.text = "Nectar Touches: 0";

        RectTransform rt = textObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 400f);
        rt.sizeDelta = new Vector2(600f, 80f);
    }

    /// <summary>
    /// Shows or hides the counter. Used to hide it during split screen: this is a single static
    /// Instance shared by every bird, so with two racers feeding it would show their combined
    /// total, which reads as one bird doing suspiciously well. The race scoreboard reports the two
    /// scores separately instead.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (counterText != null) counterText.gameObject.SetActive(visible);
    }

    // Called from HummingbirdAgent whenever the beak actually touches nectar
    public void RegisterTouch()
    {
        touchCount++;
        if (counterText != null)
        {
            counterText.text = "Nectar Touches: " + touchCount;
        }
    }
}
