// Assets/Scripts/Audio/VoiceArbiter.cs
// 语音仲裁器（2026-10-20，纯逻辑可测）：解决"同时好几条语音"的混乱。
// 规则（单通道语音状态机）：
//   · 同一时刻只有一条语音在播；
//   · 高优先级可打断低优先级（Critical > Guide > Ambient）；
//   · 同级/低级的 Guide 请求排队（最多 1 条，等当前说完）；Ambient 直接丢弃（不排队不打断）；
//   · 语音之间保留 GapSeconds 静默，避免连环轰炸。
using UnityEngine;

public enum VoicePriority { Critical = 0, Guide = 1, Ambient = 2 }

public class VoiceArbiter
{
    public float GapSeconds = 0.22f;
    public int MaxQueue = 1;

    float _busyUntil;
    int _currentPriority = 99;
    int _queuedPriority = -1;

    public bool IsBusy(float now) => now < _busyUntil;
    public int CurrentPriority => _currentPriority;
    public bool HasQueued => _queuedPriority >= 0;
    public int QueuedPriority => _queuedPriority;

    public void Reset() { _busyUntil = 0f; _currentPriority = 99; _queuedPriority = -1; }

    /// <summary>请求播放一条时长 duration 的语音。
    /// 返回 true=立即播放（如有正在播的会被打断，请先 Stop）；false=未播放（排队或丢弃）。</summary>
    public bool Request(int priority, float duration, float now)
    {
        if (now >= _busyUntil)
        {
            MarkPlaying(priority, duration, now);
            return true;
        }
        if (priority < _currentPriority)
        {
            // 高优先级：打断当前（清掉排队中的低优先）
            _queuedPriority = -1;
            MarkPlaying(priority, duration, now);
            return true;
        }
        // 同级或更低
        if (priority == (int)VoicePriority.Ambient) return false;      // 氛围语音：丢弃
        if (_queuedPriority < 0) _queuedPriority = priority;           // 排队（最多 1 条）
        return false;
    }

    /// <summary>轮询排队：若当前空闲且有排队，弹出排队优先级（调用方播放后需 MarkPlaying）。</summary>
    public bool TryDequeue(float now, out int priority)
    {
        priority = -1;
        if (_queuedPriority < 0 || now < _busyUntil) return false;
        priority = _queuedPriority;
        _queuedPriority = -1;
        return true;
    }

    /// <summary>标记开始播放某优先级的语音（占用通道 duration + 间隔）。</summary>
    public void MarkPlaying(int priority, float duration, float now)
    {
        _currentPriority = priority;
        _busyUntil = now + Mathf.Max(0f, duration) + GapSeconds;
    }

    /// <summary>清空排队（进入重要事件/成功时调用，避免旧的"跟我来"在成功后再播出来）。</summary>
    public void ClearQueue() => _queuedPriority = -1;
}
