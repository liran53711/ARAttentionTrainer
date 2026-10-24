// Assets/Scripts/Audio/VoiceCuePlayer.cs
// 语音播放器（2026-10-20：流星引导话术 + 语音仲裁）：
//   · 预生成语音片段（Assets/Resources/Voice/*.wav/mp3），WAV 由 Windows OneCore 合成（Microsoft Yaoyao）
//   · **语音仲裁**：同一时刻只播一条（VoiceArbiter：优先级/打断/排队/间隔）——修复"同时好几条语音"
//   · 正式替换为真人录音：保持同名同路径即可，本组件无需改动。
using System.Collections.Generic;
using UnityEngine;

public enum VoiceCue
{
    GuideAppear, Find, Feedback, Peek,                 // 原有（引导出现/找老师/正反馈/陪伴）
    Start,                                             // 开场吸引
    Confused, Found, Lead1, Lead2, Lead3, Success,     // 找老师三段式（第1-3轮强化）
    StarChase, StarLead, StarFirst,                    // 追星星话术
    MeteorShower, Celebrate,                           // 流星雨/庆典
    Rest, Hum,                                         // 静默休息/陪伴哼歌
    SearchStart, SearchLook, SearchHere, SearchAgain, SearchWhere, SearchFound // 首寻 3D 漫游
}

public class VoiceCuePlayer : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.9f;
    [Tooltip("0=普通播放；真机设为空间音频（声音从目标方位传来）")]
    [Range(0f, 1f)] public float spatialBlend = 0.6f;

    AudioSource _source;
    readonly Dictionary<VoiceCue, AudioClip> _clips = new Dictionary<VoiceCue, AudioClip>();
    readonly VoiceArbiter _arbiter = new VoiceArbiter();

    /// <summary>当前是否正在播放语音（对话门控的"自音频"输入）。</summary>
    public bool IsPlaying => _source != null && _source.isPlaying;

    // 排队中的语音（仲裁器只存优先级，这里存对应的 cue/位置）
    VoiceCue _queuedCue;
    Vector3 _queuedPos;
    bool _hasQueuedCue;

    // 对话门控：外界讲话期间挂起一条（Critical/Guide），静默后最多补播 2s 内的一条
    bool _gateBlocked;
    VoiceCue _gatedCue;
    Vector3 _gatedPos;
    int _gatedPriority;
    float _gatedAt;
    bool _hasGated;

    // 资源文件名映射（Resources/Voice/ 下，不带扩展名；wav/mp3 都可）
    static readonly (VoiceCue cue, string file)[] Files =
    {
        (VoiceCue.GuideAppear, "voice_guide"),
        (VoiceCue.Find,        "voice_find"),
        (VoiceCue.Feedback,    "voice_feedback"),
        (VoiceCue.Peek,        "voice_peek"),
        (VoiceCue.Start,       "voice_start"),
        (VoiceCue.Confused,    "voice_confused"),
        (VoiceCue.Found,       "voice_found"),
        (VoiceCue.Lead1,       "voice_lead1"),
        (VoiceCue.Lead2,       "voice_lead2"),
        (VoiceCue.Lead3,       "voice_lead3"),
        (VoiceCue.Success,     "voice_success"),
        (VoiceCue.StarChase,   "voice_star_chase"),
        (VoiceCue.StarLead,    "voice_star_lead"),
        (VoiceCue.StarFirst,   "voice_star_first"),
        (VoiceCue.MeteorShower,"voice_meteor_shower"),
        (VoiceCue.Celebrate,   "voice_celebrate"),   // 可能缺失 → 回退 meteor_shower
        (VoiceCue.Rest,        "voice_rest"),
        (VoiceCue.Hum,         "voice_hum"),
        (VoiceCue.SearchStart, "voice_search_start"),
        (VoiceCue.SearchLook,  "voice_search_look"),
        (VoiceCue.SearchHere,  "voice_search_here"),
        (VoiceCue.SearchAgain, "voice_search_again"),
        (VoiceCue.SearchWhere, "voice_search_where"),
        (VoiceCue.SearchFound, "voice_search_found"),
    };

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.volume = volume;
        _source.spatialBlend = spatialBlend;
        _source.spatialize = spatialBlend > 0.5f;
        _source.loop = false;

        int n = 0;
        foreach (var (cue, file) in Files)
        {
            var clip = Resources.Load<AudioClip>("Voice/" + file);
            if (clip != null) { _clips[cue] = clip; n++; }
        }
        Debug.Log($"[Voice] 语音片段加载 {n}/{Files.Length}" + (n < Files.Length ? "（缺失的将静默/回退音效）" : ""));
    }

    /// <summary>运行时调整音量（干预档位联动）。</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (_source != null) _source.volume = volume;
    }

    /// <summary>播放语音（经仲裁：可能打断低优先级 / 排队 / 被丢弃）。worldPos 供空间定位。</summary>
    public void Play(VoiceCue cue, Vector3 worldPos, VoicePriority priority = VoicePriority.Guide)
    {
        if (_source == null) return;
        var clip = Resolve(cue);
        if (clip == null) return;
        if (_gateBlocked)
        {
            // 外界讲话中：氛围语音直接丢；Critical/Guide 只挂起最新一条，静默后补播
            if (priority == VoicePriority.Ambient) return;
            _gatedCue = cue;
            _gatedPos = worldPos;
            _gatedPriority = (int)priority;
            _gatedAt = Time.realtimeSinceStartup;
            _hasGated = true;
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (_arbiter.Request((int)priority, clip.length, now))
        {
            PlayNow(clip, worldPos, priority);
        }
        else if (_arbiter.HasQueued && !_hasQueuedCue)
        {
            // 排队成功（仲裁器只保留 1 条）：缓存等待
            _queuedCue = cue;
            _queuedPos = worldPos;
            _hasQueuedCue = true;
        }
    }

    /// <summary>对话门控：外界讲话时 allowed=false（挂起 Critical/Guide，丢 Ambient）；
    /// 静默恢复时补播 2 秒内挂起的一条。</summary>
    public void SetGateAllowed(bool allowed)
    {
        if (!allowed)
        {
            _gateBlocked = true;
            return;
        }
        _gateBlocked = false;
        if (_hasGated && Time.realtimeSinceStartup - _gatedAt <= 2f)
        {
            var cue = _gatedCue;
            var pos = _gatedPos;
            var pr = _gatedPriority;
            _hasGated = false;
            Play(cue, pos, (VoicePriority)pr);
        }
        else _hasGated = false;
    }

    /// <summary>清空排队语音（成功/庆典等关键时刻调用——避免旧的引导语音事后播出）。</summary>
    public void ClearQueue()
    {
        _hasQueuedCue = false;
        _hasGated = false;
        _arbiter.ClearQueue();
    }

    void Update()
    {
        if (_source == null) return;
        float now = Time.realtimeSinceStartup;
        if (_hasQueuedCue && _arbiter.TryDequeue(now, out int pr))
        {
            var clip = Resolve(_queuedCue);
            _hasQueuedCue = false;
            if (clip != null)
            {
                PlayNow(clip, _queuedPos, (VoicePriority)pr);
                _arbiter.MarkPlaying(pr, clip.length, now);
            }
        }
    }

    void PlayNow(AudioClip clip, Vector3 worldPos, VoicePriority priority)
    {
        _source.Stop();                       // 单通道：打断当前
        transform.position = worldPos;
        _source.clip = clip;
        _source.Play();
    }

    AudioClip Resolve(VoiceCue cue)
    {
        if (_clips.TryGetValue(cue, out var clip) && clip != null) return clip;
        // 回退链：庆典系列兜底
        if (cue == VoiceCue.Celebrate || cue == VoiceCue.MeteorShower)
        {
            if (_clips.TryGetValue(VoiceCue.MeteorShower, out clip)) return clip;
            if (_clips.TryGetValue(VoiceCue.Feedback, out clip)) return clip;
        }
        if (_clips.TryGetValue(VoiceCue.Feedback, out clip)) return clip;
        return null;
    }
}
