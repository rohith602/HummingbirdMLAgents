using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the phone demo: a Start button, then live switching between the two trained
/// models while the bird is flying.
///
/// The whole reason this class exists is that the two models are NOT interchangeable.
/// They were trained with different observation stacking, so each one only accepts a
/// specific input width:
///
///     RentedGPU_Final (HB_16)  obs_3 [batch, 30]  -> Stacked Vectors 3
///     OnDevice_Final  (HB_01)  obs_3 [batch, 10]  -> Stacked Vectors 1
///
/// ML-Agents builds its sensors once, in Agent.InitializeSensors() during
/// LazyInitialize(), and assigning BehaviorParameters.Model does not rebuild them.
/// Setting the fields on a live agent therefore leaves the OLD sensor in place feeding
/// the new model the wrong number of observations — and ML-Agents logs no error at all
/// when that happens, so it looks like the model itself is broken.
///
/// Agent.OnDisable() calls CleanupSensors() and sets m_Initialized = false, so a
/// disable -> change -> enable cycle does rebuild them. That is what SelectModel does,
/// and it is why the two fields are only ever written inside that one method: there is
/// no code path here that can set a model without also setting its matching stacking.
/// </summary>
public class DemoController : MonoBehaviour
{
    [Header("Agent")]
    [Tooltip("The Hummingbird GameObject. Leave it DISABLED in the scene — it is enabled on Start.")]
    [SerializeField] private GameObject bird;

    [Header("Models (each is locked to its own Stacked Vectors value)")]
    [Tooltip("HB_01 baseline, mean reward 41.29 — requires Stacked Vectors 1")]
    [SerializeField] private ModelAsset onDeviceModel;

    [Tooltip("HB_16 rented-GPU model, mean reward 96.58 — requires Stacked Vectors 3")]
    [SerializeField] private ModelAsset rentedGpuModel;

    [Header("UI")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject inGamePanel;
    [SerializeField] private Text modelLabel;

    /// <summary>
    /// The bird this controller owns. Exposed so DemoToggles' Reset can restart every *other*
    /// bird's episode without restarting this one twice, since SelectModel already ends its own.
    /// </summary>
    public GameObject Bird { get { return bird; } }

    private BehaviorParameters behaviourParameters;

    private void Awake()
    {
        behaviourParameters = bird.GetComponent<BehaviorParameters>();

        // Belt and braces: the bird should already be disabled in the scene, but if it
        // is not, disabling it here still happens before it can build any sensors,
        // because a disabled GameObject's Awake/OnEnable never ran in the first place.
        bird.SetActive(false);

        if (startPanel != null) startPanel.SetActive(true);
        if (inGamePanel != null) inGamePanel.SetActive(false);
    }

    /// <summary>Hooked to the Start button.</summary>
    public void OnStartPressed()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (inGamePanel != null) inGamePanel.SetActive(true);

        // Open on the better model, so the first thing anyone sees is the good one.
        SelectModel(true);
    }

    /// <summary>Hooked to the "Rented GPU" button.</summary>
    public void UseRentedGpu()
    {
        SelectModel(true);
    }

    /// <summary>Hooked to the "On-Device" button.</summary>
    public void UseOnDevice()
    {
        SelectModel(false);
    }

    /// <summary>
    /// The only place either field is written. Both always change together, inside the
    /// disable/enable cycle that forces ML-Agents to rebuild the observation sensors.
    /// </summary>
    public void SelectModel(bool rented)
    {
        bird.SetActive(false);   // OnDisable -> CleanupSensors(), m_Initialized = false

        behaviourParameters.BrainParameters.NumStackedVectorObservations = rented ? 3 : 1;
        behaviourParameters.Model = rented ? rentedGpuModel : onDeviceModel;

        bird.SetActive(true);    // OnEnable -> LazyInitialize() -> InitializeSensors()

        // Respawn rather than resuming wherever the bird happened to be left, so each
        // model is judged from a comparable starting point.
        bird.GetComponent<Agent>().EndEpisode();

        if (modelLabel != null)
        {
            modelLabel.text = rented
                ? "Rented GPU  —  trained reward 96.58"
                : "On-Device  —  trained reward 41.29";
        }
    }
}
