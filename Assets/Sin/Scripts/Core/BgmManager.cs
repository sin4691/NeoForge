using Bae;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Factory.Audio
{
    // 씬 전환에도 살아남는 BGM 매니저(싱글턴). 씬마다 AudioSource를 두면 전환할 때 음악이 끊기고
    // 겹치므로 하나만 두고, 씬이 로드될 때마다 BgmLibrary 표에서 그 씬의 곡을 찾아 크로스페이드로
    // 바꾼다. 같은 곡이면 다시 시작하지 않고 그대로 이어간다.
    //
    // 씬에 따로 배치할 필요 없다 — 어느 씬에서 시작하든 로드 직후 스스로 만들어진다
    // (RuntimeInitializeOnLoadMethod). 곡 표는 Resources/BgmLibrary 에셋(BgmLibrary.cs 참고)에서
    // 읽고, 그게 없으면 조용히 무음으로 동작한다(음원이 아직 없어도 에러가 안 난다).
    //
    // 광고: Bae 어셈블리는 이쪽(FactoryPrototype)을 참조할 수 없어서(반대로 이쪽이 Bae를
    // 참조한다), LevelPlayManager의 정적 이벤트(AdStarted/AdEnded)를 여기서 구독한다.
    // 광고 중 음소거는 사용자의 볼륨/음소거 설정과 별개의 플래그(adPaused)로 관리해서, 광고가 끝났을 때
    // 원래 설정 그대로 돌아온다.
    public sealed class BgmManager : MonoBehaviour
    {
        private const string LibraryResourceName = "BgmLibrary";
        private const string VolumeKey = "Bgm.Volume";
        private const string MutedKey = "Bgm.Muted";
        // 광고가 뜨거나 음소거할 때는 곡 전환보다 훨씬 빨리 줄인다.
        private const float DefaultFadeInSeconds = 2.5f;
        private const float DefaultFadeOutSeconds = 2.5f;
        private const float DefaultDuckSeconds = 0.6f;
        // Longest time a single frame may advance a fade (see Update).
        private const float MaxFadeStepSeconds = 0.05f;

        public static BgmManager Instance { get; private set; }

        private readonly AudioSource[] sources = new AudioSource[2];
        // Fade progress per source (0 = silent, 1 = fully on) and the per-track volume of the clip it plays.
        // Progress moves linearly; the real volume applies a smoothstep curve on top (see Apply).
        private readonly float[] levels = new float[2];
        private readonly float[] clipVolumes = new float[2];
        private int activeIndex;
        private BgmLibrary library;
        private float masterVolume = 0.7f;
        private bool muted;
        private bool adPaused;

        public float MasterVolume => masterVolume;
        public bool IsMuted => muted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject("[BGM] BgmManager").AddComponent<BgmManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            masterVolume = PlayerPrefs.GetFloat(VolumeKey, masterVolume);
            muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;

            for (int i = 0; i < sources.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f; // 2D — 카메라 위치와 상관없이 항상 같은 크기.
                source.volume = 0f;
                sources[i] = source;
            }

            library = Resources.Load<BgmLibrary>(LibraryResourceName);
            if (library == null)
                Debug.Log("[BgmManager] Resources/BgmLibrary 에셋이 없어서 BGM 없이 동작합니다. " +
                    "메뉴 Tools > Audio > Create BGM Library로 만들 수 있습니다.");
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            LevelPlayManager.AdStarted += PauseForAd;
            LevelPlayManager.AdEnded += ResumeAfterAd;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            LevelPlayManager.AdStarted -= PauseForAd;
            LevelPlayManager.AdEnded -= ResumeAfterAd;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // sceneLoaded는 이 매니저가 만들어지기 전에 이미 지나간 첫 씬에는 안 불린다 —
            // 지금 열려 있는 씬의 곡을 직접 시작한다.
            PlayForScene(SceneManager.GetActiveScene().name);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            PlayForScene(scene.name);
        }

        public void PlayForScene(string sceneName)
        {
            if (library == null) return;

            if (library.TryGet(sceneName, out var entry))
            {
                SwitchTo(entry.clip, entry.volume);
            }
            else if (library.stopWhenSceneUnmapped)
            {
                SwitchTo(null, 0f);
            }
            // 표에 없고 stopWhenSceneUnmapped가 꺼져 있으면 앞 씬 곡을 그대로 이어간다.
        }

        public void SetVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(VolumeKey, masterVolume);
        }

        public void SetMuted(bool value)
        {
            muted = value;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
        }

        // 광고가 뜨는 동안 BGM을 줄이고, 끝나면 원래 설정으로 되돌린다. 광고가 실패/닫힘 어느 쪽으로
        // 끝나든 ResumeAfterAd가 불려야 한다(LevelPlayManager가 보장). 여러 번 불려도 안전하다.
        public void PauseForAd() => adPaused = true;
        public void ResumeAfterAd() => adPaused = false;

        private void SwitchTo(AudioClip clip, float volume)
        {
            var active = sources[activeIndex];
            // 같은 곡이면 다시 시작하지 않는다(Title -> Title 재로드 등에서 곡이 처음으로 안 돌아가게).
            if (clip != null && clip == active.clip && active.isPlaying)
            {
                clipVolumes[activeIndex] = volume;
                return;
            }

            // 지금 곡은 "나가는 쪽"이 되어 Update에서 서서히 줄고, 비어 있던 소스가 새 곡을 0에서
            // 시작해 서서히 커진다(크로스페이드). 나가는 쪽의 진행도(levels)는 그대로 이어받아서
            // 전환 도중에 또 전환돼도 소리가 튀지 않고 현재 크기에서 계속 줄어든다.
            int next = 1 - activeIndex;
            var incoming = sources[next];
            incoming.Stop();
            incoming.clip = clip;
            levels[next] = 0f;
            clipVolumes[next] = volume;
            incoming.volume = 0f;
            if (clip != null) incoming.Play();

            // The previous track is cut immediately on a scene change (no fade-out); only the new track fades in.
            active.Stop();
            active.clip = null;
            levels[activeIndex] = 0f;
            active.volume = 0f;

            activeIndex = next;
        }

        private void Update()
        {
            // Cap the step per frame. Scene loads freeze the main thread for seconds, and the first frame
            // afterwards reports that whole gap as delta time - without the cap the fade would jump to
            // its end in one frame and the track would switch instantly instead of fading.
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxFadeStepSeconds);
            float fadeIn = library != null ? library.fadeInSeconds : DefaultFadeInSeconds;
            float fadeOut = library != null ? library.fadeOutSeconds : DefaultFadeOutSeconds;
            float duck = library != null ? library.duckSeconds : DefaultDuckSeconds;

            var active = sources[activeIndex];
            bool ducking = muted || adPaused;
            float target = active.clip != null && !ducking ? 1f : 0f;
            // 올라갈 땐 천천히(fadeIn), 광고/음소거로 내려갈 땐 광고 소리와 안 겹치게 조금 더 빠르게(duck).
            float seconds = target > levels[activeIndex] ? fadeIn : duck;
            levels[activeIndex] = Mathf.MoveTowards(levels[activeIndex], target, dt / Mathf.Max(0.01f, seconds));
            Apply(activeIndex);

            int outIndex = 1 - activeIndex;
            var outgoing = sources[outIndex];
            if (outgoing.clip != null)
            {
                levels[outIndex] = Mathf.MoveTowards(levels[outIndex], 0f, dt / Mathf.Max(0.01f, fadeOut));
                Apply(outIndex);
                if (levels[outIndex] <= 0.0001f)
                {
                    outgoing.Stop();
                    outgoing.clip = null;
                }
            }
        }

        // 진행도에 smoothstep 곡선을 씌워 실제 볼륨으로 바꾼다(시작/끝이 완만해서 갑자기 커지거나 꺼지지 않는다).
        private void Apply(int index)
        {
            float x = Mathf.Clamp01(levels[index]);
            float eased = x * x * (3f - 2f * x);
            sources[index].volume = clipVolumes[index] * masterVolume * eased;
        }
    }
}
