// Assets/Tests/EditMode/ConversationGateLogicTests.cs
// 对话门控（2026-10-24）：外界讲话时精灵语音全部延后；自己的 TTS/音效不能被当成外界讲话。
using NUnit.Framework;

public class ConversationGateLogicTests
{
    [Test]
    public void ExternalSpeech_BlocksAndListens()
    {
        var g = new ConversationGateLogic(0.5f, 0.3f);
        g.Tick(0.1f, true, false);
        Assert.IsTrue(g.ExternalSpeech);
        Assert.IsTrue(g.Listening);
        Assert.IsFalse(g.VoiceAllowed);
        Assert.IsFalse(g.SelfAudioSuppressing);
    }

    [Test]
    public void SilenceResumesAfterDelay()
    {
        var g = new ConversationGateLogic(0.5f, 0.3f);
        g.Tick(0.1f, true, false);
        g.Tick(0.3f, false, false);
        Assert.IsFalse(g.VoiceAllowed, "静默不足 0.5s 不应放行");
        Assert.IsFalse(g.Listening, "讲话结束后不再处于倾听态");
        g.Tick(0.3f, false, false);
        Assert.IsTrue(g.VoiceAllowed, "静默 0.6s 后应恢复语音");
    }

    [Test]
    public void SelfAudio_IsNotExternalSpeech()
    {
        var g = new ConversationGateLogic(0.5f, 0.3f);
        g.Tick(0.1f, true, true); // VAD 被自己的扬声器污染
        Assert.IsFalse(g.ExternalSpeech);
        Assert.IsFalse(g.Listening);
        Assert.IsFalse(g.VoiceAllowed);
        Assert.IsTrue(g.SelfAudioSuppressing);

        g.Tick(0.2f, true, false); // 自音频尾内仍忽略 VAD
        Assert.IsFalse(g.ExternalSpeech);
        Assert.IsTrue(g.SelfAudioSuppressing);

        g.Tick(0.2f, true, false); // 尾结束，外界讲话被识别
        Assert.IsTrue(g.ExternalSpeech);
        Assert.IsTrue(g.Listening);
        Assert.IsFalse(g.VoiceAllowed);
    }

    [Test]
    public void ShortGap_DoesNotReopenVoice()
    {
        var g = new ConversationGateLogic(0.5f, 0.3f);
        g.Tick(0.1f, true, false);
        g.Tick(0.2f, false, false); // 换气 0.2s
        g.Tick(0.1f, true, false);  // 又开始说
        Assert.IsFalse(g.VoiceAllowed);
        Assert.IsTrue(g.ExternalSpeech);
    }

    [Test]
    public void Reset_RestoresOpenState()
    {
        var g = new ConversationGateLogic(0.5f, 0.3f);
        g.Tick(0.1f, true, false);
        g.Reset();
        Assert.IsTrue(g.VoiceAllowed);
        Assert.IsFalse(g.ExternalSpeech);
        Assert.IsFalse(g.Listening);
        Assert.IsFalse(g.SelfAudioSuppressing);
    }
}
