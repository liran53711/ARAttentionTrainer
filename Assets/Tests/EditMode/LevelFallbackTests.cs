// Assets/Tests/EditMode/LevelFallbackTests.cs
// M4 新逻辑单元测试：干预档位表 / 运行时阈值可调 / 跟踪器框高 / 会话元数据增强。
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class LevelFallbackTests
{
    static DetectionResult D(float x, float y, float w, float h)
        => new DetectionResult { normalizedBox = new Rect(x, y, w, h), confidence = 0.9f };
    const float Dt = 1f / 30f;

    // ── 档位表（设计 §3.3）──

    [Test]
    public void ApplyLevel_Strong_MatchesDesignTable()
    {
        var s = ScriptableObject.CreateInstance<ExperienceSettings>();
        s.ApplyLevel(0);
        Assert.AreEqual(0.5f, s.gazeStartSeconds, 1e-4f);
        Assert.AreEqual(3f, s.reGuideCooldownSeconds, 1e-4f);
        Assert.AreEqual(8f, s.peekIntervalSeconds, 1e-4f);
        Assert.AreEqual(1.25f, s.ipSizeFactor, 1e-4f);
        Assert.AreEqual(1.0f, s.cueVolumeScale, 1e-4f);
        Assert.AreEqual("强", s.CurrentLevelName);
        Object.DestroyImmediate(s);
    }

    [Test]
    public void ApplyLevel_Weak_MatchesDesignTable_AndClamps()
    {
        var s = ScriptableObject.CreateInstance<ExperienceSettings>();
        s.ApplyLevel(99); // 越界 → 钳到弱档
        Assert.AreEqual(2, s.interventionLevel);
        Assert.AreEqual(1.2f, s.gazeStartSeconds, 1e-4f);
        Assert.AreEqual(8f, s.reGuideCooldownSeconds, 1e-4f);
        Assert.AreEqual(20f, s.peekIntervalSeconds, 1e-4f);
        Assert.AreEqual(0.8f, s.ipSizeFactor, 1e-4f);
        Assert.AreEqual(0.15f, s.cueVolumeScale, 1e-4f);
        Assert.AreEqual("弱", s.CurrentLevelName);
        Object.DestroyImmediate(s);
    }

    [Test]
    public void SnapshotJson_UsesInvariantDecimalPoint()
    {
        var s = ScriptableObject.CreateInstance<ExperienceSettings>();
        s.ApplyLevel(1);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // 小数点是逗号的文化
            string json = s.SnapshotJson();
            StringAssert.Contains("0.8", json, "gazeStart 应输出 0.8（不变文化）");
            StringAssert.DoesNotContain("0,8", json, "不能是 0,8");
            StringAssert.Contains("\"level\":1", json);
        }
        finally { CultureInfo.CurrentCulture = old; Object.DestroyImmediate(s); }
    }

    // ── 运行时阈值可调 ──

    [Test]
    public void GazeLogic_StartSeconds_AdjustableAtRuntime()
    {
        var g = new GazeLogic(startSeconds: 0.8f, sustainSeconds: 2f);
        for (int i = 0; i < 18; i++) g.Tick(true, Dt); // 0.6s 注视，未到 0.8 → 未触发
        Assert.IsFalse(g.GazeStarted);
        g.StartSeconds = 0.5f; // 档位切到"强"
        g.Tick(true, Dt);
        Assert.IsTrue(g.GazeStarted, "降低阈值后应立刻跨过并触发");
    }

    [Test]
    public void StateMachine_ReGuideCooldown_AdjustableAtRuntime()
    {
        var sm = new InteractionStateMachine(reGuideCooldownSec: 10f);
        // Idle → Guiding（讲话+未注视；初始冷却为 0 允许首导）
        sm.Tick(new InteractionInputs { speaking = true, looking = false, targetValid = true, dt = Dt });
        Assert.AreEqual(InteractionState.Guiding, sm.State);
        // 注视开始 → Hidden
        sm.Tick(new InteractionInputs { speaking = true, looking = true, gazeStarted = true, targetValid = true, dt = Dt });
        Assert.AreEqual(InteractionState.Hidden, sm.State);
        // 又看别处 1.5s：冷却 10s 未过 → 应保持 Hidden（不重导）
        for (int i = 0; i < 45; i++)
            sm.Tick(new InteractionInputs { speaking = true, looking = false, targetValid = true, dt = Dt });
        Assert.AreEqual(InteractionState.Hidden, sm.State, "冷却 10s 未到，不应重导");
        // 把冷却调小 → 下一帧立刻允许重导
        sm.ReGuideCooldownSeconds = 0f;
        sm.Tick(new InteractionInputs { speaking = true, looking = false, targetValid = true, dt = Dt });
        Assert.AreEqual(InteractionState.Guiding, sm.State, "冷却调零后应允许重导");
    }

    // ── 跟踪器框高（真机 IP 估距输入） ──

    [Test]
    public void Tracker_ReportsBoxHeight_AndRetainsWhenLost()
    {
        var t = new TargetTrackerLogic();
        var s = t.Tick(new System.Collections.Generic.List<DetectionResult> { D(0.3f, 0.2f, 0.4f, 0.5f) }, Dt);
        Assert.AreEqual(0.5f, s.boxHeight01, 1e-3f);

        // 丢失 0.5s（<1s 保持期）：框高应保持
        for (int i = 0; i < 15; i++)
            s = t.Tick(new System.Collections.Generic.List<DetectionResult>(), Dt);
        Assert.AreEqual(0.5f, s.boxHeight01, 1e-3f, "丢失保持期内框高应保留");
    }

    // ── 会话元数据增强 ──

    [Test]
    public void SessionLogger_WritesSettingsDeviceVersion()
    {
        string dir = Path.Combine(Path.GetTempPath(), "aratest_" + Path.GetRandomFileName());
        try
        {
            var logger = new SessionLogger(dir, "", "", 1, "{\"level\":1}");
            logger.LogEvent("SessionStart");
            logger.Close();

            Assert.IsTrue(Directory.Exists(logger.SessionDir));
            string meta = File.ReadAllText(Path.Combine(logger.SessionDir, "session.json"));
            StringAssert.Contains("\"settings\":{\"level\":1}", meta);
            StringAssert.Contains("\"device\":", meta);
            StringAssert.Contains("\"version\":", meta);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
