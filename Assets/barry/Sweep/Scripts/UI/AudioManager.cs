using UnityEngine;

public enum DemoMixMoment
{
    TrashImpact,
    WallImpact,
    Absorb,
    ChargedRelease,
    FullCharge,
    LevelUp,
    PhaseChange,
    FinalFever,
    Result
}

/// <summary>
/// Sweep 的輕量執行期混音總控。保留三條既有 AudioSource，不要求額外 Mixer asset，
/// 但所有全域與局部音源都會使用同一組 headroom、高潮 ducking 與階段音高。
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("音軌設置")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource uiAudioSource;

    [Header("介面音效")]
    [SerializeField] private AudioClip uiClickSound;

    [Header("Sweep 混音 Headroom")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 0.92f;
    [SerializeField, Range(0f, 1f)] private float bgmBusVolume = 0.5f;
    [SerializeField, Range(0f, 1f)] private float sfxBusVolume = 0.82f;
    [SerializeField, Range(0f, 1f)] private float uiBusVolume = 0.72f;
    [SerializeField, Range(0f, 0.8f)] private float maximumBgmDuck = 0.48f;
    [SerializeField, Min(0.05f)] private float duckReleaseSeconds = 0.42f;

    private float _userBgmVolume = 1f;
    private float _userSfxVolume = 1f;
    private float _userUiVolume = 1f;
    private float _bgmDuck;
    private float _duckHoldUntil;
    private int _demoPhase;
    private bool _frenzy;
    private bool _paused;
    private bool _showingResult;

    public float SfxGain => Mathf.Clamp01(masterVolume * sfxBusVolume * _userSfxVolume);
    public float UiGain => Mathf.Clamp01(masterVolume * uiBusVolume * _userUiVolume);
    public float BgmGain => CalculateBgmGain(_bgmDuck, _paused, _showingResult);

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureSources();
            ApplyMixImmediately();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= _duckHoldUntil)
        {
            float releaseRate = maximumBgmDuck / Mathf.Max(0.05f, duckReleaseSeconds);
            _bgmDuck = Mathf.MoveTowards(_bgmDuck, 0f, releaseRate * Time.unscaledDeltaTime);
        }

        if (bgmSource != null)
        {
            bgmSource.volume = BgmGain;
            bgmSource.pitch = Mathf.MoveTowards(
                bgmSource.pitch,
                GetTargetBgmPitch(),
                0.16f * Time.unscaledDeltaTime);
        }

        if (sfxSource != null) sfxSource.volume = SfxGain;
        if (uiAudioSource != null) uiAudioSource.volume = UiGain;
    }

    public void PlayBGM(AudioClip clip, bool loop = true)
    {
        if (clip == null || bgmSource == null) return;

        if (bgmSource.clip != clip)
        {
            bgmSource.clip = clip;
            bgmSource.time = 0f;
        }

        bgmSource.loop = loop;
        if (!bgmSource.isPlaying) bgmSource.Play();
    }

    public void PlaySFX(AudioClip clip, float volumeScale = 1f)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public void PlayUIClick()
    {
        if (uiClickSound != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(uiClickSound, 0.82f);
    }

    public void PlayCustomUISound(AudioClip clip, float volumeScale = 1f)
    {
        if (clip != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    public void PushMixMoment(DemoMixMoment moment)
    {
        float duck;
        float hold;
        switch (moment)
        {
            case DemoMixMoment.TrashImpact:
                duck = 0.12f;
                hold = 0.025f;
                break;
            case DemoMixMoment.WallImpact:
                duck = 0.18f;
                hold = 0.045f;
                break;
            case DemoMixMoment.Absorb:
                duck = 0.2f;
                hold = 0.055f;
                break;
            case DemoMixMoment.ChargedRelease:
                duck = 0.3f;
                hold = 0.09f;
                break;
            case DemoMixMoment.FullCharge:
                duck = 0.44f;
                hold = 0.16f;
                break;
            case DemoMixMoment.LevelUp:
                duck = 0.38f;
                hold = 0.2f;
                break;
            case DemoMixMoment.PhaseChange:
                duck = 0.28f;
                hold = 0.12f;
                break;
            case DemoMixMoment.FinalFever:
                duck = 0.42f;
                hold = 0.22f;
                break;
            case DemoMixMoment.Result:
                duck = 0.32f;
                hold = 0.18f;
                break;
            default:
                duck = 0f;
                hold = 0f;
                break;
        }

        _bgmDuck = Mathf.Max(_bgmDuck, Mathf.Min(duck, maximumBgmDuck));
        _duckHoldUntil = Mathf.Max(_duckHoldUntil, Time.unscaledTime + hold);
    }

    public void SetDemoPhase(int phase, bool frenzy)
    {
        _demoPhase = Mathf.Clamp(phase, 0, 3);
        _frenzy = frenzy;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
    }

    public void SetResult(bool showingResult)
    {
        _showingResult = showingResult;
        if (showingResult) PushMixMoment(DemoMixMoment.Result);
    }

    public void ResetDemoMix()
    {
        _demoPhase = 0;
        _frenzy = false;
        _paused = false;
        _showingResult = false;
        _bgmDuck = 0f;
        _duckHoldUntil = 0f;
        ApplyMixImmediately();
    }

    public void StopBGM()
    {
        if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop();
    }

    public void SetBGMVolume(float volume)
    {
        _userBgmVolume = Mathf.Clamp01(volume);
        if (bgmSource != null) bgmSource.volume = BgmGain;
    }

    public void SetSFXVolume(float volume)
    {
        _userSfxVolume = Mathf.Clamp01(volume);
        if (sfxSource != null) sfxSource.volume = SfxGain;
    }

    public void SetUIVolume(float volume)
    {
        _userUiVolume = Mathf.Clamp01(volume);
        if (uiAudioSource != null) uiAudioSource.volume = UiGain;
    }

    public static float CalculateBusGain(float master, float bus, float user)
    {
        return Mathf.Clamp01(Mathf.Clamp01(master) * Mathf.Clamp01(bus) * Mathf.Clamp01(user));
    }

    private float CalculateBgmGain(float duck, bool paused, bool result)
    {
        float stateGain = result ? 0.42f : paused ? 0.62f : 1f;
        float baseGain = CalculateBusGain(masterVolume, bgmBusVolume, _userBgmVolume);
        return Mathf.Clamp01(baseGain * stateGain * (1f - Mathf.Clamp(duck, 0f, maximumBgmDuck)));
    }

    private float GetTargetBgmPitch()
    {
        if (_showingResult) return 0.96f;
        if (_paused) return 0.985f;
        if (_frenzy) return 1.09f;
        return 1f + (_demoPhase * 0.018f);
    }

    private void ConfigureSources()
    {
        ConfigureSource(bgmSource, 8, true);
        ConfigureSource(sfxSource, 64, false);
        ConfigureSource(uiAudioSource, 32, false);
    }

    private static void ConfigureSource(AudioSource source, int priority, bool loop)
    {
        if (source == null) return;
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.priority = priority;
    }

    private void ApplyMixImmediately()
    {
        if (bgmSource != null)
        {
            bgmSource.volume = BgmGain;
            bgmSource.pitch = GetTargetBgmPitch();
        }
        if (sfxSource != null) sfxSource.volume = SfxGain;
        if (uiAudioSource != null) uiAudioSource.volume = UiGain;
    }
}
