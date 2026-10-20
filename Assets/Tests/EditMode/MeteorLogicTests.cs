// Assets/Tests/EditMode/MeteorLogicTests.cs
// 流星引导纯逻辑单测（2026-10-20）：贝塞尔轨迹 / 星环槽位 / 星尘路径绕行 /
// 星星进度（只增不减、久离清空）/ 三段式引导导演（时序、轮次强化、跟随成功）。
using NUnit.Framework;
using UnityEngine;

public class MeteorLogicTests
{
    // ── 几何 ──
    [Test]
    public void Bezier_EndpointsMatch()
    {
        var p0 = new Vector3(0f, 2f, 0f);
        var c = new Vector3(0.5f, 1f, 0f);
        var p2 = new Vector3(1f, 0f, 0f);
        Assert.AreEqual(p0, MeteorLogic.Bezier(p0, c, p2, 0f));
        Assert.AreEqual(p2, MeteorLogic.Bezier(p0, c, p2, 1f));
        var mid = MeteorLogic.Bezier(p0, c, p2, 0.5f);
        Assert.AreEqual(0.5f, mid.x, 1e-4f);
        Assert.AreEqual(1.0f, mid.y, 1e-4f); // 控制点把路径拉到中点高度
    }

    [Test]
    public void RingSlot_SurroundsTeacher()
    {
        var teacher = new Vector3(0f, 1.5f, 2f);
        var viewer = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            var p = MeteorLogic.RingSlot(teacher, viewer, i, 8, 0.35f);
            // 槽位围绕老师：水平半径 ≤ radius（垂直方向有 0.8 挤压 → 椭圆环）
            var d = p - teacher;
            float horiz = new Vector2(d.x, d.z).magnitude;
            Assert.LessOrEqual(horiz, 0.36f, $"槽位 {i} 水平超界");
            Assert.GreaterOrEqual(d.magnitude, 0.25f, $"槽位 {i} 过近（应围绕老师）");
            Assert.LessOrEqual(d.magnitude, 0.45f, $"槽位 {i} 过远（应围绕老师头部）");
        }
        var s0 = MeteorLogic.RingSlot(teacher, viewer, 0, 8, 0.35f);
        var s4 = MeteorLogic.RingSlot(teacher, viewer, 4, 8, 0.35f);
        Assert.Greater(Vector3.Distance(s0, s4), 0.3f, "对角槽位应分布在不同侧");
    }

    [Test]
    public void PathControl_BendsAwayFromChild()
    {
        // 孩子站在线段中间附近：路径控制点应向远离孩子的一侧弯
        var child = new Vector3(0f, 1.5f, 0f);
        var start = new Vector3(0f, 0f, 1.2f);   // 孩子前方地面
        var end = new Vector3(0f, 0f, 4f);       // 老师脚下地面
        var ctrl = MeteorLogic.PathControl(child, start, end, 0.9f);
        // 控制点应在水平面内、偏离两点连线（绕行）
        var lineDir = (end - start).normalized;
        var offset = ctrl - Vector3.Lerp(start, end, 0.5f);
        offset.y = 0f;
        Assert.Greater(offset.magnitude, 0.5f, "控制点应偏离直线路径（绕开孩子）");
    }

    // ── 星星进度 ──
    static StarProgressLogic MakeStars() => new StarProgressLogic(2.5f, 8, 8f, 15f);

    [Test]
    public void StarProgress_DropsStarAfterInterval()
    {
        var s = MakeStars();
        for (int i = 0; i < 74; i++) Assert.AreEqual(StarEvent.None, s.Tick(1f / 30f, true)); // 2.47s
        Assert.AreEqual(StarEvent.StarDropped, s.Tick(0.1f, true));
        Assert.AreEqual(1, s.LitStars);
    }

    [Test]
    public void StarProgress_StarNotLostWhenLookingAway()
    {
        var s = MakeStars();
        s.Tick(0.1f, true); s.Tick(2.45f, true);       // 落 1 颗（2.55s）
        Assert.AreEqual(1, s.LitStars);
        for (int i = 0; i < 300; i++) s.Tick(1f / 30f, false); // 走神 10s
        Assert.AreEqual(1, s.LitStars, "走神不应扣星星（只增不减）");
        // 回来继续：进度保留
        int drops = 0;
        for (int i = 0; i < 240; i++) // 8s 注视
        {
            var ev = s.Tick(1f / 30f, true);
            if (ev == StarEvent.StarDropped) drops++;
        }
        Assert.AreEqual(3, drops, "8s 应再落 3 颗（含之前保留的进度）");
    }

    [Test]
    public void StarProgress_FullRingTriggersShower()
    {
        var s = MakeStars();
        int drops = 0; bool shower = false;
        for (int i = 0; i < 660; i++) // 22s 注视
        {
            var ev = s.Tick(1f / 30f, true);
            if (ev == StarEvent.StarDropped) drops++;
            if (ev == StarEvent.ShowerTriggered) shower = true;
        }
        Assert.IsTrue(shower, "8 颗应触发流星雨");
        Assert.AreEqual(7, drops, "触发前的 7 颗 + 第 8 颗触发（不产生 Drop 事件）");
        Assert.AreEqual(0, s.LitStars, "触发后星环清空重新积累");
    }

    [Test]
    public void StarProgress_LongAbsenceClearsRing()
    {
        var s = MakeStars();
        for (int i = 0; i < 240; i++) s.Tick(1f / 30f, true); // 8s 注视 → 3 颗
        Assert.GreaterOrEqual(s.LitStars, 3);
        bool cleared = false;
        for (int i = 0; i < 480; i++) // 16s 未注视
            if (s.Tick(1f / 30f, false) == StarEvent.Cleared) cleared = true;
        Assert.IsTrue(cleared, "老师久离 15s 应清空星环（Cleared 为瞬时事件）");
        Assert.AreEqual(0, s.LitStars);
    }

    // ── 引导导演 ──
    static GuideDirectorLogic MakeGuide() => new GuideDirectorLogic(0.8f, 1.8f, 0.9f, 6f, 8f, 3, 1f, 1.5f, 15f);

    static GuideEvent Step(GuideDirectorLogic g, float seconds, bool valid, bool everLocked, bool centered)
    {
        GuideEvent last = GuideEvent.None;
        int n = Mathf.CeilToInt(seconds * 30f);
        for (int i = 0; i < n; i++)
        {
            var e = g.Tick(1f / 30f, valid, everLocked, centered);
            if (e != GuideEvent.None) last = e;
        }
        return last;
    }

    [Test]
    public void Guide_ThreeStageTiming()
    {
        var g = MakeGuide();
        var e1 = Step(g, 0.85f, false, true, false);   // 0.8s 后触发
        Assert.AreEqual(GuideEvent.EnterConfused, e1);
        Assert.AreEqual(GuidePhase.Confused, g.Phase);
        var e2 = Step(g, 1.9f, false, true, false);
        Assert.AreEqual(GuideEvent.EnterFound, e2);
        var e3 = Step(g, 1.0f, false, true, false);
        Assert.AreEqual(GuideEvent.EnterLeading, e3);
        Assert.AreEqual(GuidePhase.Leading, g.Phase);
    }

    [Test]
    public void Guide_NotFollowingEscalatesThreeRoundsThenSilent()
    {
        var g = MakeGuide();
        Step(g, 0.85f, false, true, false);           // Confused
        Step(g, 1.9f, false, true, false);            // Found
        Step(g, 1.0f, false, true, false);            // Leading round1
        Step(g, 6.1f, false, true, false);            // 超时 → RoundUp（round2 Confused）
        Assert.AreEqual(2, g.Round);
        Step(g, 1.9f, false, true, false);            // Found 2
        Step(g, 1.0f, false, true, false);            // Leading 2
        Step(g, 6.1f, false, true, false);            // RoundUp → round3
        Assert.AreEqual(3, g.Round);
        Step(g, 1.9f, false, true, false);            // Found 3
        Step(g, 1.0f, false, true, false);            // Leading 3
        var e = Step(g, 8.2f, false, true, false);    // 第 3 轮超时（8s）→ Silent
        Assert.AreEqual(GuideEvent.EnterSilent, e);
        Assert.AreEqual(GuidePhase.Silent, g.Phase);
        // 静默期老师重获 → 回 Idle
        Step(g, 0.2f, true, true, false);
        Assert.AreEqual(GuidePhase.Idle, g.Phase);
    }

    [Test]
    public void Guide_FollowingSucceedsQuickly()
    {
        var g = MakeGuide();
        Step(g, 0.85f, false, true, false);           // Confused
        Step(g, 1.9f, false, true, false);            // Found
        Step(g, 1.0f, false, true, false);            // Leading
        // 老师回来 + 人脸居中持续 1s → Success
        var e = Step(g, 1.2f, true, true, true);
        Assert.AreEqual(GuideEvent.EnterSuccess, e);
        Assert.AreEqual(GuidePhase.Success, g.Phase);
    }

    [Test]
    public void Guide_FollowingSucceeds_EvenWithoutFaceCentered()
    {
        // 2026-10-20 修：孩子已看到老师（目标重获稳定）即视为跟随，不再强制人脸居中
        var g = MakeGuide();
        Step(g, 0.85f, false, true, false);
        Step(g, 1.9f, false, true, false);
        Step(g, 1.0f, false, true, false);            // Leading
        var e = Step(g, 1.2f, true, true, false);     // 目标回来但脸未居中
        Assert.AreEqual(GuideEvent.EnterSuccess, e, "目标稳定即算跟随（人脸居中只用于星星计分）");
    }

    [Test]
    public void Guide_NoTriggerWhenNeverLocked()
    {
        var g = MakeGuide();
        Step(g, 3f, false, false, false);             // 从未锁过 → 不触发
        Assert.AreEqual(GuidePhase.Idle, g.Phase);
    }
}
