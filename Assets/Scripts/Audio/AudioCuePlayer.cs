// Assets/Scripts/Audio/AudioCuePlayer.cs
// 音效播放器（2026-10-20 扩展流星引导音效）：全部正弦合成，零素材依赖。
//   · 原有：引导出现 / 隐藏 / 反馈
//   · 流星"咻"（扫频）、星星"叮"（8 音阶上行——第 N 颗星升 N 阶）、流星雨（琶音+低音铺垫）
// 真机阶段 spatialBlend 设 1 → Quest 空间音频渲染在 3D 位置（音效暂用普通播放）。
using UnityEngine;

public enum AudioCue { GuideAppear, Hide, Feedback, Celebrate, MeteorWhoosh, MeteorShower }

public class AudioCuePlayer : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.6f;

    [Tooltip("0=普通播放（电脑调试）；1=空间音频（真机阶段）")]
    [Range(0f, 1f)] public float spatialBlend = 0f;

    AudioSource _source;
    /// <summary>音效是否正在播放（对话门控的自音频输入）。</summary>
    public bool IsPlaying => _source != null && _source.isPlaying;
    AudioClip _guide, _hide, _feedback, _celebrate;
    AudioClip _whoosh, _showerBass, _showerArp;
    AudioClip[] _chimes; // 8 个音阶

    // 星星音阶（C5 大调音阶上行，8 颗星正好一个八度）
    static readonly float[] ChimeHz = { 523f, 587f, 659f, 698f, 784f, 880f, 988f, 1047f };

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
        _celebrate = BuildTone("cue_celebrate", new[]                                           // 庆典小号角
            { (523f, 0.09f), (659f, 0.09f), (784f, 0.09f), (1047f, 0.20f), (1319f, 0.10f), (1047f, 0.38f) });

        // 流星"咻"：高频下滑扫频
        _whoosh = BuildSweep("cue_whoosh", 1400f, 420f, 0.22f);

        // 星尘落星的"叮"：8 音阶（每颗星升一度；加一点泛音让它像铃声）
        _chimes = new AudioClip[ChimeHz.Length];
        for (int i = 0; i < ChimeHz.Length; i++)
            _chimes[i] = BuildTone("cue_chime" + i, new[] { (ChimeHz[i], 0.10f), (ChimeHz[i] * 2f, 0.16f) }, 0.32f);

        // 流星雨：低音铺垫 + 琶音（两个 clip 同时播放）
        _showerBass = BuildTone("cue_shower_bass", new[] { (131f, 0.55f), (196f, 0.55f) }, 0.25f);
        _showerArp = BuildTone("cue_shower_arp", new[]
        {
            (523f, 0.08f), (659f, 0.08f), (784f, 0.08f), (1047f, 0.10f),
            (784f, 0.08f), (1047f, 0.08f), (1319f, 0.10f), (1568f, 0.26f)
        });
    }

    public void PlayCue(AudioCue cue)
    {
        AudioClip clip = cue switch
        {
            AudioCue.GuideAppear => _guide,
            AudioCue.Hide => _hide,
            AudioCue.MeteorWhoosh => _whoosh,
            AudioCue.Celebrate => _celebrate != null ? _celebrate : _feedback,
            _ => _feedback,
        };
        if (clip != null) _source.PlayOneShot(clip);
    }

    /// <summary>星星落地音：index 0..7 依次升阶（集满像音阶上行）。</summary>
    public void PlayStarChime(int index)
    {
        if (_chimes == null || _chimes.Length == 0) return;
        var clip = _chimes[Mathf.Clamp(index, 0, _chimes.Length - 1)];
        if (clip != null) _source.PlayOneShot(clip);
    }

    /// <summary>流星雨：低音 + 琶音同时播放。</summary>
    public void PlayMeteorShower()
    {
        if (_showerBass != null) _source.PlayOneShot(_showerBass);
        if (_showerArp != null) _source.PlayOneShot(_showerArp);
    }

    /// <summary>运行时调整音量（干预档位联动：强 1.0 / 中 0.6 / 弱 0.15）。</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (_source != null) _source.volume = volume;
    }

    /// <summary>把一串 (频率, 时长) 合成为一个音频片段；每段带 20ms 淡入淡出防爆音。</summary>
    static AudioClip BuildTone(string name, (float freq, float dur)[] notes, float amp = 0.5f)
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
                data[offset + i] = amp * env * Mathf.Sin(2f * Mathf.PI * n.freq * i / sampleRate);
            }
            offset += len;
        }

        var clip = AudioClip.Create(name, total, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>扫频音（流星划过"咻"）：频率 f0 → f1 平滑下滑。</summary>
    static AudioClip BuildSweep(string name, float f0, float f1, float dur, float amp = 0.35f)
    {
        const int sampleRate = 44100;
        int len = Mathf.CeilToInt(dur * sampleRate);
        var data = new float[len];
        float phase = 0f;
        for (int i = 0; i < len; i++)
        {
            float k = i / (float)len;
            float f = Mathf.Lerp(f0, f1, k * k);
            phase += 2f * Mathf.PI * f / sampleRate;
            float env = Mathf.Min(1f, i / (0.02f * sampleRate)) * (1f - k);
            data[i] = amp * env * Mathf.Sin(phase);
        }
        var clip = AudioClip.Create(name, len, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
