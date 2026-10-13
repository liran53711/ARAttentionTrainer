// Assets/Scripts/Core/M3SimScenario.cs
// 模拟测试机 · 场景生成器（2026-10-13）：脚本化 20+ 分钟真实课堂剧情 + 种子化噪声/抖动/掉帧，
// 作为 M3DebugDriver.sim 的输入源，在无设备情况下完整跑通全部交互逻辑。
// 剧情覆盖：开场吸引 / 锁定·注视·讲话循环 / 讲话结束反馈 / 短暂扫视 / 长时间离场（>20s）/
//           快速来回（极端时序）/ 检测框高频抖动 / 偶发掉帧（0.2~0.5s）。
using System.Collections.Generic;
using UnityEngine;

/// <summary>模拟输入接口：驱动层 Tick 每帧拉取。</summary>
public interface IM3SimInputs
{
    void Tick(float dt, out List<DetectionResult> detections, out bool looking,
              out bool speaking, out bool speechEnded, out bool reset);
}

public class M3SimScenario : IM3SimInputs
{
    public float TotalDuration { get; private set; }
    public string CurrentPhase => _segIdx < _segs.Count ? _segs[_segIdx].name : "END";

    struct Seg
    {
        public float dur; public bool face; public bool look; public bool speak; public float noise; public string name;
    }

    readonly List<Seg> _segs = new List<Seg>();
    readonly List<DetectionResult> _dets = new List<DetectionResult>();
    readonly List<DetectionResult> _empty = new List<DetectionResult>();
    readonly System.Random _rng;
    int _segIdx;
    float _segT;
    bool _speakingPrev;
    int _dropFrames;

    public M3SimScenario(int seed = 7)
    {
        _rng = new System.Random(seed);
        float S = 0f;
        void Add(float d, bool face, bool look, bool speak, float noise, string name)
        {
            _segs.Add(new Seg { dur = d, face = face, look = look, speak = speak, noise = noise, name = name });
            S += d;
        }

        // ── 开场（对齐状态机真实时序：讲话∧没看→引导；看向→隐藏；讲完仍在看→反馈）──
        Add(6f, false, false, false, 0f, "开场吸引·无脸");
        Add(3f, false, false, true, 0f, "老师开口·孩子还没看到（引导）");
        Add(5f, true, false, true, 0.012f, "老师入画·孩子没看（引导中）");
        Add(2f, true, false, true, 0.012f, "仍没看（等待视线）");
        Add(4f, true, true, true, 0.012f, "看向老师（→隐藏）");
        Add(4f, true, true, true, 0.012f, "持续注视");
        Add(2.5f, true, true, false, 0.012f, "讲完·仍在看（→正反馈）");
        Add(2f, true, false, false, 0.03f, "反馈后移开");

        // ── 循环轮：真实课堂闭环（没看+讲话→引导 / 看→隐藏 / 讲完→反馈）──
        int round = 0;
        while (S < 1150f)
        {
            Add(2.5f, true, false, true, 0.02f, $"R{round}·又讲话·没看（重引导）");
            Add(4.5f, true, true, true, 0.015f, $"R{round}·看回老师（隐藏）");
            Add(2.0f, true, true, false, 0.015f, $"R{round}·讲完（反馈）");
            Add(1.5f, true, false, false, 0.03f, $"R{round}·短暂移开");
            if (round % 7 == 3)
                Add(22f, false, false, false, 0f, $"R{round}·长离场22s（不许消失+方位提示）");
            if (round % 5 == 2)
            {
                Add(0.15f, true, true, false, 0.05f, $"R{round}·惊鸿一瞥（0.15s）");
                Add(3f, false, false, false, 0f, "离场3s");
            }
            if (round % 6 == 4)
                Add(1.2f, true, true, true, 0.05f, $"R{round}·抖动中注视");
            round++;
        }
        // ── 极端段：快速来回（视线 0.4s 周期切换 ×12）──
        for (int k = 0; k < 12; k++)
        {
            Add(0.4f, true, true, true, 0.06f, "极端·快速看向");
            Add(0.4f, true, false, true, 0.06f, "极端·快速移开");
        }
        Add(12f, true, false, true, 0.09f, "极端·高频抖动·没看");
        Add(10f, false, false, true, 0f, "收尾·离场（仍在讲话）");
        Add(12f, false, false, false, 0f, "收尾·长离场");

        TotalDuration = S;
        Debug.Log($"[SIM] 剧本就绪：{_segs.Count} 段 / 共 {TotalDuration / 60f:F1} 分钟 / 种子固定");
    }

    public void Tick(float dt, out List<DetectionResult> detections, out bool looking,
                     out bool speaking, out bool speechEnded, out bool reset)
    {
        reset = false;
        // 段推进
        _segT += dt;
        while (_segIdx < _segs.Count && _segT >= _segs[_segIdx].dur)
        {
            _segT -= _segs[_segIdx].dur;
            _segIdx++;
        }
        var seg = _segIdx < _segs.Count ? _segs[_segIdx] : default;

        // 掉帧模拟（偶发 0.2~0.5s 检测窗口无输出）
        if (_dropFrames > 0) _dropFrames--;
        else if (seg.face && _rng.NextDouble() < 0.004) _dropFrames = _rng.Next(6, 15);

        if (seg.face && _dropFrames == 0)
        {
            float n = seg.noise;
            var box = new Rect(
                Mathf.Clamp01(0.46f + J(n)),
                Mathf.Clamp01(0.24f + J(n)),
                Mathf.Max(0.06f, 0.24f + J(n * 0.5f)),
                Mathf.Max(0.12f, 0.42f + J(n)));
            _dets.Clear();
            _dets.Add(new DetectionResult { normalizedBox = box, confidence = 0.85f });
            detections = _dets;
        }
        else detections = _empty;

        looking = seg.look;
        speaking = seg.speak;
        speechEnded = _speakingPrev && !seg.speak;
        _speakingPrev = seg.speak;
    }

    float J(float n) => n <= 0f ? 0f : (float)((_rng.NextDouble() * 2.0 - 1.0) * n);
}
