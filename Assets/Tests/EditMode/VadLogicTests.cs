// Assets/Tests/EditMode/VadLogicTests.cs
// EditMode 单元测试：语音活动检测（讲话开始/结束/停顿桥接/杂音抑制）+ 能量计算。
using NUnit.Framework;
using UnityEngine;

public class VadLogicTests
{
    const float Dt = 1f / 60f;
    const float Silence = -60f;
    const float Speech = -45f;

    /// <summary>新建并完成 2 秒静音校准（噪声底≈-60dB）。</summary>
    static VadLogic Calibrated()
    {
        var v = new VadLogic();
        for (int i = 0; i < 130; i++) v.Tick(Silence, Dt); // 2.17s
        return v;
    }

    [Test]
    public void Calibration_SetsNoiseFloor()
    {
        var v = Calibrated();
        Assert.AreEqual(-60f, v.NoiseFloorDb, 0.5f);
        Assert.IsFalse(v.Speaking);
    }

    [Test]
    public void SpeechStart_AfterDebounce_FiresOnce()
    {
        var v = Calibrated();
        int starts = 0;
        for (int i = 0; i < 60; i++) { v.Tick(Speech, Dt); if (v.SpeechStarted) starts++; }
        Assert.AreEqual(1, starts);
        Assert.IsTrue(v.Speaking);
    }

    [Test]
    public void ShortPause_IsBridged_KeepsSpeaking()
    {
        var v = Calibrated();
        for (int i = 0; i < 60; i++) v.Tick(Speech, Dt);  // 讲话 1s
        for (int i = 0; i < 18; i++) v.Tick(Silence, Dt); // 停顿 0.3s（<0.5s）
        Assert.IsTrue(v.Speaking, "讲话中的换气停顿不应判定为结束");
        for (int i = 0; i < 60; i++) v.Tick(Speech, Dt);
        Assert.IsTrue(v.Speaking);
    }

    [Test]
    public void LongSilence_EndsSpeech_FiresOnce()
    {
        var v = Calibrated();
        for (int i = 0; i < 60; i++) v.Tick(Speech, Dt);
        int ends = 0;
        for (int i = 0; i < 60; i++) { v.Tick(Silence, Dt); if (v.SpeechEnded) ends++; }
        Assert.AreEqual(1, ends, "连续静音 0.5s 后应恰好触发一次'讲完'");
        Assert.IsFalse(v.Speaking);
    }

    [Test]
    public void Blip_ShortNoise_DoesNotFireSpeechEnded()
    {
        var v = Calibrated();
        for (int i = 0; i < 12; i++) v.Tick(Speech, Dt);   // 0.2s 杂音（超过防抖、不够最短语音）
        for (int i = 0; i < 60; i++) v.Tick(Silence, Dt);  // 安静
        Assert.IsFalse(v.Speaking);
        Assert.IsFalse(v.SpeechEnded);
    }

    [Test]
    public void Energy_SineHalfAmplitude_IsAboutMinus9dB()
    {
        var samples = new float[1024];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * i / 64f);
        // 正弦 RMS = 幅值/√2 → 20*log10(0.3536) ≈ -9.03dB
        Assert.AreEqual(-9.03f, AudioEnergy.ComputeRmsDb(samples), 0.2f);
    }

    [Test]
    public void Energy_Silence_IsFloor()
    {
        Assert.AreEqual(-80f, AudioEnergy.ComputeRmsDb(new float[512]), 0.01f);
    }
}
