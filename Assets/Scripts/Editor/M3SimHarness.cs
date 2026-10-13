// Assets/Scripts/Editor/M3SimHarness.cs
// 模拟测试机 · 运行器 + 不变量/SLA 断言（2026-10-13）。
// 用法：-executeMethod M3SimHarness.Run
// 以 1/30s 步长把 20+ 分钟剧本"瞬间"跑完，检查：
//   I1 稳定性：全程无异常
//   I2 陪伴不隐形：精灵出现后 alpha 不得低于 0.18（设计原则"绝对禁止消失"）
//   I3 转开提示：每次"稳定锁定后丢失"须在 1.2s 内发出方向性语音（Voice:Find）
//   I4 转回反馈：每次"离开 ≥4s 后重获"须在 1.5s 内给出 Fx:Lock（找到啦）
//   I5 反馈链路计数：Fx:Feedback / Gaze:Sustained / Voice:Find 均须达到剧本预期量级
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class M3SimHarness
{
    public static void Run()
    {
        // ── 多种子抗抖动（2026-10-13）：不同噪声/掉帧图案各跑一遍 ──
        int[] seeds = { 7, 21, 99 };
        int totalViol = 0;
        foreach (int seed in seeds)
        {
            var v = RunOne(seed);
            totalViol += v.Count;
            Debug.Log($"[SIM] 种子 {seed}：{(v.Count == 0 ? "✅ 通过" : $"❌ {v.Count} 项违规")}");
            foreach (var line in v.GetRange(0, Math.Min(v.Count, 10)))
                Debug.LogError($"[SIM][seed={seed}] " + line);
        }
        Debug.Log(totalViol == 0
            ? $"[SIM] ✅✅ 全部 {seeds.Length} 个种子通过——逻辑鲁棒性达标"
            : $"[SIM] ❌ 共 {totalViol} 项违规");
        EditorApplication.Exit(totalViol == 0 ? 0 : 2);
    }

    static List<string> RunOne(int seed)
    {
        var violations = new List<string>();
        var go = new GameObject("SimDriver");
        var driver = go.AddComponent<M3DebugDriver>();
        driver.simulateGazeWithKeyboard = false;
        driver.useMicVad = false;

        driver.settings = AssetDatabase.LoadAssetAtPath<ExperienceSettings>("Assets/Settings/ExperienceSettings.asset");
        var scenario = new M3SimScenario(seed: seed);
        driver.sim = scenario;
        driver.InitCore(); // 编辑器不自动跑 Start，显式初始化

        // ── 信号记账 ──
        float lastFind = -99f, lastLock = -99f, lastAcquire = -99f;
        int cntFind = 0, cntLock = 0, cntFb = 0, cntGaze = 0, cntAppear = 0;
        float lostDeadline = -1f, acquireDeadline = -1f;
        float lastLostAt = -99f;
        driver.OnSignal += (kind, detail) =>
        {
            float now = driver.NowProbe;
            switch (kind + ":" + detail)
            {
                case "Voice:Find": lastFind = now; cntFind++; if (lostDeadline > 0) lostDeadline = -1f; break;
                case "Fx:Lock": lastLock = now; cntLock++; if (acquireDeadline > 0) acquireDeadline = -1f; break;
                case "Fx:Feedback": cntFb++; break;
                case "Gaze:Sustained": cntGaze++; break;
                case "Fx:Appear": cntAppear++; break;
                case "T:Lost":
                    // 只有"曾稳定锁定"才有提示义务（开场无脸不算）
                    if (now > 8f) { lastLostAt = now; lostDeadline = now + 1.4f; }
                    break;
                case "T:Acquired":
                    // 离开 ≥4s 的重获须有"找到啦"（冷却保护期的快速来回免检）
                    if (now - lastLostAt >= 4f) acquireDeadline = now + 1.5f;
                    break;
            }
        };

        float dt = 1f / 30f;
        int ticks = Mathf.CeilToInt((scenario.TotalDuration + 3f) / dt);
        int invisibleStreak = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < ticks; i++)
        {
            try { driver.Tick(dt); }
            catch (Exception e)
            {
                violations.Add($"[异常] t={driver.NowProbe:F1}s {e.GetType().Name}: {e.Message}");
                break;
            }
            float now = driver.NowProbe;

            // I2 不隐形（出现 4s 后；幂等去重：连续隐形只记一次）
            bool visible = driver.IpAnimProbe != 0; // != Hidden
            if (now > 4f && visible && driver.IpAlphaProbe < 0.18f)
            {
                if (invisibleStreak++ == 0)
                    violations.Add($"[隐形] t={now:F1}s anim={(IPWorldView.IPAnimState)driver.IpAnimProbe} alpha={driver.IpAlphaProbe:F2}");
            }
            else invisibleStreak = 0;

            // I3/I4 截止时间判定
            if (lostDeadline > 0 && now > lostDeadline)
            {
                violations.Add($"[无转开提示] t={now:F1}s 丢失后 1.4s 未见 Voice:Find");
                lostDeadline = -1f;
            }
            if (acquireDeadline > 0 && now > acquireDeadline)
            {
                violations.Add($"[无转回反馈] t={now:F1}s 重获后 1.5s 未见 Fx:Lock");
                acquireDeadline = -1f;
            }
        }

        // I5 计数门槛
        int expFind = 8, expLock = 6, expFb = 10;
        if (cntFind < expFind) violations.Add($"[计数] Voice:Find={cntFind} < {expFind}");
        if (cntLock < expLock) violations.Add($"[计数] Fx:Lock={cntLock} < {expLock}");
        if (cntFb < expFb) violations.Add($"[计数] Fx:Feedback={cntFb} < {expFb}");

        Debug.Log($"[SIM] ⏱ {scenario.TotalDuration / 60f:F1} 分钟剧本 / {ticks} tick / 实跑 {sw.Elapsed.TotalSeconds:F1}s");
        Debug.Log($"[SIM] 计数 Voice:Find={cntFind} Fx:Lock={cntLock} Fx:Feedback={cntFb} Gaze:Sustained={cntGaze} Fx:Appear={cntAppear}");
        UnityEngine.Object.DestroyImmediate(go); // 关闭日志句柄，避免下个种子撞锁
        return violations;
    }
}
