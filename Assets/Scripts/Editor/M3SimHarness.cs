// Assets/Scripts/Editor/M3SimHarness.cs
// 模拟测试机 · 运行器 + 不变量/SLA 断言（2026-10-20 流星引导版）。
// 用法：-executeMethod M3SimHarness.Run
// 以 1/30s 步长把 20+ 分钟剧本"瞬间"跑完，检查：
//   I1 稳定性：全程无异常
//   I2 陪伴不隐形：精灵出现后 alpha ≥ 0.18（"绝对禁止消失"）
//   I3 转开提示：每次"稳定锁定后丢失"须在 1.4s 内发出引导语音（Guide:Confused → Voice:Find）
//   I4 转回反馈：每次"离开 ≥4s 后重获"须在 1.5s 内给出 Fx:Lock
//   I5 反馈链路计数：Fx:Feedback / Gaze:Sustained / Voice:Find 达到剧本预期量级
//   I6 流星引导体系（2026-10-20）：
//      · A1'（持续注视）→ 恰在 +20s 集满 8 星触发流星雨（±0.6s）
//      · B1'（12s 注视+4s 移开+回看 16s）→ 星星保留、累计 20s 时集满（重建段 +8s）→ 验证"只增不减"
//      · C1'（海报假脸居中 20s）→ 归属过滤：不落星（窗口内 Star:Drop == 0）
//      · 星环清空：Cleared ≥ 1（老师久离 >15s）
//      · 引导链 G1'（老师离场 32s）→ 信号顺序：Confused→Found→Lead1→RoundUp→Lead2→RoundUp→Lead3→Silent
//      · 跟随成功 G2'：老师重获后 2.5s 内出现 Guide:Success
//   I7 视觉跳变诊断（仅报告）：精灵视口单帧最大跳变
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class M3SimHarness
{
    public static void Run()
    {
        int[] seeds = { 7, 21, 99 };
        int totalViol = 0;
        foreach (int seed in seeds)
        {
            var v = RunOne(seed);
            totalViol += v.Count;
            Debug.Log($"[SIM] 种子 {seed}：{(v.Count == 0 ? "✅ 通过" : $"❌ {v.Count} 项违规")}");
            foreach (var line in v.GetRange(0, Math.Min(v.Count, 12)))
                Debug.LogError($"[SIM][seed={seed}] " + line);
        }
        Debug.Log(totalViol == 0
            ? $"[SIM] ✅✅ 全部 {seeds.Length} 个种子通过——流星引导逻辑达标"
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
        driver.InitCore();

        // ── 信号记账 ──
        int cntFind = 0, cntLock = 0, cntFb = 0, cntGaze = 0, cntAppear = 0;
        int cntDrop = 0, cntShower = 0, cntCleared = 0, cntSuccess = 0, cntSilent = 0;
        int cntSearchStart = 0, cntSearchVoice = 0, cntSearchEnd = 0, searchVoicesAfterEnd = 0;
        float firstAcquireAt = -1f, searchEndAt = -1f;
        var showerTimes = new List<float>();
        var successTimes = new List<float>();
        var guideEvents = new List<(float t, string kind)>();   // 引导事件序列（G1 顺序断言）
        float gazeDeadline = -1f, acquireDeadline = -1f;
        float lastLostAt = -99f;
        bool prevLooking = true;

        driver.OnSignal += (kind, detail) =>
        {
            float now = driver.NowProbe;
            switch (kind + ":" + detail)
            {
                case "Voice:Find": cntFind++; if (gazeDeadline > 0) gazeDeadline = -1f; break;
                case "Fx:Lock": cntLock++; if (acquireDeadline > 0) acquireDeadline = -1f; break;
                case "Fx:Feedback": cntFb++; break;
                case "Gaze:Sustained": cntGaze++; break;
                case "Fx:Appear": cntAppear++; break;
                case "Star:Drop": cntDrop++; break;
                case "Star:Shower": cntShower++; showerTimes.Add(now); break;
                case "Star:Cleared": cntCleared++; break;
                case "Guide:Success": cntSuccess++; successTimes.Add(now); guideEvents.Add((now, "Success")); break;
                case "Guide:Silent": cntSilent++; guideEvents.Add((now, "Silent")); break;
                case "Guide:Confused": guideEvents.Add((now, "Confused")); break;
                case "Guide:Found": guideEvents.Add((now, "Found")); break;
                case "Guide:Lead1": guideEvents.Add((now, "Lead1")); break;
                case "Guide:Lead2": guideEvents.Add((now, "Lead2")); break;
                case "Guide:Lead3": guideEvents.Add((now, "Lead3")); break;
                case "Guide:RoundUp": guideEvents.Add((now, "RoundUp")); break;
                case "Search:Start": cntSearchStart++; break;
                case "Search:Voice":
                    cntSearchVoice++;
                    if (searchEndAt >= 0f) searchVoicesAfterEnd++;
                    break;
                case "Search:End": cntSearchEnd++; if (searchEndAt < 0f) searchEndAt = now; break;
                case "T:Lost":
                    if (now > 8f) lastLostAt = now;
                    break;
                case "T:Acquired":
                    if (firstAcquireAt < 0f) firstAcquireAt = now;
                    if (now - lastLostAt >= 4f) acquireDeadline = now + 1.5f;
                    break;
            }
        };

        float dt = 1f / 30f;
        int ticks = Mathf.CeilToInt((scenario.TotalDuration + 3f) / dt);
        int invisibleStreak = 0;
        int c1DropBefore = -1;            // C1 窗口起点时的 Drop 计数快照
        float celebrateAccum = 0f;
        float celebrateRun = 0f;
        float celebrateAlphaMin = 1f;
        bool guideWasActive = false;          // 引导激活跟踪（打断矩阵断言）
        float guideActiveSince = -1f;
        bool guideInterruptReported = false;
        var prevIp = new Vector2(0.5f, 0.5f);
        float ipJumpMax = 0f; int ipJumpCnt = 0;
        bool c1Armed = false;
        float c1End = -1f;
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

            // I2 不隐形
            bool visible = driver.IpAnimProbe != 0;
            if (now > 4f && visible && driver.IpAlphaProbe < 0.18f)
            {
                if (invisibleStreak++ == 0)
                    violations.Add($"[隐形] t={now:F1}s anim={(IPWorldView.IPAnimState)driver.IpAnimProbe} alpha={driver.IpAlphaProbe:F2}");
            }
            else invisibleStreak = 0;

            // I6 流星雨庆典期间精灵全亮
            if (driver.FaceCelebrateProbe)
            {
                celebrateRun += dt; celebrateAccum += dt;
                if (celebrateRun > 1.0f) celebrateAlphaMin = Mathf.Min(celebrateAlphaMin, driver.IpAlphaProbe);
            }
            else celebrateRun = 0f;

            // I8 打断矩阵（2026-10-20）：引导激活后（>0.6s 容差）精灵不得仍在庆祝
            {
                int gp = driver.GuidePhaseProbe;
                bool ga = gp == 1 || gp == 2 || gp == 3; // Confused/Found/Leading
                if (ga && !guideWasActive) { guideActiveSince = now; guideInterruptReported = false; }
                if (!ga) guideActiveSince = -1f;
                guideWasActive = ga;
                if (ga && guideActiveSince > 0f && now - guideActiveSince > 0.6f
                    && driver.FaceCelebrateProbe && !guideInterruptReported)
                {
                    guideInterruptReported = true;
                    violations.Add($"[打断] t={now:F1}s 引导已激活 {now - guideActiveSince:F1}s，精灵仍在庆祝（应在 0.6s 内结束）");
                }
            }

            // C1 窗口：窗口开始后记录 Drop 快照，窗口结束时比对（假脸不应落星）
            if (!c1Armed && scenario.C1StartSec > 0f && now >= scenario.C1StartSec)
            {
                c1Armed = true; c1DropBefore = cntDrop; c1End = scenario.C1StartSec + 21f;
            }
            if (c1Armed && c1End > 0f && now >= c1End)
            {
                if (cntDrop != c1DropBefore)
                    violations.Add($"[假脸过滤] C1' 窗口内落星 {cntDrop - c1DropBefore} 颗（假脸不应触发）");
                c1End = -1f;
            }

            // I7 跳变诊断
            var ipNow = driver.IpPosProbe;
            float jump = Vector2.Distance(ipNow, prevIp);
            if (jump > ipJumpMax) ipJumpMax = jump;
            if (jump > 0.12f) ipJumpCnt++;
            prevIp = ipNow;

            // I3（2026-10-21 改口径）：看开视线（引导处于 Idle）后 1.4s 内须有 Voice:Find。
            // 不再等 TargetLost——老师可能仍在相机广角内（真机实测会拖 14s）。
            // 三轮后的静默期（Silent）是刻意不说话，不强制。
            bool lookingNow = driver.LookingProbe;
            if (prevLooking && !lookingNow && now > 8f && driver.GuidePhaseProbe == 0)
                gazeDeadline = now + 1.4f;
            prevLooking = lookingNow;

            // I4 截止判定
            if (gazeDeadline > 0 && now > gazeDeadline)
            {
                violations.Add($"[无引导语音] t={now:F1}s 看开视线后 1.4s 未见 Voice:Find");
                gazeDeadline = -1f;
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

        // ── I6 流星引导断言 ──
        // A1'：持续注视 → +20s 集满
        {
            float exp = scenario.A1StartSec + 20f;
            bool hit = false;
            foreach (var t in showerTimes) if (Mathf.Abs(t - exp) <= 0.6f) { hit = true; break; }
            if (scenario.A1StartSec > 0f && !hit)
                violations.Add($"[A1' 集满时机] 期望 {exp:F1}s±0.6 有流星雨，实际 [{string.Join(",", showerTimes.ConvertAll(x => x.ToString("F1")))}]");
        }
        // B1'：星星保留 + 累计 20s（重建段 +8s）
        {
            float exp = scenario.B1Seq3StartSec + 8f;
            bool hit = false;
            foreach (var t in showerTimes) if (Mathf.Abs(t - exp) <= 0.8f) { hit = true; break; }
            if (scenario.B1Seq3StartSec > 0f && !hit)
                violations.Add($"[B1' 累计语义] 期望 {exp:F1}s±0.8 有流星雨（12s+移开4s+回看8s）");
        }
        if (cntDrop < 16) violations.Add($"[落星计数] Star:Drop={cntDrop} < 16（A1'+B1' 应各 8 颗）");
        if (cntShower < 2) violations.Add($"[流星雨计数] Star:Shower={cntShower} < 2");
        if (cntCleared < 1) violations.Add($"[星环清空] Star:Cleared={cntCleared} < 1（老师久离应清空）");

        // G1'：引导链顺序断言（老师离场 32s 内）
        if (scenario.G1LeaveStartSec > 0f)
        {
            // 实际序列：RoundUp 后进入下一轮 Confused（疑惑文案不重复播，直接从"发现"重演）
            string[] pattern = { "Confused", "Found", "Lead1", "RoundUp", "Found", "Lead2", "RoundUp", "Found", "Lead3", "Silent" };
            int pi = 0;
            const float G1Window = 46f;
            foreach (var ev in guideEvents)
            {
                if (ev.t < scenario.G1LeaveStartSec || ev.t > scenario.G1LeaveStartSec + G1Window) continue;
                if (pi < pattern.Length && ev.kind == pattern[pi]) pi++;
            }
            if (pi < pattern.Length)
            {
                var win = new List<string>();
                foreach (var ev in guideEvents)
                    if (ev.t >= scenario.G1LeaveStartSec && ev.t <= scenario.G1LeaveStartSec + G1Window)
                        win.Add($"{ev.kind}@{ev.t - scenario.G1LeaveStartSec:F1}");
                violations.Add($"[引导链] G1' 顺序缺失（匹配到 {pi}/{pattern.Length}：期望 {string.Join("→", pattern)}）实际窗口：[{string.Join(", ", win)}]");
            }
        }
        // G2'：跟随成功（重获后 2.5s 内）
        if (scenario.G2ReacquireSec > 0f)
        {
            bool hit = false;
            foreach (var t in successTimes)
                if (t >= scenario.G2ReacquireSec && t <= scenario.G2ReacquireSec + 2.5f) { hit = true; break; }
            if (!hit) violations.Add($"[跟随成功] G2' 重获后 2.5s 内未见 Guide:Success");
        }

        // ── 首寻 3D 漫游（2026-10-20）：第一次找到老师前持续语音+漫游，首次锁定 0.6s 内停止 ──
        if (cntSearchStart != 1) violations.Add($"[首寻] Search:Start={cntSearchStart}（应恰为 1）");
        if (cntSearchVoice < 2) violations.Add($"[首寻] Search:Voice={cntSearchVoice} < 2（开局无老师应持续语音吸引）");
        if (cntSearchEnd != 1) violations.Add($"[首寻] Search:End={cntSearchEnd}（首次看到老师应恰结束一次）");
        if (firstAcquireAt > 0f && searchEndAt > 0f && searchEndAt > firstAcquireAt + 0.6f)
            violations.Add($"[首寻] 首次锁定 {firstAcquireAt:F1}s 后 {searchEndAt - firstAcquireAt:F1}s 才停止搜索（应 ≤0.6s）");
        if (searchVoicesAfterEnd > 0)
            violations.Add($"[首寻] 搜索结束后仍播了 {searchVoicesAfterEnd} 句搜索语音");

        Debug.Log($"[SIM] ⏱ {scenario.TotalDuration / 60f:F1} 分钟剧本 / {ticks} tick / 实跑 {sw.Elapsed.TotalSeconds:F1}s");
        Debug.Log($"[SIM] 计数 Voice:Find={cntFind} Fx:Lock={cntLock} Fx:Feedback={cntFb} Gaze:Sustained={cntGaze} Fx:Appear={cntAppear}");
        Debug.Log($"[SIM] 流星：Drop={cntDrop} Shower={cntShower} [{string.Join(", ", showerTimes.ConvertAll(t => t.ToString("F1")))}] " +
                  $"Cleared={cntCleared} 引导Success={cntSuccess} Silent={cntSilent} 流星雨alphaMin={celebrateAlphaMin:F2}");
        Debug.Log($"[SIM] 首寻：Start={cntSearchStart} Voice={cntSearchVoice} End={cntSearchEnd} " +
                  $"首次锁定={firstAcquireAt:F1}s 搜索结束={searchEndAt:F1}s 结束后语音={searchVoicesAfterEnd}");
        Debug.Log($"[SIM] I7视觉跳变（诊断）：单帧最大 {ipJumpMax:F3}，>0.12 共 {ipJumpCnt} 次（{ipJumpCnt / (scenario.TotalDuration / 60f):F1}/分钟）");
        UnityEngine.Object.DestroyImmediate(go);
        return violations;
    }
}
