// Assets/Scripts/Core/GuideDirector.cs
// 流星引导 · 三段式引导导演（2026-10-20，纯逻辑可测）：
//   老师离开视野（曾有稳定锁定，丢失 ≥0.8s）
//     → Confused 疑惑(1.8s)：老师去哪里了呢？
//     → Found   发现(0.9s)：找到老师了！跟我来～
//     → Leading 带领(6s，第3轮8s)：小朋友，跟我来～
//         ├─ 跟随（目标重获且人脸居中持续 1s）→ Success（"找到老师了，真棒！"）
//         └─ 未跟随 → 轮次+1（最多 3 轮，逐轮强化）→ 回到 Confused
//     → 3 轮仍未跟随 → Silent 静默陪伴（15s 或直到目标重获），之后可再次触发
// 话术集（找老师/追星星）由 StarProgress 的星星存量决定（见 GuideVoice 映射）。
using UnityEngine;

public enum GuidePhase { Idle, Confused, Found, Leading, Success, Silent }

/// <summary>引导导演事件（driver 消费：语音/泡泡/特效/信号）。</summary>
public enum GuideEvent { None, EnterConfused, EnterFound, EnterLeading, EnterSuccess, RoundUp, EnterSilent }

public class GuideDirectorLogic
{
    public GuidePhase Phase { get; private set; } = GuidePhase.Idle;
    /// <summary>当前强化轮次（1..MaxRounds）。</summary>
    public int Round { get; private set; } = 1;
    public float PhaseTime { get; private set; }
    /// <summary>本轮带领时长（第 3 轮更长）。</summary>
    public float CurrentLeadDuration => Round >= 3 ? _leadDuration3 : _leadDuration;

    readonly float _lostTriggerSec;    // 丢失多久后开始找（0.8s）
    readonly float _confusedSec;       // 疑惑时长
    readonly float _foundSec;          // 发现时长
    readonly float _leadDuration;      // 带领时长（1-2 轮）
    readonly float _leadDuration3;     // 带领时长（第 3 轮）
    readonly int _maxRounds;           // 强化轮数上限（3）
    readonly float _followConfirmSec;  // 跟随确认时长（1s）
    readonly float _successSec;        // 成功反馈展示时长
    readonly float _silentSec;         // 静默期时长

    float _lostT;
    float _followT;

    public GuideDirectorLogic(float lostTriggerSec = 0.8f, float confusedSec = 1.8f, float foundSec = 0.9f,
                              float leadDuration = 6f, float leadDuration3 = 8f, int maxRounds = 3,
                              float followConfirmSec = 1f, float successSec = 1.5f, float silentSec = 15f)
    {
        _lostTriggerSec = Mathf.Max(0.1f, lostTriggerSec);
        _confusedSec = Mathf.Max(0.1f, confusedSec);
        _foundSec = Mathf.Max(0.1f, foundSec);
        _leadDuration = Mathf.Max(0.5f, leadDuration);
        _leadDuration3 = Mathf.Max(_leadDuration, leadDuration3);
        _maxRounds = Mathf.Max(1, maxRounds);
        _followConfirmSec = Mathf.Max(0.1f, followConfirmSec);
        _successSec = Mathf.Max(0.2f, successSec);
        _silentSec = Mathf.Max(1f, silentSec);
    }

    public void Reset()
    {
        Phase = GuidePhase.Idle;
        Round = 1;
        PhaseTime = 0f;
        _lostT = 0f;
        _followT = 0f;
    }

    /// <summary>每帧推进。
    /// targetValid=老师被检测到；everLocked=本会话曾稳定锁定过（认识老师）；
    /// faceCentered=老师人脸在视野中心（跟随判定的硬条件）。</summary>
    public GuideEvent Tick(float dt, bool targetValid, bool everLocked, bool faceCentered)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case GuidePhase.Idle:
                _lostT = targetValid ? 0f : _lostT + dt;
                if (everLocked && !targetValid && _lostT >= _lostTriggerSec)
                {
                    Round = 1;
                    Enter(GuidePhase.Confused);
                    return GuideEvent.EnterConfused;
                }
                return GuideEvent.None;

            case GuidePhase.Confused:
                if (PhaseTime >= _confusedSec) { Enter(GuidePhase.Found); return GuideEvent.EnterFound; }
                return GuideEvent.None;

            case GuidePhase.Found:
                if (PhaseTime >= _foundSec) { Enter(GuidePhase.Leading); return GuideEvent.EnterLeading; }
                return GuideEvent.None;

            case GuidePhase.Leading:
                // 跟随判定（2026-10-20 修）：老师重新入画并稳定 followConfirm 即视为跟随——
                // 不再强制"人脸居中"（人脸检测苛刻 → 孩子已看到老师、引导不停、语音继续喊"跟我来"）。
                // 人脸居中（faceCentered）仅用于星星计分，不参与跟随判定。
                _followT = targetValid ? _followT + dt : 0f;
                if (_followT >= _followConfirmSec) { Enter(GuidePhase.Success); return GuideEvent.EnterSuccess; }
                if (PhaseTime >= CurrentLeadDuration)
                {
                    if (Round >= _maxRounds) { Enter(GuidePhase.Silent); return GuideEvent.EnterSilent; }
                    Round++;
                    Enter(GuidePhase.Confused);
                    return GuideEvent.RoundUp; // 注意：本轮同时进入下一轮 Confused
                }
                return GuideEvent.None;

            case GuidePhase.Success:
                if (PhaseTime >= _successSec) Enter(GuidePhase.Idle);
                return GuideEvent.None;

            case GuidePhase.Silent:
                // 静默期：目标重获或静默计时结束 → 回 Idle（等待下一次自然触发）
                if (targetValid || PhaseTime >= _silentSec) Enter(GuidePhase.Idle);
                return GuideEvent.None;
        }
        return GuideEvent.None;
    }

    void Enter(GuidePhase p)
    {
        Phase = p;
        PhaseTime = 0f;
        if (p == GuidePhase.Leading) _followT = 0f;
    }
}
