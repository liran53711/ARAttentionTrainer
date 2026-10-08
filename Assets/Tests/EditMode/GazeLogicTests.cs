// Assets/Tests/EditMode/GazeLogicTests.cs
// EditMode 单元测试：注视判定（视口代理）、时长累计/衰减、瞬态事件只触发一次、可重复触发。
using NUnit.Framework;
using UnityEngine;

public class GazeLogicTests
{
    const float Dt = 1f / 30f;

    [Test]
    public void IsLookingAt_Center_True_Corner_False()
    {
        Assert.IsTrue(GazeLogic.IsLookingAt(new Vector2(0.5f, 0.5f)));
        Assert.IsTrue(GazeLogic.IsLookingAt(new Vector2(0.45f, 0.42f)));
        Assert.IsFalse(GazeLogic.IsLookingAt(new Vector2(0.05f, 0.05f)));
    }

    [Test]
    public void GazeStarted_FiresExactlyOnce_AtThreshold()
    {
        var g = new GazeLogic(startSeconds: 0.8f, sustainSeconds: 2f);
        int fires = 0; float firstAt = -1f;
        for (int i = 0; i < 30; i++) // 1 秒持续注视
        {
            g.Tick(true, Dt);
            if (g.GazeStarted) { fires++; if (firstAt < 0) firstAt = (i + 1) * Dt; }
        }
        Assert.AreEqual(1, fires);
        Assert.GreaterOrEqual(firstAt, 0.75f);
        Assert.LessOrEqual(firstAt, 0.9f);
    }

    [Test]
    public void GazeSustained_FiresExactlyOnce_AtThreshold()
    {
        var g = new GazeLogic(startSeconds: 0.8f, sustainSeconds: 2f);
        int fires = 0;
        for (int i = 0; i < 90; i++) // 3 秒持续注视
        {
            g.Tick(true, Dt);
            if (g.GazeSustained) fires++;
        }
        Assert.AreEqual(1, fires);
    }

    [Test]
    public void NotLooking_DecaysDwell_NotHardReset()
    {
        var g = new GazeLogic();
        for (int i = 0; i < 30; i++) g.Tick(true, Dt);    // 1.0s
        for (int i = 0; i < 9; i++) g.Tick(false, Dt);    // 移开 0.3s（decayRate=1）
        Assert.AreEqual(0.7f, g.Dwell, 0.05f);
    }

    [Test]
    public void GazeStarted_Refires_AfterDroppingBelowThreshold()
    {
        var g = new GazeLogic(startSeconds: 0.8f, sustainSeconds: 2f);
        int fires = 0;
        for (int i = 0; i < 27; i++) { g.Tick(true, Dt); if (g.GazeStarted) fires++; }   // 0.9s → 触发1次
        for (int i = 0; i < 12; i++) g.Tick(false, Dt);                                   // 掉到 0.5（<0.8，解锁）
        for (int i = 0; i < 27; i++) { g.Tick(true, Dt); if (g.GazeStarted) fires++; }   // 重新看 → 再触发
        Assert.AreEqual(2, fires);
    }
}
