// Assets/Scripts/Core/ConversationGateLogic.cs
// 对话门控 · 纯逻辑（2026-10-24）：
//   · 外界有人讲话（老师/同学/孩子）→ 精灵语音全部延后，视觉引导照常；
//   · 静默 resumeDelay 后才重新放行语音；
//   · 系统自己的 TTS/音效会被麦克风拾取 → selfAudio 播放期间 + selfAudioTail 内忽略 VAD，
//     不把系统自己的声音当成"外界讲话"；
//   · Listening 用于精灵的扶耳倾听状态（与语音放行分离）。
using UnityEngine;

public class ConversationGateLogic
{
    /// <summary>当前是否允许精灵说话（外界静默且不在自音频尾内）。</summary>
    public bool VoiceAllowed { get; private set; } = true;
    /// <summary>是否处于"外界有人讲话、应该倾听"的状态。</summary>
    public bool Listening { get; private set; }
    /// <summary>有效外界讲话（已排除自音频/自音频尾）。</summary>
    public bool ExternalSpeech { get; private set; }
    /// <summary>当前是否因系统自己的声音而忽略 VAD。</summary>
    public bool SelfAudioSuppressing { get; private set; }

    readonly float _resumeDelay;
    readonly float _selfTail;
    float _silenceT;
    float _selfT;

    public ConversationGateLogic(float resumeDelay = 0.5f, float selfAudioTail = 0.30f)
    {
        _resumeDelay = Mathf.Max(0.05f, resumeDelay);
        _selfTail = Mathf.Max(0f, selfAudioTail);
    }

    public void Reset()
    {
        VoiceAllowed = true;
        Listening = false;
        ExternalSpeech = false;
        SelfAudioSuppressing = false;
        _silenceT = 0f;
        _selfT = 0f;
    }

    /// <summary>每帧推进。vadSpeaking 来自 MicVadService/模拟输入；selfAudioPlaying 来自
    /// VoiceCuePlayer/AudioCuePlayer 的实际播放状态。</summary>
    public void Tick(float dt, bool vadSpeaking, bool selfAudioPlaying)
    {
        if (dt < 0f) dt = 0f;

        if (selfAudioPlaying) _selfT = _selfTail;
        else if (_selfT > 0f) _selfT = Mathf.Max(0f, _selfT - dt);
        SelfAudioSuppressing = selfAudioPlaying || _selfT > 0f;

        if (SelfAudioSuppressing)
        {
            // 自己的扬声器正在响（或刚响完）：VAD 不可信，也不放行新语音
            ExternalSpeech = false;
            Listening = false;
            VoiceAllowed = false;
            _silenceT = 0f;
            return;
        }

        ExternalSpeech = vadSpeaking;
        Listening = vadSpeaking;
        if (vadSpeaking)
        {
            _silenceT = 0f;
            VoiceAllowed = false;
        }
        else
        {
            _silenceT += dt;
            VoiceAllowed = _silenceT >= _resumeDelay;
        }
    }
}
