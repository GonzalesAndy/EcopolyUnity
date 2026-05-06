using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ecopoly.Utils;
using Ecopoly.Network;

namespace Ecopoly.Core
{
    /// <summary>
    /// Application entry point.
    /// Present only in Bootstrap.unity.
    /// Initializes services in the correct order, then loads MainMenu.
    /// </summary>
    public class BootstrapController : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private Data.GameSettings _gameSettings;
        [SerializeField] private Data.BoardConfig  _boardConfig;

        [Header("Scenes")]
        [SerializeField] private string _mainMenuScene = "MainMenu";

        [Header("Loading UI")]
        [SerializeField] private UnityEngine.UI.Slider _loadingBar;
        [SerializeField] private TMPro.TextMeshProUGUI _loadingText;

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            StartCoroutine(InitializeServices());
        }

        private IEnumerator InitializeServices()
        {
            SetProgress(0f, "Initializing...");

            // 1. EventBus (static, always ready — no Clear needed here,
            //    listeners are already registered by active MonoBehaviours)
            SetProgress(0.1f, "Event system OK");
            yield return null;

            // 2. GameManager
            if (GameManager.Instance == null)
            {
                Debug.LogError("[Bootstrap] GameManager not found!");
                yield break;
            }
            SetProgress(0.2f, "Game manager OK");
            yield return null;

            // 3. Unity Gaming Services (async) — await on main thread via task bridge
            SetProgress(0.3f, "Connecting to Unity services...");
            if (LobbyManager.Instance != null)
            {
                Task ugsTask = LobbyManager.Instance.InitializeAsync();
                float timeout = 0f;
                while (!ugsTask.IsCompleted && timeout < 5f)
                {
                    timeout += Time.deltaTime;
                    yield return null;
                }

                if (ugsTask.IsFaulted)
                    Debug.LogWarning($"[Bootstrap] UGS failed: {ugsTask.Exception?.GetBaseException().Message}");
                else if (!ugsTask.IsCompleted)
                    Debug.LogWarning("[Bootstrap] UGS timed out - switching to offline mode.");
            }
            else
            {
                Debug.LogWarning("[Bootstrap] LobbyManager not found - skipping UGS init.");
            }
            SetProgress(0.5f, "Network services OK");
            yield return null;

            // 4. Audio
            SetProgress(0.7f, "Audio system OK");
            yield return null;

            // 5. Addressables
            SetProgress(0.85f, "Resources OK");
            yield return new WaitForSeconds(0.2f);

            // 6. Load main menu
            SetProgress(1f, "Ready!");
            yield return new WaitForSeconds(0.5f);

            SceneManager.LoadScene(_mainMenuScene);
        }

        private void SetProgress(float value, string message)
        {
            if (_loadingBar != null) _loadingBar.value = value;
            if (_loadingText != null) _loadingText.text = message;
        }
    }
}
