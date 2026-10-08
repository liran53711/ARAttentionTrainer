# M3 实施计划：核心交互循环（电脑端）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在电脑上用 TED 视频 + 键盘模拟输入，跑通「引导 → 注视 → 隐藏 → 反馈」核心交互循环（含 IP 占位动画、音效、训练日志)。

**Architecture:** 全部核心逻辑写成**纯 C# 类**（可单元测试，不依赖 Unity 场景），MonoBehaviour 只做胶水。视线判定用"目标在视野中心附近"的视口代理（真机阶段换成世界角度版，逻辑同构）。讲话输入在 M3 用键盘模拟（真 VAD 在 M4）。

**Tech Stack:** Unity 6.4 · C# 纯逻辑 + EditMode NUnit 测试 · OnGUI 调试显示 · 运行时生成音效（无外部素材）

**Spec:** `docs/specs/2026-10-07-ar-attention-design.md`（§3 状态机、§4.2.1 头部启发式、§5 日志）

## Global Constraints

- 纯逻辑类不引用 MonoBehaviour/场景，可在 EditMode 直接测试
- 所有阈值走 `ExperienceSettings`，禁止散落魔法数字
- IP 素材是占位（程序生成圆形贴图）；组员产出真实素材后替换（接口不变）
- 设备专属（PCA 帧源、真头姿、世界空间 IP、空间锚、VAD）**本计划不做**，等 Meta SDK 207 到货后作为 M3.5 合入
- 每个任务完成后用户跑测试 + commit（消息我给）

---

## 任务总览与依赖

```
T1 OneEuroFilter ─┬─► T2 TargetTrackerLogic ─┬─► T4 InteractionStateMachine ─┬─► T8 DebugDriver+场景（总装）
T3 GazeLogic ─────┴──────────────────────────┘                              │
T5 ExperienceSettings 扩展 ─────────────────────────────────────────────────┤
T6 IP占位动画 + 音效 ───────────────────────────────────────────────────────┤
T7 SessionLogger ───────────────────────────────────────────────────────────┘
```

---

### Task 1: OneEuroFilter（平滑滤波，纯逻辑）

**Files:**
- Create: `Assets/Scripts/Core/OneEuroFilter.cs`
- Test: `Assets/Tests/EditMode/OneEuroFilterTests.cs`

**Interfaces:**
- Produces: `class OneEuroFilter { OneEuroFilter(float minCutoff=1f, float beta=0.05f); float Filter(float value, float dt); void Reset(); }`（标量版；Vector2 用两个实例组合）

- [ ] **Step 1: 写失败测试**

```csharp
using NUnit.Framework;
using System.Collections.Generic;

public class OneEuroFilterTests
{
    [Test]
    public void StepInput_ConvergesToTarget()
    {
        var f = new OneEuroFilter();
        f.Filter(0f, 1f / 30f);
        float last = 0f;
        for (int i = 0; i < 300; i++) last = f.Filter(10f, 1f / 30f);
        Assert.Less(System.Math.Abs(last - 10f), 0.1f);
    }

    [Test]
    public void JitteryInput_OutputIsSmootherThanInput()
    {
        var f = new OneEuroFilter();
        var outputs = new List<float>();
        for (int i = 0; i < 200; i++)
        {
            float noisy = 0.5f + (i % 2 == 0 ? 0.02f : -0.02f); // 模拟检测框抖动
            outputs.Add(f.Filter(noisy, 1f / 30f));
        }
        // 后 100 帧输出极差应远小于输入极差（0.04）
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 100; i < 200; i++) { min = System.Math.Min(min, outputs[i]); max = System.Math.Max(max, outputs[i]); }
        Assert.Less(max - min, 0.01f);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**（Test Runner EditMode → 编译错 `OneEuroFilter` 不存在）
- [ ] **Step 3: 实现**：经典 One Euro Filter（值滤波 + 导数自适应截止频率；算法参考 https://cristal.univ-lille.fr/~casiez/1euro/ 论文公式）
- [ ] **Step 4: 跑测试确认通过**（2 绿）
- [ ] **Step 5: 用户 commit**：`feat: OneEuroFilter 平滑滤波（含单元测试）`

---

### Task 2: TargetTrackerLogic（目标选择 + 头部点 + 丢失/冻结）

**Files:**
- Create: `Assets/Scripts/Tracking/TargetTrackerLogic.cs`
- Test: `Assets/Tests/EditMode/TargetTrackerLogicTests.cs`

**Interfaces:**
- Produces:
```csharp
public struct TargetSnapshot {
    public bool valid;           // 锚点可用（含冻结期）
    public bool frozen;          // 丢失>lostKeepSec，位置冻结
    public Vector2 headPoint01;  // 平滑后的头部点（归一化，左上原点）
    public float confidence;
    public float lastSeenSeconds; // 距上次检测到的秒数
}
public class TargetTrackerLogic {
    public TargetTrackerLogic(float switchHysteresis = 1.25f, float lostKeepSec = 1f, float idleDropSec = 10f);
    public TargetSnapshot Tick(List<DetectionResult> detections, float dt);
    public void Reset();
    public static Vector2 HeadPointOf(Rect normalizedBox); // 框顶+25%高度处水平中心
}
```

**逻辑规格**（测试要覆盖）：
1. `HeadPointOf`: box=(0.2,0.2,0.4,0.4) → headPoint=(0.4, 0.3)（y=0.2+0.4×0.25）
2. 无历史时选**面积最大**的目标
3. 滞回：已有目标 A；新候选 B 面积 > A×1.25 才切换；否则保持 A
4. 短暂丢失(<1s)：valid=true 不冻结，位置保持
5. 丢失≥1s：frozen=true，位置冻结；≥10s：valid=false
6. 输出已过 OneEuro 平滑（抖动输入 → 输出稳定）

- [ ] **Step 1: 写失败测试**（覆盖规格 1-6，例如：）

```csharp
[Test]
public void HeadPoint_IsUpperCenterOfBox()
{
    var p = TargetTrackerLogic.HeadPointOf(new UnityEngine.Rect(0.2f, 0.2f, 0.4f, 0.4f));
    Assert.AreEqual(0.4f, p.x, 1e-4f);
    Assert.AreEqual(0.3f, p.y, 1e-4f);
}

[Test]
public void Switch_RequiresSignificantLargerCandidate()
{
    var t = new TargetTrackerLogic(switchHysteresis: 1.25f);
    var big = new List<DetectionResult> { D(0.1f, 0.1f, 0.4f, 0.4f, 0.9f) };   // 面积0.16
    t.Tick(big, 1f / 30f);
    var slightlyBigger = new List<DetectionResult> { D(0.5f, 0.5f, 0.42f, 0.42f, 0.9f) }; // 面积0.176 = ×1.10 → 不切换
    var s = t.Tick(slightlyBigger, 1f / 30f);
    Assert.AreEqual(0.3f, s.headPoint01.x, 0.05f); // 仍在旧目标附近(0.1+0.4/2=0.3)
}

[Test]
public void Lost_Over1s_FreezesPosition()
{
    var t = new TargetTrackerLogic(lostKeepSec: 1f);
    t.Tick(new List<DetectionResult> { D(0.1f, 0.1f, 0.2f, 0.2f, 0.9f) }, 1f / 30f);
    for (int i = 0; i < 40; i++) t.Tick(new List<DetectionResult>(), 1f / 30f); // 1.33s 无检测
    var s = t.Tick(new List<DetectionResult>(), 1f / 30f);
    Assert.IsTrue(s.valid);
    Assert.IsTrue(s.frozen);
}
```

- [ ] **Step 2: 跑测试确认失败**
- [ ] **Step 3: 实现**（选择/滞回/丢失状态机 + 两个 OneEuroFilter 平滑 headPoint）
- [ ] **Step 4: 跑测试确认通过**
- [ ] **Step 5: 用户 commit**：`feat: TargetTracker 目标选择+头部点+丢失冻结（含单元测试）`

---

### Task 3: GazeLogic（注视判定代理 + 时长）

**Files:**
- Create: `Assets/Scripts/Gaze/GazeLogic.cs`
- Test: `Assets/Tests/EditMode/GazeLogicTests.cs`

**Interfaces:**
```csharp
public class GazeLogic {
    public GazeLogic(float startSeconds = 0.8f, float sustainSeconds = 2.0f, float decayRate = 1f);
    public float Dwell { get; }                 // 注视累计秒数
    public bool Looking { get; }                // 当前帧是否注视
    public bool GazeStarted { get; }            // 本帧：dwell 首次跨过 startSeconds
    public bool GazeSustained { get; }          // 本帧：dwell 首次跨过 sustainSeconds
    public void Tick(bool looking, float dt);
    public static bool IsLookingAt(Vector2 target01, float radius = 0.28f); // 与视口中心(0.5,0.5)的距离判圆
}
```
> 电脑端为视口代理；真机阶段加一个"世界角度版"静态方法，逻辑类不变。

**测试覆盖**：中心点=lookAt；角落(0.05,0.05)=非注视；连续 looking 0.85s 触发 GazeStarted（且只触发一次）；中断后 decay；再重看会再次触发。

- [ ] **Step 1-4: TDD 循环（同上模式）**
- [ ] **Step 5: commit**：`feat: GazeLogic 注视判定与时长累计（含单元测试）`

---

### Task 4: InteractionStateMachine（核心状态机）

**Files:**
- Create: `Assets/Scripts/Interaction/InteractionStateMachine.cs`
- Test: `Assets/Tests/EditMode/InteractionStateMachineTests.cs`

**Interfaces:**
```csharp
public enum InteractionState { Idle, Guiding, Hidden, Feedback }
public struct InteractionInputs {
    public bool speaking;        // 讲话中
    public bool speechJustEnded; // 本帧恰逢讲话段结束（上升/下降沿由驱动层提供）
    public bool looking;         // 当前注视
    public bool gazeStarted;     // 来自 GazeLogic
    public bool gazeSustained;   // 来自 GazeLogic
    public bool targetValid;     // 来自 TargetTrackerLogic
    public float dt;
}
public class InteractionStateMachine {
    public InteractionStateMachine(
        float reGuideCooldownSec = 3f, float feedbackDurationSec = 2f,
        float feedbackCooldownSec = 2f, float idleTimeoutSec = 10f);
    public InteractionState State { get; }
    public float StateTime { get; }
    public bool GuideTriggered { get; }    // 本帧瞬态事件（驱动 IP 动画）
    public bool FeedbackTriggered { get; } // 本帧瞬态事件
    public void Tick(InteractionInputs input);
}
```

**转换表（测试逐条覆盖）**：

| 当 | 条件 | 转去 |
|----|------|------|
| Idle | speaking ∧ ¬looking ∧ targetValid ∧ 冷却已过 | → Guiding（发 GuideTriggered） |
| Guiding | gazeStarted | → Hidden |
| Guiding | ¬targetValid ∨ (¬speaking ∧ 超过idleTimeout) | → Idle |
| Hidden | gazeSustained ∧ speechJustEnded | → Feedback（发 FeedbackTriggered） |
| Hidden | ¬looking ∧ speaking ∧ StateTime≥reGuideCooldownSec | → Guiding（发 GuideTriggered） |
| Hidden | ¬speaking ∧ ¬gazeSustained ∧ StateTime≥2s | → Idle（讲完没看够，不给反馈） |
| Feedback | StateTime≥feedbackDurationSec | → speaking∧¬looking ? Guiding : Idle（受反馈冷却约束） |
| 任意 | ¬targetValid | → Idle |

- [ ] **Step 1: 写失败测试**（每行转换表 ≥1 个测试，含冷却期防抖测试：冷却中不重复触发）
- [ ] **Step 2-4: TDD 循环**
- [ ] **Step 5: commit**：`feat: 交互状态机（引导/隐藏/反馈转换表+冷却，含单元测试）`

---

### Task 5: ExperienceSettings 扩展（M3 参数）

**Files:**
- Modify: `Assets/Scripts/Core/ExperienceSettings.cs`
- Modify: `Assets/Scripts/Editor/DevSceneCreator.cs`（无逻辑变化，仅确保新字段默认值生效）

- [ ] **Step 1: 加字段**（全部带 Tooltip）：

```csharp
[Header("M3 交互参数")]
public float gazeStartSeconds = 0.8f;      // 开始注视：连续看这么久的注视才"算数"
public float gazeSustainSeconds = 2.0f;    // 注视达成：累计到这么久才给反馈
public float gazeViewportRadius = 0.28f;   // 注视判定：目标离屏幕中心多近算"在看"
public float reGuideCooldownSeconds = 3f;  // 重新引导冷却
public float feedbackDurationSeconds = 2f; // 反馈动画时长
public float idleTimeoutSeconds = 10f;     // 没讲话这么久 → 待机
public float logSampleEverySeconds = 2f;   // 日志采样间隔
```

- [ ] **Step 2: commit**：`feat: ExperienceSettings 补充 M3 交互参数`

---

### Task 6: IP 占位动画 + 音效（程序生成，零素材）

**Files:**
- Create: `Assets/Scripts/Interaction/IPDebugView.cs`（OnGUI 画占位小精灵）
- Create: `Assets/Scripts/Audio/AudioCuePlayer.cs`（运行时生成音效）

**Interfaces:**
- `IPDebugView.SetVisual(float x01, float y01, float alpha, float scale, Color tint)`——驱动层每帧喂状态，视图层不持逻辑
- `AudioCuePlayer.PlayCue(AudioCue cue)`，`enum AudioCue { GuideAppear, Hide, Feedback }`

**要点**：
- 占位贴图：64×64 程序生成圆形（带描边），`Texture2D.SetPixels` 一次生成
- 音效：`AudioClip.Create` + 正弦波合成（GuideAppear=上行双音；Hide=下行软音；Feedback=三音琶音），带 20ms 淡入淡出防爆音
- OnGUI 坐标：`y01` 是左上原点归一化（与 DetectionResult 一致），`GUI` 也是左上原点，直接换算

- [ ] **Step 1: 实现两个文件**（无单测；逻辑极少，靠 T8 实测）
- [ ] **Step 2: commit**：`feat: IP 占位动画视图 + 程序生成音效`

---

### Task 7: SessionLogger（训练数据记录）

**Files:**
- Create: `Assets/Scripts/Logging/SessionLogger.cs`
- Test: `Assets/Tests/EditMode/SessionLoggerTests.cs`（格式化函数级）

**Interfaces:**
```csharp
public class SessionLogger {
    public SessionLogger(string rootDir, string childName = "", string ipName = "", int interventionLevel = 1);
    public string SessionDir { get; }
    public void LogEvent(string type, string detail = "");
    public void LogSample(float t, bool looking, bool speaking, bool targetValid, float fps, float inferMs);
    public void Close();
    // 静态格式化（单测覆盖）
    public static string FormatSampleCsv(float t, bool looking, bool speaking, bool targetValid, float fps, float inferMs);
    public static string BuildSessionFolderName(System.DateTime t); // yyyyMMdd_HHmmss
}
```
**文件结构**：`<root>/sessions/<时间戳>/`：`session.json`（元数据）、`events.jsonl`（一行一事件）、`samples.csv`。**不记录任何图像/音频**。
**事件类型**：GuideStart / GazeStart / GazeSustained / Feedback / SpeechStart / SpeechEnd / TargetLost / TargetAcquired / SessionStart / SessionEnd。

- [ ] **Step 1: 写失败测试**（CSV 行格式：`"t,looking,speaking,targetValid,fps,inferMs"` 表头 + 数据行；文件夹名格式正则）
- [ ] **Step 2-4: TDD 循环**
- [ ] **Step 5: commit**：`feat: SessionLogger 训练数据记录（JSONL+CSV，格式化含测试）`

---

### Task 8: M3DebugDriver 总装 + 场景 + 验收

**Files:**
- Create: `Assets/Scripts/Core/M3DebugDriver.cs`
- Modify: `Assets/Scripts/Editor/DevSceneCreator.cs`（场景加挂 driver）
- Modify: `docs/measurements/`（验收后记录）

**M3DebugDriver 职责**（胶水层，尽量薄）：
1. 每帧：拉 `detector.Latest` → `TargetTrackerLogic.Tick` → 计算 looking（`GazeLogic.IsLookingAt` 或键盘覆盖）→ `GazeLogic.Tick` → `InteractionStateMachine.Tick`（讲话默认真，L 键切；切"讲完"时产生 speechJustEnded 沿）
2. 状态机输出 → 驱动 IP tween（Appearing 0.5s 渐入 → 移向 headPoint 1.2s；Hiding 1s 渐隐；Reappearing 随机偏移弹出；Feedback 2s 脉冲）+ 音效
3. 日志：事件 + 每 2s 采样；目标丢失/重获打事件
4. 键盘调试：`K`=切换模拟注视　`L`=切换模拟讲话　`R`=重置会话

**场景更新**：DevEditor 加挂 M3DebugDriver 并接引用（在 DevSceneCreator 里自动接好）。

- [ ] **Step 1: 实现 driver + 更新 DevSceneCreator**
- [ ] **Step 2: 👤 用户 Play 验收**（脚本）：
  1. 默认状态（讲话=真，未注视）→ IP 占位圆 **出现并移向画面中的人**，有引导音效
  2. 按 **K**（模拟孩子看向老师）→ 持续 0.8s 后 IP **渐隐**（隐藏=交付注意力），音效
  3. 保持看 2s + 按 **K** 恢复未注视同时按 **L** 切"讲完" → **反馈**（IP 随机位置重现+闪光+琶音）→ 冷却后循环
  4. 中途再按 **K** 切回"未注视"（讲话仍在）→ 3 秒冷却后 IP **温和重新出现**
  5. 查看 `%USERPROFILE%\AppData\LocalLow\DefaultCompany\ARAttentionTrainer\sessions\<最新>\` 下三个文件生成且内容合理
- [ ] **Step 3: 用户 commit**：`feat: M3 核心交互循环跑通（引导/隐藏/反馈+日志+音效），电脑端验收通过`

---

## 自审记录

- **Spec 覆盖**：§3.1 状态机转换表 ✓（T4）、§3.2 阈值定义 ✓（T3/T5）、§3.3 干预档位（弱差异仅体现在参数默认值，手动档 UI 在 M4）✓、§4.2.1 头部启发式 ✓（T2）、§5 日志 ✓（T7）。§4.3 真机 3D 定位、§4.4 VAD、§6 权限降级 → 设备阶段（M3.5/M4），非本计划范围。（检测的"人框"→"头部点"转换在 T2 完成，对应 §4.2.1 第 1 条。）
- **占位符扫描**：无 TBD；T6 无单测是有意（纯视图），理由已注明。
- **类型一致性**：`TargetSnapshot.headPoint01` ↔ T8 使用；`GazeLogic` 瞬态标志与 `InteractionInputs` 字段一一对应；`DetectionResult.normalizedBox`（Rect, 左上原点）贯穿 T2。
