// Assets/Scripts/Gaze/GazeLogic.cs
// 注视判定（纯逻辑）。Quest 3 无眼动追踪 → 用代理信号：
//   电脑端 = "目标点是否落在视口中心附近"（视口代理）
//   真机阶段 = 头显前向与目标方向夹角（逻辑同构，届时加一个静态方法）
// Dwell = 注视累计秒数：看着时累积、移开时按 decayRate 衰减（不硬清零，抗单帧抖动）。
// 输出两个"本帧瞬态"事件：GazeStarted（累计跨过 startSeconds）、GazeSustained（跨过 sustainSeconds）。
using UnityEngine;

public class GazeLogic
{
    float _startSeconds;
    readonly float _sustainSeconds;
    readonly float _decayRate;

    /// <summary>开始注视阈值（运行时可按干预档位调整；降低后未触发的阈值会自动解锁重试）。</summary>
    public float StartSeconds
    {
        get => _startSeconds;
        set => _startSeconds = Mathf.Max(0.05f, value);
    }

    public float Dwell { get; private set; }
    public bool Looking { get; private set; }
    public bool GazeStarted { get; private set; }
    public bool GazeSustained { get; private set; }

    bool _startedFired;
    bool _sustainedFired;

    public GazeLogic(float startSeconds = 0.8f, float sustainSeconds = 2.0f, float decayRate = 1f)
    {
        _startSeconds = startSeconds;
        _sustainSeconds = sustainSeconds;
        _decayRate = decayRate;
    }

    public void Reset()
    {
        Dwell = 0f;
        Looking = false;
        GazeStarted = false;
        GazeSustained = false;
        _startedFired = false;
        _sustainedFired = false;
    }

    /// <summary>视口代理：目标归一化坐标（左上原点）距中心(0.5,0.5)在 radius 内 → 视为在注视。</summary>
    public static bool IsLookingAt(Vector2 target01, float radius = 0.28f)
        => Vector2.Distance(target01, new Vector2(0.5f, 0.5f)) <= radius;

    public void Tick(bool looking, float dt)
    {
        if (dt <= 0f) dt = 1f / 30f;

        Looking = looking;
        Dwell = looking
            ? Dwell + dt
            : Mathf.Max(0f, Dwell - dt * _decayRate);

        GazeStarted = false;
        GazeSustained = false;

        // 跨过阈值时各触发一次
        if (!_startedFired && Dwell >= _startSeconds) { _startedFired = true; GazeStarted = true; }
        if (!_sustainedFired && Dwell >= _sustainSeconds) { _sustainedFired = true; GazeSustained = true; }

        // 掉回阈值以下后解锁，允许再次触发（"看→走神→重新看"的循环需要）
        if (Dwell < _startSeconds) _startedFired = false;
        if (Dwell < _sustainSeconds) _sustainedFired = false;
    }
}
