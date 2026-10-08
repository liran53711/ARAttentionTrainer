// Assets/Tests/EditMode/TargetTrackerLogicTests.cs
// EditMode 单元测试：目标选择 / 头部点 / 滞回切换 / 丢失-冻结-失效。
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class TargetTrackerLogicTests
{
    static DetectionResult D(float x, float y, float w, float h, float c = 0.9f)
        => new DetectionResult { normalizedBox = new Rect(x, y, w, h), confidence = c };

    static List<DetectionResult> L(params DetectionResult[] d) => new List<DetectionResult>(d);
    static readonly List<DetectionResult> Empty = new List<DetectionResult>();
    const float Dt = 1f / 30f;

    [Test]
    public void HeadPoint_IsUpperCenterOfBox()
    {
        var p = TargetTrackerLogic.HeadPointOf(new Rect(0.2f, 0.2f, 0.4f, 0.4f));
        Assert.AreEqual(0.4f, p.x, 1e-4f);
        Assert.AreEqual(0.3f, p.y, 1e-4f);
    }

    [Test]
    public void FirstLock_PicksLargestArea()
    {
        var t = new TargetTrackerLogic();
        var s = t.Tick(L(D(0.1f, 0.1f, 0.2f, 0.2f),          // 面积 0.04
                         D(0.5f, 0.5f, 0.5f, 0.5f)), Dt);     // 面积 0.25 ← 应选
        Assert.IsTrue(s.valid);
        Assert.AreEqual(0.75f, s.headPoint01.x, 1e-3f);
        Assert.AreEqual(0.625f, s.headPoint01.y, 1e-3f);
    }

    [Test]
    public void SlightlyLargerCandidate_DoesNotStealTarget()
    {
        var t = new TargetTrackerLogic(switchHysteresis: 1.25f);
        t.Tick(L(D(0.10f, 0.10f, 0.40f, 0.40f)), Dt);              // A: 面积0.16
        var s = t.Tick(L(D(0.12f, 0.10f, 0.40f, 0.40f),            // A': 面积0.16，帧间移动小 → 匹配
                         D(0.55f, 0.55f, 0.42f, 0.42f)), Dt);      // B: ×1.10 不够大 → 不切换
        Assert.Less(Vector2.Distance(s.headPoint01, new Vector2(0.32f, 0.20f)), 0.05f,
            "应继续跟踪 A'（头部约 0.32,0.20），而不是跳到 B（约 0.76,0.655）");
    }

    [Test]
    public void MuchLargerCandidate_SwitchesTarget()
    {
        var t = new TargetTrackerLogic(switchHysteresis: 1.25f);
        t.Tick(L(D(0.10f, 0.10f, 0.40f, 0.40f)), Dt);              // A
        var s = t.Tick(L(D(0.12f, 0.10f, 0.40f, 0.40f),            // A' 0.16
                         D(0.50f, 0.50f, 0.50f, 0.50f)), Dt);      // B 0.25 > 0.16×1.25 → 切换
        Assert.Less(Vector2.Distance(s.headPoint01, new Vector2(0.75f, 0.625f)), 0.01f);
    }

    [Test]
    public void LostTarget_ReAcquires_NewPersonAfterDelay()
    {
        var t = new TargetTrackerLogic(lostKeepSec: 1f, idleDropSec: 10f, reAcquireSec: 1.5f);
        t.Tick(L(D(0.05f, 0.05f, 0.3f, 0.3f)), Dt);   // 锁定左侧的人

        for (int i = 0; i < 48; i++) t.Tick(Empty, Dt); // 1.6s 无检测

        // 远处出现新的人（模拟镜头切换）→ 应重新锁定而不是死守旧位置
        var s = t.Tick(L(D(0.60f, 0.30f, 0.35f, 0.35f)), Dt);
        Assert.IsTrue(s.valid);
        Assert.IsFalse(s.frozen);
        Assert.Less(Vector2.Distance(s.headPoint01, new Vector2(0.775f, 0.3875f)), 0.05f,
            "应重新锁定到新位置（头部约 0.775,0.3875）");
    }

    [Test]
    public void Lost_HoldThenFreezeThenInvalid()
    {
        var t = new TargetTrackerLogic(lostKeepSec: 1f, idleDropSec: 10f);
        var s0 = t.Tick(L(D(0.1f, 0.1f, 0.3f, 0.3f)), Dt);
        var held = s0.headPoint01;

        // 0.5s：保持可见、未冻结、位置不变
        TargetSnapshot s = default;
        for (int i = 0; i < 15; i++) s = t.Tick(Empty, Dt);
        Assert.IsTrue(s.valid);
        Assert.IsFalse(s.frozen);
        Assert.AreEqual(held.x, s.headPoint01.x, 1e-4f);

        // 合计约 1.33s：冻结
        for (int i = 0; i < 25; i++) s = t.Tick(Empty, Dt);
        Assert.IsTrue(s.valid);
        Assert.IsTrue(s.frozen);
        Assert.AreEqual(held.x, s.headPoint01.x, 1e-4f);

        // 超过 10s：失效
        for (int i = 0; i < 280; i++) s = t.Tick(Empty, Dt);
        Assert.IsFalse(s.valid);
    }
}
