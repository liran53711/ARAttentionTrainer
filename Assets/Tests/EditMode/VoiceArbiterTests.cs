// Assets/Tests/EditMode/VoiceArbiterTests.cs
// 语音仲裁单测（2026-10-20）：单通道/优先级打断/排队/氛围丢弃/间隔。
using NUnit.Framework;

public class VoiceArbiterTests
{
    static VoiceArbiter Make() => new VoiceArbiter { GapSeconds = 0.2f, MaxQueue = 1 };

    [Test]
    public void Idle_PlaysImmediately()
    {
        var a = Make();
        Assert.IsTrue(a.Request((int)VoicePriority.Guide, 1f, 0f));
        Assert.IsTrue(a.IsBusy(0.5f));
        Assert.IsFalse(a.IsBusy(1.5f)); // 1.0 + gap 0.2 后空闲
    }

    [Test]
    public void HigherPriority_Interrupts()
    {
        var a = Make();
        a.Request((int)VoicePriority.Guide, 2f, 0f);            // 正在播引导
        Assert.IsTrue(a.Request((int)VoicePriority.Critical, 1f, 0.5f), "高优先应打断");
        Assert.AreEqual((int)VoicePriority.Critical, a.CurrentPriority);
    }

    [Test]
    public void LowerPriority_QueuesThenPlays()
    {
        var a = Make();
        a.Request((int)VoicePriority.Critical, 1f, 0f);
        Assert.IsFalse(a.Request((int)VoicePriority.Guide, 0.8f, 0.4f), "低优先不打断");
        Assert.IsTrue(a.HasQueued);
        Assert.IsFalse(a.TryDequeue(0.4f, out _), "仍在播放中，不能出队");
        Assert.IsTrue(a.TryDequeue(1.3f, out int pr), "播放结束后应可出队");
        Assert.AreEqual((int)VoicePriority.Guide, pr);
        Assert.IsFalse(a.HasQueued);
    }

    [Test]
    public void Ambient_NeverQueues()
    {
        var a = Make();
        a.Request((int)VoicePriority.Guide, 2f, 0f);
        Assert.IsFalse(a.Request((int)VoicePriority.Ambient, 0.5f, 0.2f));
        Assert.IsFalse(a.HasQueued, "氛围语音直接丢弃，不排队");
    }

    [Test]
    public void SamePriority_DoesNotInterrupt()
    {
        var a = Make();
        a.Request((int)VoicePriority.Guide, 2f, 0f);
        Assert.IsFalse(a.Request((int)VoicePriority.Guide, 1f, 0.5f), "同级不打断（排队/丢弃）");
        Assert.AreEqual((int)VoicePriority.Guide, a.CurrentPriority);
    }

    [Test]
    public void Gap_Respected()
    {
        var a = Make(); // gap 0.2
        a.Request((int)VoicePriority.Guide, 1f, 0f);            // busyUntil = 1.2
        Assert.IsFalse(a.Request((int)VoicePriority.Guide, 1f, 1.1f), "Gap 内同级不播（排队）");
        Assert.IsTrue(a.TryDequeue(1.25f, out _), "间隔过后排队可出队");
        // 注意：Critical 在 Gap 内仍可打断（重要反馈优先），这是设计意图
        var b = Make();
        b.Request((int)VoicePriority.Guide, 1f, 0f);
        Assert.IsTrue(b.Request((int)VoicePriority.Critical, 1f, 1.1f), "Critical 可随时打断");
    }
}
