// Assets/Scripts/Editor/M3SimHarness.cs
// 模拟测试机 · 运行器 + 不变量/SLA 断言（2026-10-13）。
// 用法：-executeMethod M3SimHarness.Run
// 以 1/30s 步长把 20+ 分钟剧本"瞬间"跑完，检查：
//   I1 稳定性：全程无异常
//   I2 陪伴不隐形：精灵出现后 alpha 不得低于 0.18（设计原则"绝对禁止消失"）
//   I3 转开提示：每次"稳定锁定后丢失"须在 1.4s 内发出方向性语音（Voice:Find）
//   I4 转回反馈：每次"离开 ≥4s 后重获"须在 1.5s 内给出 Fx:Lock（找到啦）
//   I5 反馈链路计数：Fx:Feedback / Gaze:Sustained / Voice:Find 均须达到剧本预期量级
//   I6 人脸居中奖励（2026-10-13 新增）：
//      · A1 桥段（持续居中）→ 恰在 +20s 达标触发庆典（时间窗口 ±0.8s）
//      · A2 桥段（12s+丢2s+重建）→ 恰在第三段 +14s 处达标（验证"衰减不重置"且不提前）
//      · B1 桥段（16s 后离开）→ 不得触发庆典（庆典总数恰为 2）
//      · C1 桥段（海报假脸居中但人框在别处）→ 归属过滤：保持不得累计（段末探针 <1s）、不触发
//      · C2 桥段（老师走动 15s，人框+脸同步移动）→ 居中保持正常累计（出现 Hold15）
//      · 庆典持续 ≥5.5s（6s 舞蹈）且期间精灵全亮（alpha ≥ 0.85）
//      · 里程碑 5/10/15s 信号各 ≥2 次
//   I7 视觉跳变诊断（仅报告）：统计精灵视口位置的单帧最大跳变——交互"怪"的量化排查
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
        int cntHold5 = 0, cntHold10 = 0, cntHold15 = 0;
        var celebrateTimes = new List<float>();
        var hold15Times = new List<float>();  // Face:Hold15 信号时刻（C2 走动断言用）
        var ipJumpTop = new List<string>();   // 跳变 TOP5 诊断
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
                case "Face:Hold5": cntHold5++; break;
                case "Face:Hold10": cntHold10++; break;
                case "Face:Hold15": cntHold15++; hold15Times.Add(now); break;
                case "Face:Celebrate": celebrateTimes.Add(now); break;
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
        float celebrateAccum = 0f;
        float celebrateRun = 0f;              // 本轮庆典已持续
        float celebrateAlphaMin = 1f;         // 庆典稳定期(>1s)的最低 alpha
        float c1MaxHold = 0f;                 // C1 假脸期间保持探针的最大值（应≈0）
        var prevIp = new Vector2(0.5f, 0.5f);
        float ipJumpMax = 0f; int ipJumpCnt = 0; // I7：单帧视口跳变统计（诊断）
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

            // I6 庆典期间精灵必须全亮（奖励看不见=白做）
            if (driver.FaceCelebrateProbe)
            {
                celebrateRun += dt;
                celebrateAccum += dt;
                if (celebrateRun > 1.0f)
                    celebrateAlphaMin = Mathf.Min(celebrateAlphaMin, driver.IpAlphaProbe);
            }
            else celebrateRun = 0f;

            // I6 C1 假脸检查点：C1 段末（+18~+20.5s）保持探针必须 ≈0
            if (scenario.C1StartSec > 0f && now >= scenario.C1StartSec + 18f && now <= scenario.C1StartSec + 20.5f)
                c1MaxHold = Mathf.Max(c1MaxHold, driver.FaceHoldProbe);

            // I7 视口跳变诊断（仅统计；>0.12 的记录 TOP5 供定位来源）
            var ipNow = driver.IpPosProbe;
            float jump = Vector2.Distance(ipNow, prevIp);
            if (jump > ipJumpMax) ipJumpMax = jump;
            if (jump > 0.12f)
            {
                ipJumpCnt++;
                if (ipJumpTop.Count < 5)
                    ipJumpTop.Add($"{now:F1}s {prevIp.x:F2},{prevIp.y:F2}→{ipNow.x:F2},{ipNow.y:F2} d={jump:F2} anim={(IPWorldView.IPAnimState)driver.IpAnimProbe}");
            }
            prevIp = ipNow;

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

        // ── I6 人脸居中奖励断言 ──
        float celebrateSec = driver.settings != null ? driver.settings.faceCelebrateSeconds : 20f;
        if (celebrateTimes.Count != 2)
            violations.Add($"[庆典计数] 期望恰好 2 次（A1/A2），实际 {celebrateTimes.Count} 次 @{string.Join(",", celebrateTimes)}");
        else
        {
            float exp1 = scenario.A1CenterStartSec + celebrateSec;
            float exp2 = scenario.A2Seq3StartSec + 14f; // 6s剩余 + 14s = 20s（衰减不重置的精确预期）
            if (Mathf.Abs(celebrateTimes[0] - exp1) > 0.8f)
                violations.Add($"[庆典1时机] t={celebrateTimes[0]:F2}s 期望 {exp1:F2}s±0.8（A1 持续居中20s）");
            if (Mathf.Abs(celebrateTimes[1] - exp2) > 0.8f)
                violations.Add($"[庆典2时机] t={celebrateTimes[1]:F2}s 期望 {exp2:F2}s±0.8（A2 重建后+14s）");
        }
        if (celebrateAccum < 5.5f)
            violations.Add($"[庆典时长] 累计 {celebrateAccum:F1}s < 5.5s（应≈6s 舞蹈）");
        if (celebrateAccum > 0.5f && celebrateAlphaMin < 0.85f)
            violations.Add($"[庆典可见性] 庆典稳定期 alpha 最低 {celebrateAlphaMin:F2} < 0.85");
        if (cntHold5 < 2 || cntHold10 < 2 || cntHold15 < 2)
            violations.Add($"[里程碑] Hold5={cntHold5} Hold10={cntHold10} Hold15={cntHold15}（各应 ≥2）");

        // C1 假脸归属过滤：海报假脸居中 20s 不得累计（段末保持 < 1s），更不得触发庆典
        if (scenario.C1StartSec > 0f && c1MaxHold > 1f)
            violations.Add($"[假脸过滤] C1 段末保持 {c1MaxHold:F2}s ≥1s——海报假脸被计入（归属过滤失效）");

        // C2 老师走动：居中保持应正常累计（C2 段 15s 时应出现 Hold15）
        if (scenario.C2StartSec > 0f)
        {
            bool found = false;
            foreach (var t in hold15Times)
                if (t >= scenario.C2StartSec + 13.2f && t <= scenario.C2StartSec + 16.5f) { found = true; break; }
            if (!found)
                violations.Add($"[走动累计] C2 段（走动注视 15s）内未见 Face:Hold15——移动中累计失效");
        }

        Debug.Log($"[SIM] ⏱ {scenario.TotalDuration / 60f:F1} 分钟剧本 / {ticks} tick / 实跑 {sw.Elapsed.TotalSeconds:F1}s");
        Debug.Log($"[SIM] 计数 Voice:Find={cntFind} Fx:Lock={cntLock} Fx:Feedback={cntFb} Gaze:Sustained={cntGaze} Fx:Appear={cntAppear}");
        Debug.Log($"[SIM] 人脸奖励：庆典={celebrateTimes.Count}次 [{string.Join(", ", celebrateTimes.ConvertAll(t => t.ToString("F1")))}] " +
                  $"时长={celebrateAccum:F1}s 庆典alphaMin={celebrateAlphaMin:F2} 里程碑5/10/15={cntHold5}/{cntHold10}/{cntHold15} " +
                  $"C1末保持={c1MaxHold:F2}s");
        Debug.Log($"[SIM] I7视觉跳变（诊断）：单帧最大 {ipJumpMax:F3} 视口单位，>0.12 共 {ipJumpCnt} 次（{ipJumpCnt / (scenario.TotalDuration / 60f):F1}/分钟）");
        foreach (var j in ipJumpTop) Debug.Log($"[SIM]   ↳ 跳变样本 {j}");
        UnityEngine.Object.DestroyImmediate(go); // 关闭日志句柄，避免下个种子撞锁
        return violations;
    }
}
