using System;
using UnityEngine;

[Serializable]
public struct NamedAudioClip
{
    public string soundName;
    public AudioClip clip;
}

public class AudioEmitter : MonoBehaviour
{
    public enum AudioType
    {
        SFX,
        BGM
    }

    [Header("單一音源設定 (相容舊有物件)")]
    [SerializeField] private AudioClip audioClip;
    [SerializeField] private AudioType audioType = AudioType.SFX;

    [Header("播放時機與屬性")]
    [Tooltip("勾選後，當物件載入或場景開始時會自動播放")]
    [SerializeField] private bool playOnStart = false;
    [Tooltip("對背景音樂有效，決定是否持續循環播放")]
    [SerializeField] private bool loop = true;

    [Header("多音源擴充庫 (供玩家複雜技能使用)")]
    [SerializeField] private NamedAudioClip[] soundLibrary;

    private AudioSource localLoopSource;
    private readonly AudioSource[] _pitchedOneShotSources = new AudioSource[8];
    private int _nextPitchedSource;

    private void Start()
    {
        if (playOnStart)
        {
            PlaySound();
        }
    }

    public void PlaySound()
    {
        if (audioClip == null || AudioManager.Instance == null) return;

        if (audioType == AudioType.SFX)
        {
            AudioManager.Instance.PlaySFX(audioClip);
        }
        else
        {
            AudioManager.Instance.PlayBGM(audioClip, loop);
        }
    }

    public void PlayOneShot(string soundName)
    {
        PlayOneShot(soundName, 1f, 1f);
    }

    public void PlayOneShot(string soundName, float pitch, float volume = 1f)
    {
        AudioClip clip = GetClip(soundName);
        if (clip != null && AudioManager.Instance != null)
        {
            if (Mathf.Approximately(pitch, 1f) && Mathf.Approximately(volume, 1f))
            {
                AudioManager.Instance.PlaySFX(clip);
            }
            else
            {
                PlayPitchedOneShot(clip, pitch, volume);
            }
        }
        else
        {
            // [重點註釋] 攔截字串錯誤或未指派音檔的致命失誤，提供明確排查方向
            Debug.LogWarning($"[AudioEmitter] 無法播放單次音效 '{soundName}'。原因：未在 Sound Library 中找到該名稱，或未指派 AudioClip。");
        }
    }

    private void PlayPitchedOneShot(AudioClip clip, float pitch, float volume)
    {
        int index = _nextPitchedSource++ % _pitchedOneShotSources.Length;
        AudioSource source = _pitchedOneShotSources[index];
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            _pitchedOneShotSources[index] = source;
        }

        source.Stop();
        source.clip = clip;
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        source.volume = Mathf.Clamp01(volume);
        source.Play();
    }

    public void PlayLocalLoop(string soundName, float pitch = 1f, float volume = 1f)
    {
        AudioClip clip = GetClip(soundName);
        if (clip == null)
        {
            // [重點註釋] 同樣攔截連續音效的資源遺失問題
            Debug.LogWarning($"[AudioEmitter] 無法播放連續音效 '{soundName}'。請檢查 Sound Library 設定。");
            return;
        }

        if (localLoopSource == null)
        {
            localLoopSource = gameObject.AddComponent<AudioSource>();
            localLoopSource.loop = true;
            localLoopSource.playOnAwake = false;
            localLoopSource.spatialBlend = 0f;
        }

        if (localLoopSource.clip != clip || !localLoopSource.isPlaying)
        {
            localLoopSource.clip = clip;
            localLoopSource.Play();
        }

        localLoopSource.pitch = pitch;
        localLoopSource.volume = volume;
    }

    public void StopLocalLoop()
    {
        if (localLoopSource != null && localLoopSource.isPlaying)
        {
            localLoopSource.Stop();
        }
    }

    public bool IsPlayingLocalLoop(string soundName)
    {
        if (localLoopSource != null && localLoopSource.isPlaying)
        {
            return localLoopSource.clip == GetClip(soundName);
        }
        return false;
    }

    private AudioClip GetClip(string soundName)
    {
        foreach (var s in soundLibrary)
        {
            if (s.soundName == soundName) return s.clip;
        }
        return null;
    }
}
