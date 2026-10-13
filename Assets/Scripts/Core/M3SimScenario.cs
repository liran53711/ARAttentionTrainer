// Assets/Scripts/Core/M3SimScenario.cs
// 模拟测试机 · 场景生成器（2026-10-13）：脚本化 20+ 分钟真实课堂剧情 + 种子化噪声/抖动/掉帧，
// 作为 M3DebugDriver.sim 的输入源，在无设备情况下完整跑通全部交互逻辑。
// 剧情覆盖：开场吸引 / 锁定·注视·讲话循环 / 讲话结束反馈 / 短暂扫视 / 长时间离场（>20s）/
//           快速来回（极端时序）/ 检测框高频抖动 / 偶发掉帧（0.2~0.5s）。
// 2026-10-13 扩展（人脸居中奖励）：独立"人脸框"通道（检测到老师脸的位置）——
//   居中度定义：框中心距 (0.5,0.5) 的距离；居中≈0.01 / 偏离≈0.31（越过滞回带，确保衰减）。
//   专用桥段：A1 正视老师（持续居中 20s+ → 必达庆典）、B1 虚晃一枪（16s 后离开 → 不达标）、
//   A2 中断后仍达标（12s 居中 + 2s 丢失 + 16s 居中 → 恰在 +14s 处达标，验证"衰减不重置"）。
using System.Collections.Generic;
using UnityEngine;

/// <summary>模拟输入接口：驱动层 Tick 每帧拉取。</summary>
public interface IM3SimInputs
{
    void Tick(float dt, out List<DetectionResult> detections, out List<DetectionResult> faces,
              out bool looking, out bool speaking, out bool speechEnded, out bool reset);
}

public class M3SimScenario : IM3SimInputs
{
    public float TotalDuration { get; private set; }
    public string CurrentPhase => _segIdx < _segs.Count ? _segs[_segIdx].name : "END";

    // ── 人脸奖励桥段时间轴（harness 断言用；单位=场景秒）──
    public float A1CenterStartSec = -1f;   // A1 持续居中开始 → 期望 +faceCelebrateSeconds(20s) 达标
    public float A2Seq3StartSec = -1f;     // A2 第三段（重建居中）开始 → 期望 +14s 达标（6s剩余+14=20）

    struct Seg
    {
        public float dur; public bool face; public bool look; public bool speak; public float noise; public string name;
        public bool faceBox; public float fcx, fcy;   // 人脸框（检测到的老师脸）：中心归一化坐标
    }

    readonly List<Seg> _segs = new List<Seg>();
    readonly List<DetectionResult> _dets = new List<DetectionResult>();
    readonly List<DetectionResult> _faces = new List<DetectionResult>();
    readonly List<DetectionResult> _empty = new List<DetectionResult>();
    readonly System.Random _rng;
    int _segIdx;
    float _segT;
    bool _speakingPrev;
    int _dropFrames;

    // 人脸框尺寸（典型课堂人脸框）
    const float FW = 0.22f, FH = 0.32f;
    // 居中 / 偏离 位置（偏离距离 0.312 > radius×hysteresis=0.286，确保进入衰减而不是滞回冻结）
    const float CEN_X = 0.50f, CEN_Y = 0.49f;
    const float OFF_X = 0.26f, OFF_Y = 0.30f;

    public M3SimScenario(int seed = 7)
    {
        _rng = new System.Random(seed);
        float S = 0f;
        void Add(float d, bool face, bool look, bool speak, float noise, string name,
                 bool faceBox = false, float fcx = OFF_X, float fcy = OFF_Y)
        {
            _segs.Add(new Seg { dur = d, face = face, look = look, speak = speak, noise = noise, name = name,
                                faceBox = faceBox, fcx = fcx, fcy = fcy });
            S += d;
        }
        // 人脸框"居中"快捷参数
        const float CX = CEN_X, CY = CEN_Y;

        // ── 开场（对齐状态机真实时序：讲话∧没看→引导；看向→隐藏；讲完仍在看→反馈）──
        Add(6f, false, false, false, 0f, "开场吸引·无脸");
        Add(3f, false, false, true, 0f, "老师开口·孩子还没看到（引导）");
        Add(5f, true, false, true, 0.012f, "老师入画·孩子没看（引导中）", true);                 // 脸偏离：孩子没看老师
        Add(2f, true, false, true, 0.012f, "仍没看（等待视线）", true);
        Add(4f, true, true, true, 0.012f, "看向老师（→隐藏）", true, CX, CY);                    // 脸居中：孩子看老师
        Add(4f, true, true, true, 0.012f, "持续注视", true, CX, CY);
        Add(2.5f, true, true, false, 0.012f, "讲完·仍在看（→正反馈）", true, CX, CY);            // 开场居中 10.5s < 20s（安全）
        Add(2f, true, false, false, 0.03f, "反馈后移开", true);

        // ── 循环轮：真实课堂闭环（没看+讲话→引导 / 看→隐藏 / 讲完→反馈）──
        int round = 0;
        while (S < 1150f)
        {
            Add(4.5f, true, false, true, 0.02f, $"R{round}·又讲话·没看（重引导）", true);
            Add(4.5f, true, true, true, 0.015f, $"R{round}·看回老师（隐藏）", true, CX, CY);      // 居中 4.5s
            Add(2.0f, true, true, false, 0.015f, $"R{round}·讲完（反馈）", true, CX, CY);         // 居中 2.0s（连起来 6.5s < 20s）
            Add(2.5f, true, false, false, 0.03f, $"R{round}·短暂移开", true);
            if (round % 7 == 3)
                Add(22f, false, false, false, 0f, $"R{round}·长离场22s（不许消失+方位提示）");
            if (round % 5 == 2)
            {
                Add(0.15f, true, true, false, 0.05f, $"R{round}·惊鸿一瞥（0.15s）", true, CX, CY);
                Add(3f, false, false, false, 0f, "离场3s");
            }
            if (round % 6 == 4)
                Add(1.2f, true, true, true, 0.05f, $"R{round}·抖动中注视", true, CX, CY);

            // ── 人脸奖励专用桥段（2026-10-13）──
            //  每个桥段前先"清场 3s"（偏离视线，把基础轮次残留的累计衰减到 0）→ 桥段时间轴精确确定。
            if (round == 12)
            {
                Add(3f, true, false, true, 0.03f, "A1·清场3s（累计清零）", true);
                A1CenterStartSec = S;
                Add(24f, true, true, true, 0.01f, "A1·正视老师·人脸持续居中（→+20s 达标庆典）", true, CX, CY);
                Add(4f, true, true, true, 0.02f, "A1b·庆典余韵", true, CX, CY);
                Add(3f, true, false, true, 0.03f, "A1c·移开视线", true);
            }
            if (round == 20)
            {
                // 虚晃一枪：清场后 16s 居中 → 离开 → 衰减 → 差 4s 不达标（里程碑 5/10/15 照发）
                Add(3f, true, false, true, 0.03f, "B1·清场3s（累计清零）", true);
                Add(16f, true, true, true, 0.01f, "B1·坚持16s（接近达标）", true, CX, CY);
                Add(4f, true, false, true, 0.03f, "B1b·功亏一篑（离开视线）", true);
            }
            if (round == 28)
            {
                // 中断后仍达标：清场 → 12s 居中 → 2s 丢失（衰减到 6）→ 第 3 段 +14s 处 = 20s 达标
                Add(3f, true, false, true, 0.03f, "A2·清场3s（累计清零）", true);
                Add(12f, true, true, true, 0.012f, "A2·第一段居中12s", true, CX, CY);
                Add(2f, true, false, true, 0.03f, "A2b·眨眼丢失2s（衰减到6s）", true);
                A2Seq3StartSec = S;
                Add(16f, true, true, true, 0.012f, "A2c·重建居中（→+14s 处达标）", true, CX, CY);
            }
            round++;
        }
        // ── 极端段：快速来回（视线 0.4s 周期切换 ×12）──
        for (int k = 0; k < 12; k++)
        {
            Add(0.4f, true, true, true, 0.06f, "极端·快速看向", true, CX, CY);
            Add(0.4f, true, false, true, 0.06f, "极端·快速移开", true);
        }
        Add(12f, true, false, true, 0.09f, "极端·高频抖动·没看", true);
        Add(10f, false, false, true, 0f, "收尾·离场（仍在讲话）");
        Add(12f, false, false, false, 0f, "收尾·长离场");

        TotalDuration = S;
        Debug.Log($"[SIM] 剧本就绪：{_segs.Count} 段 / 共 {TotalDuration / 60f:F1} 分钟 / 种子固定" +
                  $"（A1 居中@{A1CenterStartSec:F1}s / A2 重建@{A2Seq3StartSec:F1}s）");
    }

    public void Tick(float dt, out List<DetectionResult> detections, out List<DetectionResult> faces,
                     out bool looking, out bool speaking, out bool speechEnded, out bool reset)
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

        // 人脸框通道（BlazeFace 语义：检测到老师脸 → 框；中心=fcx,fcy ± 小抖动）
        // 注：不受 _dropFrames 影响——真实语义中人体检测停顿 ≠ 人脸检测停顿（各自的推理循环独立）
        if (seg.faceBox)
        {
            float j = seg.noise * 0.25f;
            float cx = Mathf.Clamp01(seg.fcx + J(j));
            float cy = Mathf.Clamp01(seg.fcy + J(j));
            _faces.Clear();
            _faces.Add(new DetectionResult
            {
                normalizedBox = new Rect(cx - FW * 0.5f, cy - FH * 0.5f, FW, FH),
                confidence = 0.92f,
            });
            faces = _faces;
        }
        else faces = _empty;

        looking = seg.look;
        speaking = seg.speak;
        speechEnded = _speakingPrev && !seg.speak;
        _speakingPrev = seg.speak;
    }

    float J(float n) => n <= 0f ? 0f : (float)((_rng.NextDouble() * 2.0 - 1.0) * n);
}
