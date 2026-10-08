// Assets/Tests/EditMode/InteractionStateMachineTests.cs
// EditMode 单元测试：状态机转换表逐条覆盖（对应设计文档 §3.1）。
using NUnit.Framework;

public class InteractionStateMachineTests
{
    const float Dt = 1f / 30f;

    static InteractionInputs I(bool speaking, bool looking, bool target = true,
        bool gazeStarted = false, bool gazeSustained = false, bool speechEnded = false)
        => new InteractionInputs
        {
            speaking = speaking, looking = looking, targetValid = target,
            gazeStarted = gazeStarted, gazeSustained = gazeSustained,
            speechJustEnded = speechEnded, dt = Dt
        };

    static void TickN(InteractionStateMachine m, InteractionInputs i, int n)
    { for (int k = 0; k < n; k++) m.Tick(i); }

    /// <summary>把状态机推到 Guiding（1 次 tick 即可）。</summary>
    static InteractionStateMachine ToGuiding()
    {
        var m = new InteractionStateMachine();
        m.Tick(I(true, false));
        Assert.AreEqual(InteractionState.Guiding, m.State);
        return m;
    }

    /// <summary>推到 Hidden（走真实的 Guiding→Hidden 转换）。</summary>
    static InteractionStateMachine ToHidden()
    {
        var m = ToGuiding();
        m.Tick(I(true, true, gazeStarted: true));
        Assert.AreEqual(InteractionState.Hidden, m.State);
        return m;
    }

    [Test]
    public void Idle_StaysWhenSilent()
    {
        var m = new InteractionStateMachine();
        TickN(m, I(false, false), 10);
        Assert.AreEqual(InteractionState.Idle, m.State);
    }

    [Test]
    public void Idle_Guides_WhenSpeakingAndNotLooking()
    {
        var m = new InteractionStateMachine();
        m.Tick(I(true, false));
        Assert.AreEqual(InteractionState.Guiding, m.State);
        Assert.IsTrue(m.GuideTriggered, "进入引导应发出本帧瞬态事件");
    }

    [Test]
    public void Idle_NoGuide_WhenAlreadyLooking()
    {
        var m = new InteractionStateMachine();
        TickN(m, I(true, true), 10);
        Assert.AreEqual(InteractionState.Idle, m.State);
    }

    [Test]
    public void Guiding_ToHidden_OnGazeStarted()
    {
        var m = ToGuiding();
        m.Tick(I(true, true, gazeStarted: true));
        Assert.AreEqual(InteractionState.Hidden, m.State);
    }

    [Test]
    public void Guiding_ToIdle_OnTargetLost()
    {
        var m = ToGuiding();
        m.Tick(I(true, false, target: false));
        Assert.AreEqual(InteractionState.Idle, m.State);
    }

    [Test]
    public void Guiding_ToIdle_AfterLongSilence()
    {
        var m = ToGuiding();
        TickN(m, I(false, false), 301); // 10.03s 没讲话
        Assert.AreEqual(InteractionState.Idle, m.State);
    }

    [Test]
    public void Hidden_Feedback_WhenSustainedThenSpeechEnds()
    {
        var m = ToHidden();
        m.Tick(I(true, true, gazeSustained: true)); // 注视达成（latch）
        TickN(m, I(true, true), 30);                // 继续看 1s
        m.Tick(I(false, true, speechEnded: true));  // 语音结束瞬间仍在看
        Assert.AreEqual(InteractionState.Feedback, m.State);
        Assert.IsTrue(m.FeedbackTriggered);
    }

    [Test]
    public void Hidden_NoFeedback_IfSpeechEndsWhileNotLooking()
    {
        var m = ToHidden();
        m.Tick(I(false, false, speechEnded: true)); // 讲完时没在看
        TickN(m, I(false, false), 70);              // 超过 2s 宽限
        Assert.AreEqual(InteractionState.Idle, m.State, "没看够不给反馈，应回待机");
    }

    [Test]
    public void Hidden_ReGuides_AfterLookAwayAndCooldown()
    {
        var m = ToHidden();
        TickN(m, I(true, false), 60);   // 看别处 2s：冷却未过（引导发生于 t≈0）→ 仍保持隐藏
        Assert.AreEqual(InteractionState.Hidden, m.State);

        bool fired = false;
        for (int k = 0; k < 90 && m.State != InteractionState.Guiding; k++)
        {
            m.Tick(I(true, false));
            if (m.GuideTriggered) fired = true;
        }
        Assert.AreEqual(InteractionState.Guiding, m.State, "冷却+抖动宽限过后应温和重新引导");
        Assert.IsTrue(fired);
    }

    [Test]
    public void Feedback_EndsToIdle_ThenCooldownBlocksGuide()
    {
        var m = ToHidden();
        m.Tick(I(true, true, gazeSustained: true));
        m.Tick(I(false, true, speechEnded: true));
        Assert.AreEqual(InteractionState.Feedback, m.State);

        TickN(m, I(true, false), 61);   // 反馈 2s 播完（其间讲话恢复）
        Assert.AreEqual(InteractionState.Idle, m.State);

        TickN(m, I(true, false), 30);   // 反馈冷却 2s 内 → 不引导
        Assert.AreEqual(InteractionState.Idle, m.State);

        TickN(m, I(true, false), 45);   // 再等 1.5s → 冷却过 → 引导
        Assert.AreEqual(InteractionState.Guiding, m.State);
    }
}
