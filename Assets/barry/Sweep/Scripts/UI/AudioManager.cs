using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("音軌設置")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource uiAudioSource;

    [Header("介面音效")]
    [SerializeField] private AudioClip uiClickSound;

    private void Awake()
    {
        // 確保全域唯一性並跨場景保留，維持單例生命週期
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayBGM(AudioClip clip, bool loop = true)
    {
        if (clip == null || bgmSource.clip == clip) return;

        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.Play();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip);
        }
    }

    // 唯一的通用按鈕點擊事件，高度精簡化
    public void PlayUIClick()
    {
        if (uiClickSound != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(uiClickSound);
    }

    public void PlayCustomUISound(AudioClip clip)
    {
        if (clip != null && uiAudioSource != null)
            uiAudioSource.PlayOneShot(clip);
    }

    public void StopBGM()
    {
        if (bgmSource.isPlaying)
        {
            bgmSource.Stop();
        }
    }

    public void SetBGMVolume(float volume)
    {
        bgmSource.volume = Mathf.Clamp01(volume);
    }

    public void SetSFXVolume(float volume)
    {
        if (sfxSource != null)
            sfxSource.volume = Mathf.Clamp01(volume);
    }

    public void SetUIVolume(float volume)
    {
        if (uiAudioSource != null)
            uiAudioSource.volume = Mathf.Clamp01(volume);
    }
}