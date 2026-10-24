// Assets/Tests/EditMode/FlipbookCelebrateTests.cs
// celebrate 烟花接线验收（2026-10-24）：资产已交付但代码未调用，导致真机从未出现。
using NUnit.Framework;
using UnityEngine;

public class FlipbookCelebrateTests
{
    [Test]
    public void CelebratePulse_StartsCelebrateFlipbook()
    {
        var go = new GameObject("fx");
        var fx = go.AddComponent<IpEffects>();
        fx.EnsureForTest();
        Assert.IsFalse(fx.CelebratePlaying, "初始不应在播放");
        fx.Pulse("Celebrate");
        Assert.IsTrue(fx.CelebratePlaying, "Pulse(Celebrate) 应启动 celebrate 序列帧");
        Object.DestroyImmediate(go);
    }
}
