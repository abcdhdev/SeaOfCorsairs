using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace GameSystem
{
    public class AppSettings : MonoBehaviour
    {
        private const string AppSettingsResourcePath = "AppSettings";

        private enum MusicContext
        {
            Unknown,
            Menu,
            Gameplay
        }

        public enum RenderRes
        {
            _Native,
            _1440p,
            _1080p,
            _720p
        }

        public enum Framerate
        {
            _30,
            _60,
            _120
        }

        public enum SpeedFormat
        {
            _Kph,
            _Mph
        }

        public static AppSettings Instance;
        private GameObject loadingScreenObject;
        public static Camera MainCamera;
        [Header("Resolution Settings")]
        public RenderRes maxRenderSize = RenderRes._720p;
        public bool variableResolution;
        [Range(0f, 1f)]
        public float axisBias = 0.5f;
        public float minScale = 0.5f;
        public Framerate targetFramerate = Framerate._30;
        private float currentDynamicScale = 1.0f;
        private float maxScale = 1.0f;
        public SpeedFormat speedFormat = SpeedFormat._Mph;

        [Header("Asset References")]
        public AssetReference loadingScreen;
        public AssetReference volumeManager;
        [Header("Prefabs")]
        public GameObject consoleCanvas;
        public static GameObject ConsoleCanvas;

        [Header("Music")]
        [SerializeField] private AudioClip menuMusicClip;
        [SerializeField] private AudioClip gameplayMusicClip;
        [SerializeField] private AudioClip combatMusicClip;
        [SerializeField, Range(0f, 1f)] private float baseMusicVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float combatMusicVolume = 0.5f;
        [SerializeField, Min(0.05f)] private float sceneMusicFadeSeconds = 1.5f;
        [SerializeField, Min(0.05f)] private float combatFadeInSeconds = 3f;
        [SerializeField, Min(0.05f)] private float combatFadeOutSeconds = 2f;
        [SerializeField, Min(0f)] private float combatHoldSeconds = 1.5f;
        
        [SerializeField] public string urpVersion;
        public static string UrpVersion { get { return Instance.urpVersion; } }

        private AudioSource baseMusicSource;
        private AudioSource combatMusicSource;
        private Coroutine baseMusicTransitionRoutine;
        private MusicContext currentMusicContext = MusicContext.Unknown;
        private bool wasCombatActive;
        private float combatEndTimestamp = -100f;

        // Use this for initialization
        [RuntimeInitializeOnLoadMethod]
        private static void RuntimeInitializeOnLoad()
        {
            var found = FindObjectsByType<AppSettings>(FindObjectsSortMode.None);
            if (found.Length > 1)
            {
                Debug.LogWarning($"Found {found.Length} AppSettings instances. Using the first one.");
            }

            if (found.Length == 0)
            {
                AppSettings prefab = Resources.Load<AppSettings>(AppSettingsResourcePath);
                if (prefab == null)
                {
                    Debug.LogError($"Missing Resources/{AppSettingsResourcePath} prefab. Audio and global app settings will not initialize.");
                    return;
                }

                Instance = Instantiate(prefab);
            }
            else
            {
                Instance = found[0];
            }

            DontDestroyOnLoad(Instance.gameObject);
            MainCamera = Camera.main;
            SceneManager.sceneLoaded -= LevelWasLoaded;
            SceneManager.sceneLoaded += LevelWasLoaded;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate AppSettings detected. Destroying the extra instance.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            MainCamera = Camera.main;

            if(Debug.isDebugBuild)
                Debug.Log("AppManager initializing");
            Initialize();
            CmdArgs();
            SetRenderScale();
        }
        
        private void Initialize()
        {
            if (consoleCanvas != null)
            {
                ConsoleCanvas = Instantiate(consoleCanvas);
                DontDestroyOnLoad(ConsoleCanvas);
            }
            else
            {
                ConsoleCanvas = null;
            }

            InitializeMusic();
#if UNITY_EDITOR
            urpVersion = Utility.GetURPPackageVersion();
#endif
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= LevelWasLoaded;
            if (baseMusicTransitionRoutine != null)
            {
                StopCoroutine(baseMusicTransitionRoutine);
                baseMusicTransitionRoutine = null;
            }
        }

        private static void LevelWasLoaded(Scene scene, LoadSceneMode mode)
        {
            CleanupCameras();
#if STATIC_EVERYTHING
            Utility.StaticObjects();
#endif
            Instance.Invoke(nameof(CleanupLoadingScreen), 0.5f);
        }

        private static void CleanupCameras()
        {
            foreach (var c in GameObject.FindGameObjectsWithTag("MainCamera"))
            {
                if (MainCamera != null && c != MainCamera.gameObject)
                {
                    Destroy(c);
                }
                else
                {
                    MainCamera = c.GetComponent<Camera>();
                }
            }
        }

        private void CleanupLoadingScreen()
        {
            if(loadingScreenObject) loadingScreen?.ReleaseInstance(loadingScreenObject);
        }

        private void SetRenderScale()
        {
            var res = maxRenderSize switch
            {
                RenderRes._720p => 1280f,
                RenderRes._1080p => 1920f,
                RenderRes._1440p => 2560f,
                _ => Screen.width
            };
            var renderScale = Mathf.Clamp(res / Screen.width, 0.1f, 1.0f);

            if(Debug.isDebugBuild)
                Debug.Log($"Settings render scale to {renderScale * 100}% based on {maxRenderSize.ToString()}");

            maxScale = renderScale;
#if !UNITY_EDITOR
            UniversalRenderPipeline.asset.renderScale = renderScale;
#endif
        }

        private void Update()
        {
#if !UNITY_EDITOR
            Utility.CheckQualityLevel(); //TODO - hoping to remove one day when we have a quality level callback
#endif

            UpdateMusicContext();
            UpdateCombatMusicLayer();

            if (!MainCamera) return;

            if (variableResolution)
            {
                MainCamera.allowDynamicResolution = true;

                var offset = 0f;
                var currentFrametime = Time.deltaTime;
                const float rate = 0.1f;

                offset = targetFramerate switch
                {
                    Framerate._30 => currentFrametime > (1000f / 30f) ? -rate : rate,
                    Framerate._60 => currentFrametime > (1000f / 60f) ? -rate : rate,
                    Framerate._120 => currentFrametime > (1000f / 120f) ? -rate : rate,
                    _ => offset
                };

                currentDynamicScale = Mathf.Clamp(currentDynamicScale + offset, minScale, 1f);

                var offsetVec = new Vector2(Mathf.Lerp(1, currentDynamicScale, Mathf.Clamp01((1 - axisBias) * 2f)),
                    Mathf.Lerp(1, currentDynamicScale, Mathf.Clamp01(axisBias * 2f)));

                ScalableBufferManager.ResizeBuffers(offsetVec.x, offsetVec.y);
            }
            else
            {
                MainCamera.allowDynamicResolution = false;
            }
        }

        private void InitializeMusic()
        {
            baseMusicSource = GetComponent<AudioSource>();
            if (baseMusicSource == null)
            {
                baseMusicSource = gameObject.AddComponent<AudioSource>();
            }

            ConfigureMusicSource(baseMusicSource);
            if (menuMusicClip == null)
            {
                menuMusicClip = baseMusicSource.clip;
            }

            if (baseMusicSource.clip == null && menuMusicClip != null)
            {
                baseMusicSource.clip = menuMusicClip;
            }

            combatMusicSource = gameObject.AddComponent<AudioSource>();
            ConfigureMusicSource(combatMusicSource);
            combatMusicSource.clip = combatMusicClip;
            combatMusicSource.volume = 0f;

            // Start with menu music immediately (login overlay is visible on startup)
            currentMusicContext = MusicContext.Menu;
            SwitchBaseMusic(menuMusicClip, true);
            PrepareCombatLayer(false);
        }

        private static void ConfigureMusicSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
        }

        private void UpdateMusicContext()
        {
            if (baseMusicSource == null)
            {
                return;
            }

            MusicContext targetContext = ResolveMusicContext();

            if (targetContext == currentMusicContext)
            {
                return;
            }

            currentMusicContext = targetContext;
            AudioClip nextClip = targetContext == MusicContext.Menu ? menuMusicClip : gameplayMusicClip;

            if (nextClip == null)
            {
                return;
            }

            SwitchBaseMusic(nextClip, false);
            PrepareCombatLayer(targetContext == MusicContext.Gameplay);
        }

        private static MusicContext ResolveMusicContext()
        {
            if (HasLocalGameplayPresence())
            {
                return MusicContext.Gameplay;
            }

            return LoginOverlayController.IsMetaUiActive
                ? MusicContext.Menu
                : MusicContext.Gameplay;
        }

        private static bool HasLocalGameplayPresence()
        {
            Player localPlayer = Player.LocalPlayer;
            if (localPlayer == null && PlayerManager.Instance != null)
            {
                localPlayer = PlayerManager.Instance.LocalPlayer;
            }

            return localPlayer != null && localPlayer.IsSpawned;
        }

        private void SwitchBaseMusic(AudioClip targetClip, bool immediate)
        {
            if (baseMusicSource == null || targetClip == null)
            {
                return;
            }

            if (baseMusicTransitionRoutine != null)
            {
                StopCoroutine(baseMusicTransitionRoutine);
                baseMusicTransitionRoutine = null;
            }

            float targetVolume = Mathf.Clamp01(baseMusicVolume);
            if (immediate)
            {
                baseMusicSource.clip = targetClip;
                baseMusicSource.volume = targetVolume;
                if (!baseMusicSource.isPlaying)
                {
                    baseMusicSource.Play();
                }

                return;
            }

            if (baseMusicSource.clip == targetClip && baseMusicSource.isPlaying)
            {
                baseMusicSource.volume = targetVolume;
                return;
            }

            baseMusicTransitionRoutine = StartCoroutine(FadeBaseMusicToClip(targetClip, targetVolume));
        }

        private IEnumerator FadeBaseMusicToClip(AudioClip targetClip, float targetVolume)
        {
            float fadeDuration = Mathf.Max(0.05f, sceneMusicFadeSeconds);

            if (baseMusicSource != null && baseMusicSource.isPlaying)
            {
                float fadeOutStartVolume = baseMusicSource.volume;
                float fadeOutTime = 0f;
                while (fadeOutTime < fadeDuration)
                {
                    fadeOutTime += Time.unscaledDeltaTime;
                    baseMusicSource.volume = Mathf.Lerp(fadeOutStartVolume, 0f, fadeOutTime / fadeDuration);
                    yield return null;
                }
            }

            if (baseMusicSource == null)
            {
                yield break;
            }

            baseMusicSource.clip = targetClip;
            baseMusicSource.volume = 0f;
            if (!baseMusicSource.isPlaying)
            {
                baseMusicSource.Play();
            }

            float fadeInTime = 0f;
            while (fadeInTime < fadeDuration)
            {
                fadeInTime += Time.unscaledDeltaTime;
                baseMusicSource.volume = Mathf.Lerp(0f, targetVolume, fadeInTime / fadeDuration);
                yield return null;
            }

            baseMusicSource.volume = targetVolume;
            baseMusicTransitionRoutine = null;
        }

        private void PrepareCombatLayer(bool gameplayActive)
        {
            if (combatMusicSource == null)
            {
                return;
            }

            if (!gameplayActive || combatMusicClip == null)
            {
                combatMusicSource.volume = 0f;
                if (combatMusicSource.isPlaying)
                {
                    combatMusicSource.Stop();
                }

                wasCombatActive = false;
                combatEndTimestamp = -100f;
                return;
            }

            if (combatMusicSource.clip != combatMusicClip)
            {
                combatMusicSource.clip = combatMusicClip;
            }

            if (!combatMusicSource.isPlaying)
            {
                combatMusicSource.volume = 0f;
                combatMusicSource.Play();
            }
        }

        private void UpdateCombatMusicLayer()
        {
            if (combatMusicSource == null || baseMusicSource == null)
            {
                return;
            }

            if (currentMusicContext != MusicContext.Gameplay || combatMusicClip == null)
            {
                FadeCombatLayerTowards(0f, combatFadeOutSeconds);
                if (combatMusicSource.volume <= 0.0001f && combatMusicSource.isPlaying)
                {
                    combatMusicSource.Stop();
                }

                // Restore gameplay music volume
                float baseTarget = Mathf.Clamp01(baseMusicVolume);
                FadeSourceTowards(baseMusicSource, baseTarget, combatFadeOutSeconds);
                return;
            }

            if (!combatMusicSource.isPlaying)
            {
                combatMusicSource.volume = 0f;
                combatMusicSource.Play();
            }

            bool localCombatActive = IsLocalPlayerInCombat();

            // Track combat end timestamp for hold period
            if (localCombatActive)
            {
                wasCombatActive = true;
                combatEndTimestamp = -100f;
            }
            else if (wasCombatActive)
            {
                // Combat just ended — start hold timer
                wasCombatActive = false;
                combatEndTimestamp = Time.unscaledTime;
            }

            // Determine if we should still be in combat music (including hold period)
            bool shouldPlayCombat = localCombatActive ||
                (combatEndTimestamp > 0f && Time.unscaledTime - combatEndTimestamp < combatHoldSeconds);

            float combatTarget = shouldPlayCombat ? Mathf.Clamp01(combatMusicVolume) : 0f;
            float combatFade = shouldPlayCombat ? combatFadeInSeconds : combatFadeOutSeconds;
            FadeCombatLayerTowards(combatTarget, combatFade);

            // Crossfade: bring gameplay music down when combat is playing, up when not
            float combatBlend = combatMusicSource.volume / Mathf.Max(Mathf.Clamp01(combatMusicVolume), 0.01f);
            float baseVolTarget = Mathf.Lerp(Mathf.Clamp01(baseMusicVolume), 0f, combatBlend);
            float baseFade = shouldPlayCombat ? combatFadeInSeconds : combatFadeOutSeconds;
            FadeSourceTowards(baseMusicSource, baseVolTarget, baseFade);
        }

        private void FadeCombatLayerTowards(float targetVolume, float fadeDuration)
        {
            FadeSourceTowards(combatMusicSource, targetVolume, fadeDuration);
        }

        private static void FadeSourceTowards(AudioSource source, float targetVolume, float fadeDuration)
        {
            if (source == null)
            {
                return;
            }

            fadeDuration = Mathf.Max(0.05f, fadeDuration);
            float step = Time.unscaledDeltaTime / fadeDuration;
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, step);
        }

        private static bool IsLocalPlayerInCombat()
        {
            Player localPlayer = Player.LocalPlayer;
            if (localPlayer == null && PlayerManager.Instance != null)
            {
                localPlayer = PlayerManager.Instance.LocalPlayer;
            }

            if (localPlayer == null || !localPlayer.IsSpawned || localPlayer.IsDead)
            {
                return false;
            }

            if (localPlayer.TryGetComponent(out PlayerAttack localAttack) && localAttack.IsAttacking)
            {
                return true;
            }

            return localPlayer.IsInCombat;
        }

        public void ToggleSRPBatcher(bool enableSRPBatcher)
        {
            UniversalRenderPipeline.asset.useSRPBatcher = enableSRPBatcher;
        }

        public static void LoadScene(int buildIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            LoadScene(SceneUtility.GetScenePathByBuildIndex(buildIndex), mode);
        }

        public static void LoadScene(string scenePath, LoadSceneMode mode = LoadSceneMode.Single)
        {
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            switch (mode)
            {
                case LoadSceneMode.Single:
                    Instance.StartCoroutine(LoadSceneInternal(scenePath));
                    break;
                case LoadSceneMode.Additive:
                    SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        private static IEnumerator LoadSceneInternal(string scenePath)
        {
            var loadingScreenLoading = Instance.loadingScreen.InstantiateAsync();
            yield return loadingScreenLoading;
            Instance.loadingScreenObject = loadingScreenLoading.Result;
            Instance.loadingScreenObject.SendMessage("SetLoad", 0.0001f);
            DontDestroyOnLoad(Instance.loadingScreenObject);

            var buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if(Debug.isDebugBuild)
                Debug.Log($"loading scene {scenePath} at build index {buildIndex}");

            // get current scene and set a loading scene as active
            var currentScene = SceneManager.GetActiveScene();
            var loadingScene = SceneManager.CreateScene("Loading");
            SceneManager.SetActiveScene(loadingScene);

            // unload last scene
            var unload = SceneManager.UnloadSceneAsync(currentScene, UnloadSceneOptions.None);
            while (!unload.isDone)
            {
                Instance.loadingScreenObject.SendMessage("SetLoad", unload.progress * 0.5f);
                yield return null;
            }

            // clean up
            var clean = Resources.UnloadUnusedAssets();
            while (!clean.isDone) { yield return null; }

            // load new scene
            var load = new AsyncOperation();
#if UNITY_EDITOR
            if (buildIndex == -1)
            {
                load = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath,
                    new LoadSceneParameters(LoadSceneMode.Single));
            }
            else
            {
                load = SceneManager.LoadSceneAsync(buildIndex);
            }
#else
            load = SceneManager.LoadSceneAsync(scenePath);
#endif
            while (!load.isDone)
            {
                Instance.loadingScreenObject.SendMessage("SetLoad", load.progress * 0.5f + 0.5f);
                yield return null;
            }
        }

        private static IEnumerator LoadPrefab<T>(AssetReference assetRef, AsyncOperationHandle assetLoading, Transform parent = null)
        {
            if (typeof(T) == typeof(GameObject))
            {
                assetLoading = assetRef.InstantiateAsync(parent);
            }
            else
            {
                assetLoading = assetRef.LoadAssetAsync<T>();
            }
            yield return assetLoading;
        }

        public static void ExitGame(string s = "")
        {
            if(s != "")
                Debug.LogError(s);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        private static void CmdArgs()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length <= 0) return;
            foreach (var argRaw in args)
            {
                if(string.IsNullOrEmpty(argRaw) || argRaw[0] != '-') continue;
                var arg = argRaw.Split(':');

                switch (arg[0])
                {
                    case "-loadlevel":
                        LoadScene(arg[1]);
                        break;
                    case "-benchmarkFlythrough":
                        LoadScene("benchmark_island-flythrough");
                        break;
                }
            }
        }
    }
}
