// Assets/Scripts/Core/M3SimScenario.cs
// 模拟测试机 · 场景生成器（2026-10-13）：脚本化 20+ 分钟真实课堂剧情 + 种子化噪声/抖动/掉帧，
// 作为 M3DebugDriver.sim 的输入源，在无设备情况下完整跑通全部交互逻辑。
// 剧情覆盖：开场吸引 / 锁定·注视·讲话循环 / 讲话结束反馈 / 短暂扫视 / 长时间离场（>20s）/
//           快速来回（极端时序）/ 检测框高频抖动 / 偶发掉帧（0.2~0.5s）。
// 人脸奖励（2026-10-13 扩展）：人脸框与人体框**物理绑定**——
//   · 居中 = 人框在画面中央、脸自动在框顶（face = 人框中心 + (−0.08,−0.12)，距中心 ≈0.17 < 0.22）
//   · 偏离 = 孩子转头 → 人框连同脸一起移到画面左侧（距中心 ≈0.36 > 滞回带 0.286，确保衰减）
//   · 桥段：A1 持续居中（→+20s 必达庆典）/ B1 16s 后离开（不达标）/ A2 12s+丢脸2s+16s（+14s 达标）/
//           C1 海报假脸居中 20s 但人框在别处（归属过滤 → 不累计不触发）/
//           C2 老师走动 15s（人框+脸同步移动，居中保持应正常累计）
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
    public float A1CenterStartSec = -1f;   // A1 持续居中开始 → 期望 +20s 达标
    public float A2Seq3StartSec = -1f;     // A2 第三段（重建居中）开始 → 期望 +14s 达标
    public float C1StartSec = -1f;         // C1 海报假脸居中开始 → 期望不累计不触发
    public float C2StartSec = -1f;         // C2 老师走动开始 → 期望正常累计（+15s 出现 Hold15）

    struct Seg
    {
        public float dur; public bool face; public bool look; public bool speak; public float noise; public string name;
        public bool faceBox; public float fcx, fcy;   // 人脸框：<0 = 自动跟随人框顶部
        public float pbx, pby;                        // 人体框中心（归一化）
    }

    readonly List<Seg> _segs = new List<Seg>();
    readonly List<DetectionResult> _dets = new List<DetectionResult>();
    readonly List<DetectionResult> _faces = new List<DetectionResult>();
    readonly List<DetectionResult> _empty = new List<DetectionResult>();
    List<DetectionResult> _lastDetCache;   // 掉帧期间挂起的旧检测（模拟真实 Latest 保留语义）
    readonly System.Random _rng;
    int _segIdx;
    float _segT;
    bool _speakingPrev;
    int _dropFrames;

    // 人脸框尺寸（典型课堂人脸框）
    const float FW = 0.22f, FH = 0.32f;
    // 人体框尺寸（与旧版一致：中心 (0.58,0.45)，宽 0.24 高 0.42）
    const float PW = 0.24f, PH = 0.42f;
    // "偏离" = 孩子转头 → 人框移到画面左侧
    const float LEFT_X = 0.30f, LEFT_Y = 0.40f;

    public M3SimScenario(int seed = 7)
    {
        _rng = new System.Random(seed);
        float S = 0f;
        void Add(float d, bool face, bool look, bool speak, float noise, string name,
                 bool faceBox = false, float fcx = -1f, float fcy = -1f, float pbx = 0.58f, float pby = 0.45f)
        {
            _segs.Add(new Seg { dur = d, face = face, look = look, speak = speak, noise = noise, name = name,
                                faceBox = faceBox, fcx = fcx, fcy = fcy, pbx = pbx, pby = pby });
            S += d;
        }
        // 快捷参数：居中=人框默认位+脸自动；偏离=人框左移
        const float LX = LEFT_X, LY = LEFT_Y;

        // ── 开场（对齐状态机真实时序：讲话∧没看→引导；看向→隐藏；讲完仍在看→反馈）──
        Add(6f, false, false, false, 0f, "开场吸引·无脸");
        Add(3f, false, false, true, 0f, "老师开口·孩子还没看到（引导）");
        Add(5f, true, false, true, 0.012f, "老师入画·孩子没看（引导中）", true, -1, -1, LX, LY);
        Add(2f, true, false, true, 0.012f, "仍没看（等待视线）", true, -1, -1, LX, LY);
        Add(4f, true, true, true, 0.012f, "看向老师（→隐藏）", true);                    // 居中（脸距中心≈0.17）
        Add(4f, true, true, true, 0.012f, "持续注视", true);
        Add(2.5f, true, true, false, 0.012f, "讲完·仍在看（→正反馈）", true);            // 开场居中 10.5s < 20s（安全）
        Add(2f, true, false, false, 0.03f, "反馈后移开", true, -1, -1, LX, LY);

        // ── 循环轮：真实课堂闭环（没看+讲话→引导 / 看→隐藏 / 讲完→反馈）──
        int round = 0;
        while (S < 1150f)
        {
            Add(4.5f, true, false, true, 0.02f, $"R{round}·又讲话·没看（重引导）", true, -1, -1, LX, LY);
            Add(4.5f, true, true, true, 0.015f, $"R{round}·看回老师（隐藏）", true);              // 居中 4.5s
            Add(2.0f, true, true, false, 0.015f, $"R{round}·讲完（反馈）", true);                 // 居中 2.0s（连起来 6.5s < 20s）
            Add(2.5f, true, false, false, 0.03f, $"R{round}·短暂移开", true, -1, -1, LX, LY);
            if (round % 7 == 3)
                Add(22f, false, false, false, 0f, $"R{round}·长离场22s（不许消失+方位提示）");
            if (round % 5 == 2)
            {
                Add(0.15f, true, true, false, 0.05f, $"R{round}·惊鸿一瞥（0.15s）", true);
                Add(3f, false, false, false, 0f, "离场3s");
            }
            if (round % 6 == 4)
                Add(1.2f, true, true, true, 0.05f, $"R{round}·抖动中注视", true);

            // ── 人脸奖励专用桥段（2026-10-13）──
            //  每个桥段前先"清场 3s"（人框左移，把基础轮次残留的累计衰减到 0）→ 桥段时间轴精确确定。
            if (round == 12)
            {
                Add(3f, true, false, true, 0.03f, "A1·清场3s（累计清零）", true, -1, -1, LX, LY);
                A1CenterStartSec = S;
                Add(24f, true, true, true, 0.01f, "A1·正视老师·人脸持续居中（→+20s 达标庆典）", true);
                Add(4f, true, true, true, 0.02f, "A1b·庆典余韵", true);
                Add(3f, true, false, true, 0.03f, "A1c·移开视线", true, -1, -1, LX, LY);
            }
            if (round == 20)
            {
                // 虚晃一枪：清场后 16s 居中 → 离开 → 衰减 → 差 4s 不达标（里程碑 5/10/15 照发）
                Add(3f, true, false, true, 0.03f, "B1·清场3s（累计清零）", true, -1, -1, LX, LY);
                Add(16f, true, true, true, 0.01f, "B1·坚持16s（接近达标）", true);
                Add(4f, true, false, true, 0.03f, "B1b·功亏一篑（环顾四周）", true, -1, -1, LX, LY);
            }
            if (round == 28)
            {
                // 中断后仍达标：清场 → 12s 居中 → 2s 脸检测丢失（衰减到 6）→ 第 3 段 +14s 处 = 20s 达标
                Add(3f, true, false, true, 0.03f, "A2·清场3s（累计清零）", true, -1, -1, LX, LY);
                Add(12f, true, true, true, 0.012f, "A2·第一段居中12s", true);
                Add(2f, true, false, true, 0.03f, "A2b·眨眼丢失2s（脸检测暂丢·人还在）", false);  // faceBox=false：只有人没有脸
                A2Seq3StartSec = S;
                Add(16f, true, true, true, 0.012f, "A2c·重建居中（→+14s 处达标）", true);
            }
            if (round == 36)
            {
                // C1 海报假脸：人框在画面右下（老师不在中心），但中央"检出"一张高分假脸（海报）——
                // 归属过滤应将其挡掉：不累计、不触发（真机"16 张脸"过检场景的模拟）
                Add(3f, true, false, true, 0.03f, "C1·清场3s（累计清零）", true, -1, -1, LX, LY);
                C1StartSec = S;
                Add(20f, true, false, true, 0.01f, "C1·海报假脸居中20s（人框在右下→不该累计）",
                    true, 0.50f, 0.49f, 0.80f, 0.75f);
                Add(3f, true, false, true, 0.03f, "C1b·恢复", true, -1, -1, LX, LY);
            }
            if (round == 44)
            {
                // C2 老师走动：人框+脸同步移动（孩子注视跟随），居中保持应正常累计（15s → Hold15，未到 20s）
                Add(3f, true, false, true, 0.03f, "C2·清场3s（累计清零）", true, -1, -1, LX, LY);
                C2StartSec = S;
                Add(5f, true, true, true, 0.012f, "C2a·老师走动·注视跟随（右移）", true, -1, -1, 0.61f, 0.43f);
                Add(5f, true, true, true, 0.012f, "C2b·老师走动·注视跟随（左移）", true, -1, -1, 0.55f, 0.47f);
                Add(5f, true, true, true, 0.012f, "C2c·老师走动·注视跟随（回中）", true);
            }
            round++;
        }
        // ── 极端段：快速来回（视线 0.4s 周期切换 ×12）──
        for (int k = 0; k < 12; k++)
        {
            Add(0.4f, true, true, true, 0.06f, "极端·快速看向", true);
            Add(0.4f, true, false, true, 0.06f, "极端·快速移开", true, -1, -1, LX, LY);
        }
        Add(12f, true, false, true, 0.09f, "极端·高频抖动·没看（人框左移抖动）", true, -1, -1, LX, LY);
        Add(10f, false, false, true, 0f, "收尾·离场（仍在讲话）");
        Add(12f, false, false, false, 0f, "收尾·长离场");

        TotalDuration = S;
        Debug.Log($"[SIM] 剧本就绪：{_segs.Count} 段 / 共 {TotalDuration / 60f:F1} 分钟 / 种子固定" +
                  $"（A1@{A1CenterStartSec:F1}s / A2重建@{A2Seq3StartSec:F1}s / C1假脸@{C1StartSec:F1}s / C2走动@{C2StartSec:F1}s）");
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
        // ★2026-10-13 语义修正：真实链路（FaceDetector.Latest）掉帧时保留上一批结果——
        //   模拟器也应"保留旧输出"而不是输出空（否则归属判定/保持计时被误扣，与真机行为不符）
        if (_dropFrames > 0) _dropFrames--;
        else if (seg.face && _rng.NextDouble() < 0.004) _dropFrames = _rng.Next(6, 15);

        if (seg.face)
        {
            if (_dropFrames > 0 && _lastDetCache != null)
            {
                detections = _lastDetCache;             // 停顿：挂旧结果（同真实 Latest）
            }
            else
            {
                float n = seg.noise;
                var box = new Rect(
                    Mathf.Clamp01(seg.pbx - PW * 0.5f + J(n)),
                    Mathf.Clamp01(seg.pby - PH * 0.5f + J(n)),
                    Mathf.Max(0.06f, PW + J(n * 0.5f)),
                    Mathf.Max(0.12f, PH + J(n)));
                _dets.Clear();
                _dets.Add(new DetectionResult { normalizedBox = box, confidence = 0.85f });
                _lastDetCache = _dets;
                detections = _dets;
            }
        }
        else
        {
            _lastDetCache = null;
            detections = _empty;
        }

        // 人脸框通道（BlazeFace 语义：检测到老师脸 → 框）。
        // 位置：显式给定（fcx≥0，如 C1 假脸）或自动跟随人框顶部（face = 人框中心 + (−0.08,−0.12)）。
        // 注：不受 _dropFrames 影响——真实语义中人体检测停顿 ≠ 人脸检测停顿（推理循环独立）。
        if (seg.faceBox)
        {
            float j = seg.noise * 0.25f;
            float cx = seg.fcx >= 0f ? seg.fcx + J(j) : seg.pbx - 0.08f + J(j);
            float cy = seg.fcy >= 0f ? seg.fcy + J(j) : seg.pby - 0.12f + J(j);
            cx = Mathf.Clamp01(cx); cy = Mathf.Clamp01(cy);
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
