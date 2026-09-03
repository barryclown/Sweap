using UnityEngine;
using System;
using System.Collections.Generic;

// 定義結構，讓你在面板可以自由設定「哪個材質，對應哪個音檔名稱」
[Serializable]
public struct TrashSoundMapping
{
    public TrashType type;
    public string soundName;
}

[RequireComponent(typeof(PlayerController), typeof(AudioEmitter))]
public class PlayerAudioController : MonoBehaviour
{
    private PlayerController player;
    private AudioEmitter emitter;

    [Header("對應音檔名稱 (需與 AudioEmitter 面板設定完全一致)")]
    public string switchSkillSound = "SwitchSkill";
    public string leftPressSound = "LeftPress";
    public string leftReleaseSound = "LeftRelease";
    public string chargingSound = "Charging";
    public string chargeShootSound = "ChargeShoot";
    public string wallHitSound = "WallHit";
    public string moveLoopSound = "Moving";

    [Header("垃圾打擊設定 (支援多材質)")]
    [Tooltip("將各種垃圾材質對應到 AudioEmitter 裡的名稱")]
    public TrashSoundMapping[] trashHitMappings;
    [Tooltip("獨立材質防連發機制：例如 0.05 秒內不會重複播放同一個鋁罐聲，但可以同時播放鋁罐跟玻璃聲")]
    public float trashHitCooldown = 0.05f;

    // 利用字典儲存「每一種材質」上次發出聲音的時間
    private Dictionary<TrashType, float> _hitCooldowns = new Dictionary<TrashType, float>();
    private int _hitPitchChain;
    private float _lastAnyTrashHitTime = float.NegativeInfinity;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        emitter = GetComponent<AudioEmitter>();
    }

    private void OnEnable()
    {
        player.OnModeChanged += HandleModeChanged;
        player.OnLeftPressAction += HandleLeftPress;
        player.OnLeftReleaseAction += HandleLeftRelease;
        player.OnRightPressStart += HandleRightPress;
        player.OnChargedSweepUpdate += HandleChargeUpdate;
        player.OnChargedSweepReleased += HandleChargeRelease;
        player.OnWallHitEvent += HandleWallHit;

        // 註冊帶有參數的垃圾擊中事件
        player.OnTrashHitEvent += HandleTrashHit;
    }

    private void OnDisable()
    {
        player.OnModeChanged -= HandleModeChanged;
        player.OnLeftPressAction -= HandleLeftPress;
        player.OnLeftReleaseAction -= HandleLeftRelease;
        player.OnRightPressStart -= HandleRightPress;
        player.OnChargedSweepUpdate -= HandleChargeUpdate;
        player.OnChargedSweepReleased -= HandleChargeRelease;
        player.OnWallHitEvent -= HandleWallHit;

        player.OnTrashHitEvent -= HandleTrashHit;
    }

    private void Update()
    {
        if (player.CurrentSpeed > 0.1f && !player.isBeingAbsorbed && !player.isBlocking)
        {
            float vol = Mathf.Clamp01(player.CurrentSpeed / player.maxSpeed);
            emitter.PlayLocalLoop(moveLoopSound, 1f, vol);
        }
        else if (emitter.IsPlayingLocalLoop(moveLoopSound))
        {
            emitter.StopLocalLoop();
        }
    }

    private void HandleModeChanged(BroomMode mode)
    {
        emitter.PlayOneShot(switchSkillSound);
    }

    private void HandleLeftPress()
    {
        emitter.PlayOneShot(leftPressSound);
    }

    private void HandleLeftRelease()
    {
        emitter.PlayOneShot(leftReleaseSound);
    }

    private void HandleWallHit()
    {
        emitter.PlayOneShot(wallHitSound);
    }

    private void HandleRightPress()
    {
        emitter.PlayLocalLoop(chargingSound, 0.8f);
    }

    private void HandleChargeUpdate(float holdTime, float t, Vector2 origin, Vector2 dir)
    {
        float pitch = Mathf.Lerp(0.8f, 1.5f, t);
        emitter.PlayLocalLoop(chargingSound, pitch);
    }

    private void HandleChargeRelease(float holdTime, float t, Vector2 origin, Vector2 dir)
    {
        if (emitter.IsPlayingLocalLoop(chargingSound))
        {
            emitter.StopLocalLoop();
        }
        emitter.PlayOneShot(chargeShootSound, Mathf.Lerp(1f, 1.22f, t));
    }

    // [重點註釋] 高階材質混音邏輯：透過字典找出該材質上次播放的時間，進行獨立冷卻
    private void HandleTrashHit(TrashType type)
    {
        if (!_hitCooldowns.ContainsKey(type))
        {
            _hitCooldowns[type] = -1f;
        }

        if (Time.time - _hitCooldowns[type] > trashHitCooldown)
        {
            string soundToPlay = GetTrashSoundName(type);

            if (!string.IsNullOrEmpty(soundToPlay))
            {
                _hitPitchChain = Time.unscaledTime - _lastAnyTrashHitTime <= 0.55f
                    ? Mathf.Min(_hitPitchChain + 1, 10)
                    : 1;
                _lastAnyTrashHitTime = Time.unscaledTime;
                float pitch = 0.94f + ((_hitPitchChain - 1) * 0.065f);
                emitter.PlayOneShot(soundToPlay, pitch);
                _hitCooldowns[type] = Time.time;
            }
        }
    }

    // 輔助函式：從設定好的清單中尋找對應的字串
    private string GetTrashSoundName(TrashType type)
    {
        for (int i = 0; i < trashHitMappings.Length; i++)
        {
            if (trashHitMappings[i].type == type)
            {
                return trashHitMappings[i].soundName;
            }
        }
        return null;
    }
}
