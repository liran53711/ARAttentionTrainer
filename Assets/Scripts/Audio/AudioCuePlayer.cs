// Assets/Scripts/Audio/AudioCuePlayer.cs
// 音效播放器：引导出现 / 隐藏 / 反馈 三种提示音，运行时正弦波合成（零素材依赖）。
// 真机阶段 spatialBlend 设 1 → 由 Quest 空间音频渲染在 3D 位置；电脑调试设 0。
using UnityEngine;

public enum AudioCue { GuideAppear, Hide, Feedback }

public class AudioCuePlayer : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.6f;

    [Tooltip("0=普通播放（电脑调试）；1=空间音频（真机阶段）")]
    [Range(0f, 1f)] public float spatialBlend = 0f;

    AudioSource _source;
    AudioClip _guide, _hide, _feedback;

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.volume = volume;
        _source.spatialBlend = spatialBlend;
        _source.spatialize = spatialBlend > 0f;

        _guide = BuildTone("cue_guide", new[] { (520f, 0.12f), (780f, 0.16f) });          // 上行双音：出现
        _hide = BuildTone("cue_hide", new[] { (660f, 0.12f), (440f, 0.20f) });            // 下行软音：隐藏
        _feedback = BuildTone("cue_feedback", new[] { (523f, 0.10f), (659f, 0.10f), (784f, 0.18f) }); // C-E-G 琶音：正向反馈
    }

    public void PlayCue(AudioCue cue)
    {
        AudioClip clip = cue switch
        {
            AudioCue.GuideAppear => _guide,
            AudioCue.Hide => _hide,
            _ => _feedback,
        };
        if (clip != null) _source.PlayOneShot(clip);
    }

    /// <summary>运行时调整音量（干预档位联动：强 1.0 / 中 0.6 / 弱 0.15）。</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (_source != null) _source.volume = volume;
    }

    /// <summary>把一串 (频率, 时长) 合成为一个音频片段；每段带 20ms 淡入淡出防爆音。</summary>
    static AudioClip BuildTone(string name, (float freq, float dur)[] notes)
    {
        const int sampleRate = 44100;
        int total = 0;
        foreach (var n in notes) total += Mathf.CeilToInt(n.dur * sampleRate);

        var data = new float[total];
        int offset = 0;
        int fade = Mathf.Max(1, Mathf.CeilToInt(0.02f * sampleRate));
        foreach (var n in notes)
        {
            int len = Mathf.CeilToInt(n.dur * sampleRate);
            for (int i = 0; i < len; i++)
            {
                float env = Mathf.Min(1f, Mathf.Min(i, len - 1 - i) / (float)fade);
                data[offset + i] = 0.5f * env * Mathf.Sin(2f * Mathf.PI * n.freq * i / sampleRate);
            }
            offset += len;
        }

        var clip = AudioClip.Create(name, total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
