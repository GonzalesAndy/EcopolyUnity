using UnityEngine;
using Ecopoly.Utils;
using Ecopoly.Data;
using DistantLands.Cozy;
using DistantLands.Cozy.Data;

namespace Ecopoly.Audio
{
    /// <summary>
    /// Centralized audio manager.
    /// FMOD wrapper: all audio calls go through this manager.
    /// If FMOD is not installed, paths are silently ignored.
    ///
    /// Suggested FMOD event paths (to create in FMOD Studio):
    ///   event:/SFX/Dice/Roll
    ///   event:/SFX/Money/Receive
    ///   event:/SFX/Money/Pay
    ///   event:/SFX/Card/Draw
    ///   event:/SFX/Disaster/{DisasterType}
    ///   event:/Music/Board/Ambient
    ///   event:/Music/Board/Tension  (increases with CEP level)
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("FMOD Parameters")]
        [Tooltip("Global FMOD parameter tied to intensity level (0–4)")]
        [SerializeField] private string _fmodIntensityParam = "IntensityLevel";

        [Tooltip("FMOD event path for ambient music")]
        [SerializeField] private string _fmodAmbientEvent = "event:/Music/Board/Ambient";

        [Header("Cozy Ambiance")]
        [SerializeField] private AmbienceProfile _startingAmbienceProfile;
        [SerializeField] private bool _forceCozyAmbianceOnStartup = true;

        [Header("Unity Audio Fallback")]
        [Tooltip("Fallback AudioSource used when FMOD is not installed.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Ambient music clip played on loop.")]
        [SerializeField] private AudioClip _ambientMusicClip;
        [Tooltip("Clip played when dice are rolled.")]
        [SerializeField] private AudioClip _diceRollClip;
        [Tooltip("Clip played on property purchase.")]
        [SerializeField] private AudioClip _purchaseClip;
        [Tooltip("Clip played when a card is drawn.")]
        [SerializeField] private AudioClip _cardDrawnClip;
        [Tooltip("Clip played when a disaster is triggered.")]
        [SerializeField] private AudioClip _disasterTriggeredClip;
        [Tooltip("Clip played when rent is paid.")]
        [SerializeField] private AudioClip _payRentClip;
        [Tooltip("Clip played when money is received (rent, GO bonus, etc.).")]
        [SerializeField] private AudioClip _receiveMoneyClip;
        [Tooltip("Clip played when a player passes GO.")]
        [SerializeField] private AudioClip _passedGoClip;
        [Tooltip("Clip played when a player is sent to jail.")]
        [SerializeField] private AudioClip _jailedClip;
        [Tooltip("Clip played when a player is released from jail.")]
        [SerializeField] private AudioClip _releasedFromJailClip;
        [Tooltip("Clip played when the global CEP intensity level changes.")]
        [SerializeField] private AudioClip _thresholdChangedClip;
        [Tooltip("Clip played at game over.")]
        [SerializeField] private AudioClip _gameOverClip;

        // Persistent ambient music instance
    #if UNITY_FMOD
        private FMOD.Studio.EventInstance _ambientInstance;
    #endif
        private bool _fmodAvailable;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            // DontDestroyOnLoad requires a root GameObject; detach first if parented.
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            CheckFMODAvailability();
        }

        private void CheckFMODAvailability()
        {
#if UNITY_FMOD
            _fmodAvailable = true;
#else
                _fmodAvailable = false;
                Debug.Log("[Audio] FMOD not available — audio disabled.");
#endif
        }

        private void OnEnable()
        {
            EventBus.On(GameEvent.GameStarted, OnGameStarted);
            EventBus.On(GameEvent.DiceRolled, OnDiceRolled);
            EventBus.On(GameEvent.ChanceCardDrawn, OnCardDrawn);
            EventBus.On(GameEvent.EventCardDrawn, OnCardDrawn);
            EventBus.On(GameEvent.RentPaid, OnRentPaid);
            EventBus.On(GameEvent.PropertyPurchased, OnPropertyPurchased);
            EventBus.On(GameEvent.PlayerPassedGo, OnPlayerPassedGo);
            EventBus.On(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.On(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
            EventBus.On(GameEvent.PlayerBankrupt, OnPlayerBankrupt);
            EventBus.On(GameEvent.PlayerCEPMaxReached, OnPlayerCEPMaxReached);
            EventBus.On(GameEvent.DilemmaCardResolved, OnDilemmaCardResolved);
            EventBus.On(GameEvent.DisasterTriggered, OnDisasterTriggered);
            EventBus.On(GameEvent.DisasterResolved, OnDisasterResolved);
            EventBus.On(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.On(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.On(GameEvent.GlobalGameOver, OnGameOver);
        }

        private void OnDisable()
        {
            EventBus.Off(GameEvent.GameStarted, OnGameStarted);
            EventBus.Off(GameEvent.DiceRolled, OnDiceRolled);
            EventBus.Off(GameEvent.ChanceCardDrawn, OnCardDrawn);
            EventBus.Off(GameEvent.EventCardDrawn, OnCardDrawn);
            EventBus.Off(GameEvent.RentPaid, OnRentPaid);
            EventBus.Off(GameEvent.PropertyPurchased, OnPropertyPurchased);
            EventBus.Off(GameEvent.PlayerPassedGo, OnPlayerPassedGo);
            EventBus.Off(GameEvent.PlayerJailed, OnPlayerJailed);
            EventBus.Off(GameEvent.PlayerReleasedFromJail, OnPlayerReleasedFromJail);
            EventBus.Off(GameEvent.PlayerBankrupt, OnPlayerBankrupt);
            EventBus.Off(GameEvent.PlayerCEPMaxReached, OnPlayerCEPMaxReached);
            EventBus.Off(GameEvent.DilemmaCardResolved, OnDilemmaCardResolved);
            EventBus.Off(GameEvent.DisasterTriggered, OnDisasterTriggered);
            EventBus.Off(GameEvent.DisasterResolved, OnDisasterResolved);
            EventBus.Off(GameEvent.GlobalCEPThresholdChanged, OnThresholdChanged);
            EventBus.Off(GameEvent.PlayerEliminated, OnPlayerEliminated);
            EventBus.Off(GameEvent.GlobalGameOver, OnGameOver);
        }

        // --- Event Handlers ---
        private void OnGameStarted(object _)
        {
            SetStartingAmbiance();
            PlayAmbient();
        }

        private void OnDiceRolled(object _)
        {
            if (_fmodAvailable) 
            {
                PlayOneShot("event:/SFX/Dice/Roll");
            }
            else if (_audioSource != null && _diceRollClip != null)
            {
                _audioSource.PlayOneShot(_diceRollClip);
            }
        }

        private void OnCardDrawn(object _)
        {
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/Card/Draw");
            }
            else if (_audioSource != null && _cardDrawnClip != null)
            {
                _audioSource.PlayOneShot(_cardDrawnClip);
            }
        }

        private void OnRentPaid(object payload)
        {
            if (payload is RentPayload rent)
            {
                if (_fmodAvailable)
                {
                    PlayOneShot(rent.Amount > 0 ? "event:/SFX/Money/Pay" : "event:/SFX/Money/Receive");
                }
                else if (_audioSource != null)
                {
                    AudioClip clip = rent.Amount > 0 ? _payRentClip : _receiveMoneyClip;
                    if (clip != null)
                    {
                        _audioSource.PlayOneShot(clip);
                    }
                }
            }
        }

        private void OnPropertyPurchased(object _)
        {
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/Money/Pay");
            }
            else if (_audioSource != null && _purchaseClip != null)
            {
                _audioSource.PlayOneShot(_purchaseClip);
            }
        }

        private void OnPlayerPassedGo(object _)
        {
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/Money/Receive");
            }
            else if (_audioSource != null && _passedGoClip != null)
            {
                _audioSource.PlayOneShot(_passedGoClip);
            }
        }

        private void OnPlayerJailed(object _)
        {
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/Player/Jailed");
            }
            else if (_audioSource != null && _jailedClip != null)
            {
                _audioSource.PlayOneShot(_jailedClip);
            }
        }

        private void OnPlayerReleasedFromJail(object _)
        {
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/Player/Released");
            }
            else if (_audioSource != null && _releasedFromJailClip != null)
            {
                _audioSource.PlayOneShot(_releasedFromJailClip);
            }
        }

        private void OnPlayerBankrupt(object _)
        {
            PlayOneShot("event:/SFX/Player/Bankrupt");
        }

        private void OnPlayerCEPMaxReached(object _)
        {
            PlayOneShot("event:/SFX/Player/Eliminated");
        }

        private void OnDilemmaCardResolved(object payload)
        {
            if (!(payload is bool paidByAll)) return;
            PlayOneShot(paidByAll ? "event:/SFX/UI/Success" : "event:/SFX/UI/Warning");
        }

        private void OnDisasterTriggered(object payload)
        {
            if (!(payload is DisasterEventPayload disaster)) return;
            
            if (_fmodAvailable)
            {
                // The FMOD path is defined in EventCardData.fmodEventPath
                // Retrieved via CardManager or supplied in the payload
                // Placeholder: generic path
                PlayOneShot($"event:/SFX/Disaster/{disaster.DisasterId}");
            }
            else if (_audioSource != null && _disasterTriggeredClip != null)
            {
                _audioSource.PlayOneShot(_disasterTriggeredClip);
            }
        }

        private void OnDisasterResolved(object _)
        {
            PlayOneShot("event:/SFX/Disaster/Resolved");
        }

        private void OnThresholdChanged(object payload)
        {
            if (!(payload is int level)) return;
            
            if (_fmodAvailable)
            {
                SetGlobalParameter(_fmodIntensityParam, level);
            }
            else if (_audioSource != null && _thresholdChangedClip != null)
            {
                _audioSource.PlayOneShot(_thresholdChangedClip);
            }
        }

        private void OnPlayerEliminated(object _)
        {
            PlayOneShot("event:/SFX/Player/Eliminated");
        }

        private void OnGameOver(object _)
        {
            StopAmbient();
            
            if (_fmodAvailable)
            {
                PlayOneShot("event:/SFX/GameOver");
            }
            else if (_audioSource != null && _gameOverClip != null)
            {
                _audioSource.PlayOneShot(_gameOverClip);
            }
        }

        // --- Cozy Ambiance ---
        private void SetStartingAmbiance()
        {
            if (!_forceCozyAmbianceOnStartup)
                return;

            if (CozyWeather.instance == null)
            {
                Debug.LogWarning("[AudioManager] CozyWeather instance not found in scene.");
                return;
            }

            if (!CozyWeather.instance.GetModule(out CozyAmbienceModule ambienceModule))
            {
                Debug.LogWarning("[AudioManager] CozyAmbienceModule not found in CozyWeather.");
                return;
            }

            if (_startingAmbienceProfile != null)
            {
                ambienceModule.SetAmbience(_startingAmbienceProfile);
                Debug.Log($"[AudioManager] Set ambiance to: {_startingAmbienceProfile.name}");
            }
            else
            {
                Debug.LogWarning("[AudioManager] No starting ambiance profile assigned. Assign a profile in the inspector.");
            }
        }

        // --- FMOD Helpers ---
        private void PlayAmbient()
        {
            if (_fmodAvailable)
            {
#if UNITY_FMOD
                _ambientInstance = FMODUnity.RuntimeManager.CreateInstance(_fmodAmbientEvent);
                _ambientInstance.start();
#endif
            }
            else if (_audioSource != null && _ambientMusicClip != null)
            {
                _audioSource.clip = _ambientMusicClip;
                _audioSource.loop = true;
                _audioSource.Play();
            }
        }

        private void StopAmbient()
        {
            if (_fmodAvailable)
            {
#if UNITY_FMOD
                _ambientInstance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
                _ambientInstance.release();
#endif
            }
            else if (_audioSource != null)
            {
                _audioSource.Stop();
            }
        }

        public void PlayOneShot(string fmodPath, Vector3 position = default)
        {
            if (!_fmodAvailable || string.IsNullOrEmpty(fmodPath)) return;
#if UNITY_FMOD
            FMODUnity.RuntimeManager.PlayOneShot(fmodPath, position);
#endif
        }

        public void SetGlobalParameter(string paramName, float value)
        {
            if (!_fmodAvailable) return;
#if UNITY_FMOD
            FMODUnity.RuntimeManager.StudioSystem.setParameterByName(paramName, value);
#endif
        }

        private void OnApplicationQuit()
        {
            StopAmbient();
        }
    }
}
