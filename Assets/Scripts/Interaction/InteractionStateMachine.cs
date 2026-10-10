// Assets/Scripts/Interaction/InteractionStateMachine.cs
// 核心交互状态机（纯逻辑，对应设计文档 §3.1 的转换表 + 用户调整项）：
//   待机 → 引导：讲话中 ∧ 未注视 ∧ 锚点有效 ∧ 冷却完毕
//   引导 → 隐藏：孩子开始注视（GazeStarted）
//   隐藏 → 反馈：注视已达成（latch）∧ 语音结束瞬间仍在看
//   隐藏 → 引导：讲话继续 ∧ 孩子又看别处（超过抖动宽限）∧ 引导冷却完毕   ← "温和重新引导"
//   隐藏 → 待机：讲完但没看够 → 宽限后回待机（不给反馈）
//   反馈 → 待机：反馈动画播完（随后受反馈冷却约束才允许再引导）
//   任意 → 待机：锚点失效
using UnityEngine;

public enum InteractionState { Idle, Guiding, Hidden, Feedback }

public struct InteractionInputs
{
    public bool speaking;         // 老师讲话中
    public bool speechJustEnded;  // 本帧恰逢讲话段结束（由驱动层产生边沿）
    public bool looking;          // 当前是否在注视（代理信号）
    public bool gazeStarted;      // GazeLogic 瞬态
    public bool gazeSustained;    // GazeLogic 瞬态
    public bool targetValid;      // TargetTrackerLogic.valid
    public float dt;
}

public class InteractionStateMachine
{
    float _reGuideCooldownSec;
    readonly float _feedbackDurationSec;
    readonly float _feedbackCooldownSec;
    readonly float _idleTimeoutSec;
    readonly float _speechEndGraceSec;
    readonly float _lookAwayGraceSec;

    /// <summary>重新引导冷却（运行时可按干预档位调整：强3s/中5s/弱8s）。</summary>
    public float ReGuideCooldownSeconds
    {
        get => _reGuideCooldownSec;
        set
        {
            _reGuideCooldownSec = Mathf.Max(0f, value);
            // 档位切换即时生效：运行中的冷却等待按新上限收窄——切到更强档立即能更快重导
            if (_guideCooldownTimer > _reGuideCooldownSec)
                _guideCooldownTimer = _reGuideCooldownSec;
        }
    }

    float _guideCooldownTimer;
    float _feedbackCooldownTimer;
    float _silenceTime;         // 连续"没讲话"秒数
    float _notLookingTime;      // Hidden 中连续"没看"秒数
    bool _gazeAchievedThisHidden;

    public InteractionState State { get; private set; } = InteractionState.Idle;
    public float StateTime { get; private set; }
    public bool GuideTriggered { get; private set; }    // 本帧瞬态：进入了引导
    public bool FeedbackTriggered { get; private set; } // 本帧瞬态：进入了反馈

    public InteractionStateMachine(
        float reGuideCooldownSec = 3f,
        float feedbackDurationSec = 2f,
        float feedbackCooldownSec = 2f,
        float idleTimeoutSec = 10f,
        float speechEndGraceSec = 2f,
        float lookAwayGraceSec = 1f)
    {
        _reGuideCooldownSec = reGuideCooldownSec;
        _feedbackDurationSec = feedbackDurationSec;
        _feedbackCooldownSec = feedbackCooldownSec;
        _idleTimeoutSec = idleTimeoutSec;
        _speechEndGraceSec = speechEndGraceSec;
        _lookAwayGraceSec = lookAwayGraceSec;
    }

    public void Reset()
    {
        State = InteractionState.Idle;
        StateTime = 0f;
        _guideCooldownTimer = 0f;
        _feedbackCooldownTimer = 0f;
        _silenceTime = 0f;
        _notLookingTime = 0f;
        _gazeAchievedThisHidden = false;
        GuideTriggered = false;
        FeedbackTriggered = false;
    }

    void GoTo(InteractionState s)
    {
        if (s == InteractionState.Guiding)
        {
            GuideTriggered = true;
            _guideCooldownTimer = _reGuideCooldownSec;
        }
        if (s == InteractionState.Feedback)
        {
            FeedbackTriggered = true;
            // 反馈播完后还需再等 feedbackCooldownSec 才允许下一次引导
            _feedbackCooldownTimer = _feedbackDurationSec + _feedbackCooldownSec;
        }
        if (s == InteractionState.Hidden)
        {
            _gazeAchievedThisHidden = false;
            _notLookingTime = 0f;
        }
        State = s;
        StateTime = 0f;
    }

    public void Tick(InteractionInputs input)
    {
        float dt = input.dt > 0f ? input.dt : 1f / 30f;

        StateTime += dt;
        _guideCooldownTimer = Mathf.Max(0f, _guideCooldownTimer - dt);
        _feedbackCooldownTimer = Mathf.Max(0f, _feedbackCooldownTimer - dt);
        _silenceTime = input.speaking ? 0f : _silenceTime + dt;

        GuideTriggered = false;
        FeedbackTriggered = false;

        if (!input.targetValid)
        {
            if (State != InteractionState.Idle) GoTo(InteractionState.Idle);
            return;
        }

        switch (State)
        {
            case InteractionState.Idle:
                if (input.speaking && !input.looking &&
                    _guideCooldownTimer <= 0f && _feedbackCooldownTimer <= 0f)
                    GoTo(InteractionState.Guiding);
                break;

            case InteractionState.Guiding:
                if (input.gazeStarted) { GoTo(InteractionState.Hidden); break; }
                if (_silenceTime >= _idleTimeoutSec) GoTo(InteractionState.Idle);
                break;

            case InteractionState.Hidden:
            {
                if (input.gazeSustained) _gazeAchievedThisHidden = true;
                _notLookingTime = input.looking ? 0f : _notLookingTime + dt;

                // ① 正反馈：注视已达成，且语音结束的瞬间仍在看
                if (input.speechJustEnded && _gazeAchievedThisHidden && input.looking)
                {
                    GoTo(InteractionState.Feedback);
                    break;
                }

                if (!input.speaking)
                {
                    // ② 讲完了但没看够（或已移开）→ 宽限后回待机，不给反馈
                    if (_silenceTime >= _speechEndGraceSec)
                    {
                        if (!_gazeAchievedThisHidden || !input.looking) GoTo(InteractionState.Idle);
                        else GoTo(InteractionState.Feedback); // 兜底：达成且在看但没收到边沿
                    }
                    break;
                }

                // ③ 讲话仍在继续、孩子又看别处（超过抖动宽限 + 冷却）→ 温和重新引导
                if (_notLookingTime >= _lookAwayGraceSec && _guideCooldownTimer <= 0f)
                    GoTo(InteractionState.Guiding);
                break;
            }

            case InteractionState.Feedback:
                if (StateTime >= _feedbackDurationSec) GoTo(InteractionState.Idle);
                break;
        }
    }
}
