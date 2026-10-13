// Assets/Scripts/Audio/VoiceCuePlayer.cs
// 语音指导：播放预生成的语音片段（Assets/Resources/Voice/*.mp3）。
// Quest 无系统 TTS 引擎 → 预生成音频（edge-tts，年轻女声 zh-CN-XiaoyiNeural）。
// 后续替换为真人录音：保持同名同路径即可，本组件无需改动。
// 片段缺失时静默跳过（由 AudioCuePlayer 的音效兜底）。
using UnityEngine;

public enum VoiceCue { GuideAppear, Find, Feedback, Peek, Celebrate }

public class VoiceCuePlayer : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.9f;
    [Tooltip("0=普通播放；真机设为空间音频（声音从目标方位传来）")]
    [Range(0f, 1f)] public float spatialBlend = 0.6f;

    AudioSource _source;
    AudioClip _guide, _find, _feedback, _peek, _celebrate;

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.volume = volume;
        _source.spatialBlend = spatialBlend;
        _source.spatialize = spatialBlend > 0.5f;

        _guide = Resources.Load<AudioClip>("Voice/voice_guide");
        _find = Resources.Load<AudioClip>("Voice/voice_find");
        _feedback = Resources.Load<AudioClip>("Voice/voice_feedback");
        _peek = Resources.Load<AudioClip>("Voice/voice_peek");
        _celebrate = Resources.Load<AudioClip>("Voice/voice_celebrate");

        int n = (_guide != null ? 1 : 0) + (_find != null ? 1 : 0)
              + (_feedback != null ? 1 : 0) + (_peek != null ? 1 : 0)
              + (_celebrate != null ? 1 : 0);
        Debug.Log($"[Voice] 语音片段加载 {n}/5" + (n < 5 ? "（缺失的将只有音效）" : ""));
    }

    /// <summary>运行时调整音量（干预档位联动）。</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (_source != null) _source.volume = volume;
    }

    /// <summary>播放语音；片段缺失则不响。worldPos 供空间音频定位（声音从该方位传来）。</summary>
    public void Play(VoiceCue cue, Vector3 worldPos)
    {
        AudioClip clip = cue switch
        {
            VoiceCue.GuideAppear => _guide,
            VoiceCue.Find => _find,
            VoiceCue.Feedback => _feedback,
            VoiceCue.Celebrate => _celebrate != null ? _celebrate : _feedback, // 庆典语音缺失时用反馈语音兜底
            _ => _peek,
        };
        if (clip == null) return;
        transform.position = worldPos;
        _source.PlayOneShot(clip);
    }
}
