# 注意力引导 v8 问题修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **状态：已确认（2026-10-24）。**
> - 注视期：先 IdleFly + Smile/Curious 去掉挥手；建模师 `Listen` 到位后替换。
> - 外界讲话：精灵语音全部延后；Critical 留一条到静默后；自音频 0.3s 尾。
> - 建模师：要做 `Listen` 动作（见 spec §4）。
>
> **项目 Git 规范：Claude 不执行 `git add/commit/push`；每个任务末尾的提交命令
> 由用户手动执行。**

**Goal:** 让星路一眼看出方向、进度星星可辨认、注视时精灵不再挥手并能在对话中安静倾听，
同时把外界讲话时的精灵语音降为零（不盖住老师）。

**Architecture:** 在现有 `StardustPath`/`MeteorSystem`/`IPWorldView`/`M3DebugDriver`/
`VoiceCuePlayer` 上做增量修改；新增一个纯逻辑 `ConversationGateLogic` 统一“外界讲话/
自音频/语音放行”的判断，并新增 `Attentive`/`Listening` 两个精灵状态；所有可视化改动
先走离线预览，再真机验证。

**Tech Stack:** Unity 6000.4.3f1 / URP / Meta XR SDK 207 / C# / EditMode 测试 /
M3SimHarness 模拟机 / edge-tts 语音（现有音色）。

**Spec:** `docs/specs/2026-10-24-attention-guide-issues.md`

## Global Constraints

- 不读压缩包（项目规范）；不执行 git 写操作（项目规范）。
- 所有新逻辑先写失败测试（TDD）；每个任务结束跑 EditMode 全量 + 模拟机 3 种子。
- 不改用户准备替换的表情/动画美术素材（问题 4）。
- 不合并组员 v1.1 的流星引导代码；本计划只针对当前工程。
- 真机验证必须用手柄启动；adb 启动会被 Horizon 拦截。
- 语音优先级：`Critical=0 < Guide=1 < Ambient=2`（数字越小越高）。
- 注视期“看老师”口径：`_attending = snapshot.valid && looking`（现有实现）。

---

### Task 1: 星路/语音/倾听的会话诊断

**Files:**
- Modify: `Assets/Scripts/UI/StardustPath.cs`
- Modify: `Assets/Scripts/Core/M3DebugDriver.cs`
- Test: `Assets/Tests/EditMode/StardustPathTests.cs`

**Interfaces:**
- Produces: `StardustPath.DebugSnapshot()` → `string`，格式
  `start=(x,y,z) end=(x,y,z) teacher=(x,y,z) angle=±NN len=N.NN width=N.NN dots=N`。
- Produces: 会话事件 `StarPath`（每 2s 一条，路径开启时）；
  事件 `VoiceGate`（门控状态变化时）；事件 `ListenState`（进入/退出 Attentive/Listening）。

- [ ] **Step 1: 写失败测试（格式化函数）**

在 `StardustPathTests.cs` 增加：

```csharp
[Test]
public void DebugSnapshot_ContainsGeometryFields()
{
    var s = StardustPath.FormatSnapshot(
        new Vector3(0, 1, 1), new Vector3(0, 0, 3), new Vector3(0, 1, 3),
        new Vector3(0, 1.6f, 0), 0f, 0.25f, 150);
    StringAssert.Contains("len=", s);
    StringAssert.Contains("width=0.25", s);
    StringAssert.Contains("angle=", s);
}
```

- [ ] **Step 2: 运行并确认失败**

```bash
Unity.exe -batchmode -projectPath D:/Development/Unity/ARAttentionTrainer \
  -runTests -testPlatform EditMode \
  -testResults D:/Development/Unity/ARAttentionTrainer/Builds/editmode-results.xml \
  -logFile D:/Development/Unity/ARAttentionTrainer/Builds/editmode.log
```

Expected: compile error / `FormatSnapshot` not found.

- [ ] **Step 3: 实现格式化 + 接入会话事件**

`StardustPath` 增加纯静态函数：

```csharp
public static string FormatSnapshot(Vector3 start, Vector3 end, Vector3 teacher,
                                   Vector3 eye, float floorY, float width, int dots)
{
    Vector3 dirT = teacher - eye; dirT.y = 0f;
    Vector3 fwd = Vector3.forward; // 调用方传入真实前向时可通过重载
    float ang = dirT.sqrMagnitude > 1e-4f
        ? Vector3.SignedAngle(fwd, dirT.normalized, Vector3.up) : 0f;
    return $"start={start.ToString("F2")} end={end.ToString("F2")} " +
           $"teacher={teacher.ToString("F2")} eye={eye.ToString("F2")} " +
           $"floor={floorY:F2} len={Vector3.Distance(start, end):F2} " +
           $"width={width:F2} dots={dots} angle={ang:F0}";
}

public string DebugSnapshot() =>
    FormatSnapshot(_diagStart, _diagEnd, _diagTeacher, eye != null ? eye.position : Vector3.zero,
                   _diagFloorY, _lastHalfWidth, starDots + BeaconDots);
```

（在 `Tick` 中保存 `_diagFloorY = floorY; _lastHalfWidth = halfWidth;`；
真实前向可在 `FormatSnapshot` 重载中传入，避免角度基准不对。）

`M3DebugDriver` 在星路开启分支中每 2s：

```csharp
_pathLogT += dt;
if (_pathLogT >= 2f)
{
    _pathLogT = 0f;
    _logger.LogEvent("StarPath", stardustPath.DebugSnapshot());
    Signal("Path", "Update");
}
```

- [ ] **Step 4: 运行测试并确认通过**

同 Step 2；Expected: `DebugSnapshot_ContainsGeometryFields` 通过，全量 110 项通过。

- [ ] **Step 5: 用户手动提交**

```bash
git add Assets/Scripts/UI/StardustPath.cs Assets/Scripts/Core/M3DebugDriver.cs \
        Assets/Tests/EditMode/StardustPathTests.cs
git commit -m "test: 星路几何会话诊断" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 2: 星路方向/宽度重设计

**Files:**
- Modify: `Assets/Scripts/UI/StardustPath.cs`
- Modify: `Assets/Tests/EditMode/StardustPathTests.cs`
- Modify: `Assets/Scripts/Editor/StardustPreview.cs`

**Interfaces:**
- Consumes: existing `MeteorLogic.Bezier/PathControl`, `_smooth` path, `starMat`.
- Produces: `RoadHalfWidthFor` new formula; path with centerline + tapered sides +
  a moving comet; `DebugSnapshot` includes length/width.

**参数（默认值）：**
`starDots=150`、`centerlineFraction=0.40f`、`maxHalfWidth=0.28f`、
`taper=0.62f`、`flowSpeed=0.20f`、`cometSpeed=0.55f`、`cometSize=0.18f`。

- [ ] **Step 1: 更新失败测试（宽度）**

`StardustPathTests.RoadWidth_ShrinksForShortPaths` 改为：

```csharp
Assert.AreEqual(0.10f, StardustPath.RoadHalfWidthFor(0.4f, 0.28f), 0.01f);
Assert.AreEqual(0.28f, StardustPath.RoadHalfWidthFor(5f, 0.28f), 0.001f);
Assert.AreEqual(0.20f, StardustPath.RoadHalfWidthFor(2f, 0.28f), 0.01f);
```

- [ ] **Step 2: 运行确认失败**

Expected: 旧的 `RoadHalfWidthFor` 返回 0.28/0.28 等，断言失败。

- [ ] **Step 3: 实现宽度公式与中心线/侧边分层**

```csharp
public static float RoadHalfWidthFor(float pathLength, float maxHalfWidth)
{
    return Mathf.Clamp(pathLength * 0.10f, 0.10f, maxHalfWidth);
}
```

`Tick` 中点循环拆成两段（中心线 + 侧边）：

```csharp
int centerCount = Mathf.RoundToInt(n * centerlineFraction);
for (int i = 0; i < centerCount; i++)
{
    float t = Mathf.Repeat((i + 0.5f) / Mathf.Max(1, centerCount) + _flow, 1f);
    Vector3 p = CubicBezier(_smooth.p0, _smooth.c1, _smooth.c2, _smooth.p3, t);
    Vector3 side = Vector3.Cross(CubicBezier(..., t + 0.01f) - CubicBezier(..., t - 0.01f),
                                 Vector3.up).normalized;
    if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(fwd, Vector3.up);
    p += side * (_jSide[i] * 0.08f) + Vector3.up * (0.03f + _jUp[i] * 0.08f);
    float fade = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((1f - t) / 0.08f);
    float tw = 0.65f + 0.35f * Mathf.Sin(now * (2.2f + 2.4f * _phase[i]) + _phase[i]);
    float size = starDotSize * 1.25f * _sMul[i] * (0.85f + 0.15f * tw) * fade;
    PlaceDot(i, p, size, fade * (0.8f + 0.2f * tw), ColorFor(0.1f)); // 暖金/白
}

int sideCount = n - centerCount;
for (int j = 0; j < sideCount; j++)
{
    int i = centerCount + j;
    float t = Mathf.Repeat((j + 0.5f) / Mathf.Max(1, sideCount) + _flow, 1f);
    float width = RoadHalfWidthFor(pathLen, maxHalfWidth) *
                  Mathf.Lerp(1f, taper, t); // 起点宽、终点窄
    // ... 与中心线相同的采样，jitter = _jSide[i] * width
    float size = starDotSize * 0.75f * _sMul[i] * ...;
    PlaceDot(i, p, size, fade * (0.55f + 0.2f * tw), ColorFor(0.8f)); // 淡紫
}
```

- [ ] **Step 4: 加一颗沿路移动的“指路彗星”**

在 `EnsureBuilt` 建一个额外的彗星 quad（不占 150 颗），在 `Tick` 中：

```csharp
_cometT = Mathf.Repeat(_cometT + cometSpeed * dt, 1f);
Vector3 cp = CubicBezier(_smooth.p0, _smooth.c1, _smooth.c2, _smooth.p3, _cometT);
_comet.position = cp;
_comet.rotation = Quaternion.LookRotation(cp - eye.position, Vector3.up);
_comet.localScale = Vector3.one * cometSize * (0.9f + 0.1f * Mathf.Sin(now * 6f));
_cometR.enabled = true;
var cm = _cometR.sharedMaterial;
if (cm != null) cm.SetColor("_Color", new Color(1f, 0.96f, 0.78f, 1f));
```

彗星从起点飞向老师，循环；这是“方向感”的主视觉。

- [ ] **Step 5: 离线预览验收**

```bash
"D:/Development/Unity/Editor/Unity.exe" -batchmode \
 -projectPath "D:/Development/Unity/ARAttentionTrainer" \
 -executeMethod StardustPreview.Render \
 -logFile "D:/Development/Unity/ARAttentionTrainer/Builds/stardust-preview-v9.log" -quit
```

检查 `C:/Users/Administrator/.claude/jobs/bbf5228a/tmp/stardust_preview/*.png`：
中心线、锥形宽度、彗星方向；若仍像星团，调整 `centerlineFraction/taper/width` 后再渲染。

- [ ] **Step 6: 运行全量测试**

Expected: 110/110 通过。

- [ ] **Step 7: 用户手动提交**

```bash
git add Assets/Scripts/UI/StardustPath.cs Assets/Tests/EditMode/StardustPathTests.cs \
        Assets/Scripts/Editor/StardustPreview.cs
git commit -m "feat: 星路中心线/锥形宽度/指路彗星" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 3: 进度星环变大 + celebrate 烟花接线

**Files:**
- Modify: `Assets/Scripts/UI/MeteorSystem.cs`
- Modify: `Assets/Scripts/UI/IpEffects.cs`
- Test: `Assets/Tests/EditMode/FlipbookCelebrateTests.cs`（新建）

**Interfaces:**
- `MeteorSystem.ringStarSize`（默认 0.24f）、`ringRadius`（默认 0.42f）。
- `IpEffects.CelebratePlaying`（bool 探针）；`Pulse("Celebrate")` 启动 celebrate
  flipbook（0.9s 冷却）。

- [ ] **Step 1: 写失败测试（celebrate 脉冲）**

```csharp
[Test]
public void CelebratePulse_StartsCelebrateFlipbook()
{
    var go = new GameObject("fx");
    var fx = go.AddComponent<IpEffects>();
    fx.EnsureForTest();
    Assert.IsFalse(fx.CelebratePlaying);
    fx.Pulse("Celebrate");
    Assert.IsTrue(fx.CelebratePlaying);
    Object.DestroyImmediate(go);
}
```

`IpEffects` 增加 `public void EnsureForTest() => EnsureInit();` 与
`public bool CelebratePlaying => _celebrateFlip != null && _celebrateFlip.IsPlaying;`。

- [ ] **Step 2: 运行确认失败**（`CelebratePlaying` 不存在）

- [ ] **Step 3: 实现 celebrate 接线**

`IpEffects` 增加：

```csharp
Transform _celebrate; MeshRenderer _celebrateR; FlipbookEffect _celebrateFlip;
float _nextCelebBurst;

void EnsureCelebrateFlipbook()
{
    if (_celebrate != null) return;
    _celebrate = MakeQuad("Celebrate", starMat != null ? starMat : glowMat, out _celebrateR);
    if (FlipbookLibrary.TryGet("celebrate", out var c))
    {
        _celebrateFlip = _celebrate.gameObject.AddComponent<FlipbookEffect>();
        _celebrateFlip.Bind(_celebrateR, c.atlas, c.cols, c.rows, c.fps, c.loop);
        _celebrateFlip.Stop();
    }
}
```

在 `EnsureInit`/`EnsureFlipbooks` 调用；`Pulse("Celebrate")` 中：

```csharp
EnsureCelebrateFlipbook();
if (_celebrateFlip != null && Time.time >= _nextCelebBurst)
{
    _nextCelebBurst = Time.time + 0.9f;
    Vector3 eyePos = EyePos();
    _celebrate.position = transform.position + Vector3.up * 0.15f;
    _celebrate.rotation = Quaternion.LookRotation(_celebrate.position - eyePos, Vector3.up);
    _celebrate.localScale = Vector3.one * 1.15f;
    _celebrateR.enabled = true;
    _celebrateFlip.Play(true);
}
```

- [ ] **Step 4: 放大进度星环**

`MeteorSystem`：

```csharp
[Tooltip("进度星环单颗尺寸（米）")] public float ringStarSize = 0.24f;
public float ringRadius = 0.42f;
```

替换 `MakeQuad(go.transform, 0.16f)`、`localScale = 0.16f * tw`、
`BurstStar baseScale = Vector3.one * 0.16f` 为 `ringStarSize`。

- [ ] **Step 5: 运行测试 + 设备预览**

EditMode 全量；设备上确认进度星环明显变大、celebrate 烟花在流星雨时出现。

- [ ] **Step 6: 用户手动提交**

---

### Task 4: 注视期 Attentive/Listening 状态（无挥手）【需用户确认】

**Files:**
- Modify: `Assets/Scripts/Core/M3DebugDriver.cs`
- Modify: `Assets/Scripts/UI/IPWorldView.cs`
- Modify: `Assets/Scripts/UI/IpEffects.cs`
- Modify: `Assets/Scripts/UI/StickFigureIp.cs`
- Modify: `Assets/Scripts/Editor/M3SimHarness.cs`

**Interfaces:**
- `IpAnim.Attentive=7, Listening=8`；`IPWorldView.IPAnimState.Attentive=7, Listening=8`。
- `M3DebugDriver` 在 `_attending && !_guideActive && !_celebrating &&
  _ipAnim != FeedbackPulse/Peek/Hidden` 时进入 `Attentive`；
  `_listening`（Task 6 提供）为 true 时进入 `Listening`。
- Animator 映射（短期）：`Attentive/Listening` 都令 `Guiding=false, Companion=false`
  → `IdleFly`；表情 `Smile`/`Curious`；不设置尚不存在的 Animator 参数。
- 建模师 `Listen` 动作到位后，Task 7 再改为 `Listening` 布尔/状态。

- [ ] **Step 1: 写失败测试（模拟机探针）**

在 `M3SimHarness` 加断言：场景中 `looking=1` 且引导未激活时，
`driver.IpAnimProbe` 必须是 `Attentive`（7）或 `Listening`（8），不能是 `Companion`（3）。

- [ ] **Step 2: 运行确认失败**（当前是 Companion）

- [ ] **Step 3: 实现枚举/驱动/映射**

`M3DebugDriver.UpdateIpAnim` 在 `case IpAnim.Companion` 之前加：

```csharp
case IpAnim.Attentive:
case IpAnim.Listening:
{
    _ipPos = tgt + new Vector2(_companionSideSmooth * 0.10f,
             -0.03f + Mathf.Sin(_now * 1.35f) * 0.009f);
    _ipScale = 0.85f + 0.05f * Mathf.Sin(_now * 1.7f);
    _ipAlpha = Mathf.MoveTowards(_ipAlpha, 0.55f, dt * 0.8f);
    if (_sm.State == InteractionState.Guiding) _ipAnim = IpAnim.GuidingMove;
    break;
}
```

在 `UpdateIpAnim` 开头（`tgt` 计算后）：

```csharp
if (_attending && !_guideActive && !_celebrating &&
    _ipAnim != IpAnim.FeedbackPulse && _ipAnim != IpAnim.Peek && _ipAnim != IpAnim.Hidden)
    _ipAnim = _listening ? IpAnim.Listening : IpAnim.Attentive;
```

`IPWorldView`：

```csharp
public enum IPAnimState { ..., Search = 6, Attentive = 7, Listening = 8 }

bool guiding = visual == Guiding || visual == Search || visual == Found || visual == Leading;
animator.SetBool("Guiding", guiding);
animator.SetBool("Companion", visual == Companion);
// Attentive/Listening: 两 bool 都 false → IdleFly（CompanionIdle 的挥手被绕过）
```

`DriveExpression`: `Attentive → Smile`，`Listening → Curious`；
`DebounceAnim`: `Listening` 视为瞬时（讲话立即切换），`Attentive` 非瞬时；
`IpEffects.SetMood`: 两者都映射到 `Companion`；
`StickFigureIp.PoseFor`: 两者用现有柔和悬浮姿态（无手部硬摆）。

- [ ] **Step 4: 运行测试**

Expected: 模拟机断言通过；109+ 全量通过。

- [ ] **Step 5: 设备验证**

看着老师时不再挥手；短暂移开再回看，动作不闪烁。

- [ ] **Step 6: 用户手动提交**

---

### Task 5: ConversationGateLogic（纯逻辑）

**Files:**
- Create: `Assets/Scripts/Core/ConversationGateLogic.cs`
- Create: `Assets/Tests/EditMode/ConversationGateLogicTests.cs`

**Interfaces:**
- `ConversationGateLogic.Tick(float dt, bool vadSpeaking, bool selfAudioPlaying)`
- 属性：`VoiceAllowed`、`Listening`、`ExternalSpeech`、`SelfAudioSuppressing`。
- 参数：`resumeDelay=0.5f`、`selfAudioTail=0.30f`。

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void ExternalSpeech_BlocksAndListens()
{
    var g = new ConversationGateLogic(0.5f, 0.3f);
    g.Tick(0.1f, true, false);
    Assert.IsTrue(g.Listening);
    Assert.IsFalse(g.VoiceAllowed);
    Assert.IsTrue(g.ExternalSpeech);
}

[Test]
public void SilenceResumesAfterDelay()
{
    var g = new ConversationGateLogic(0.5f, 0.3f);
    g.Tick(0.1f, true, false);
    g.Tick(0.3f, false, false);
    Assert.IsFalse(g.VoiceAllowed, "静默不足 0.5s 不应放行");
    g.Tick(0.3f, false, false);
    Assert.IsTrue(g.VoiceAllowed);
    Assert.IsFalse(g.Listening);
}

[Test]
public void SelfAudio_IsNotExternalSpeech()
{
    var g = new ConversationGateLogic(0.5f, 0.3f);
    g.Tick(0.1f, true, true); // VAD 被自己的扬声器污染
    Assert.IsFalse(g.ExternalSpeech);
    Assert.IsFalse(g.Listening);
    Assert.IsFalse(g.VoiceAllowed);
    g.Tick(0.2f, true, false); // self tail 内仍忽略
    Assert.IsFalse(g.ExternalSpeech);
    g.Tick(0.2f, true, false);
    Assert.IsTrue(g.ExternalSpeech);
}
```

- [ ] **Step 2: 运行确认 RED**（类不存在）

- [ ] **Step 3: 实现**

```csharp
public class ConversationGateLogic
{
    public bool VoiceAllowed { get; private set; } = true;
    public bool Listening { get; private set; }
    public bool ExternalSpeech { get; private set; }
    public bool SelfAudioSuppressing { get; private set; }
    readonly float _resumeDelay, _selfTail;
    float _silenceT, _selfT;

    public ConversationGateLogic(float resumeDelay = 0.5f, float selfAudioTail = 0.30f)
    {
        _resumeDelay = Mathf.Max(0.05f, resumeDelay);
        _selfTail = Mathf.Max(0f, selfAudioTail);
    }

    public void Reset() { ... }

    public void Tick(float dt, bool vadSpeaking, bool selfAudioPlaying)
    {
        if (selfAudioPlaying) _selfT = _selfTail;
        else if (_selfT > 0f) _selfT = Mathf.Max(0f, _selfT - dt);
        SelfAudioSuppressing = selfAudioPlaying || _selfT > 0f;
        if (SelfAudioSuppressing)
        {
            ExternalSpeech = false; Listening = false;
            VoiceAllowed = false; _silenceT = 0f;
            return;
        }
        ExternalSpeech = vadSpeaking;
        Listening = vadSpeaking;
        if (vadSpeaking) { _silenceT = 0f; VoiceAllowed = false; }
        else { _silenceT += dt; VoiceAllowed = _silenceT >= _resumeDelay; }
    }
}
```

- [ ] **Step 4: 运行确认 GREEN（4 个测试）**

- [ ] **Step 5: 用户手动提交**

---

### Task 6: 语音门控集成 + 自音频抑制 + 引导降频【需用户确认】

**Files:**
- Modify: `Assets/Scripts/Core/M3DebugDriver.cs`
- Modify: `Assets/Scripts/Audio/VoiceCuePlayer.cs`
- Modify: `Assets/Scripts/Audio/AudioCuePlayer.cs`
- Modify: `Assets/Scripts/Core/GuideDirector.cs`
- Modify: `Assets/Scripts/Core/ExperienceSettings.cs`
- Modify: `Assets/Scripts/Editor/M3SimHarness.cs`
- Test: `Assets/Tests/EditMode/MeteorLogicTests.cs`（GuideDirector 冷却）

**Interfaces:**
- `VoiceCuePlayer.IsPlaying`、`SetGateAllowed(bool)`、`ClearQueue()`。
- `AudioCuePlayer.IsPlaying`。
- `GuideDirectorLogic` 构造增加 `retriggerCooldownSeconds=10f`；
  `GuideConfusedDelay` 默认 0.8 → 1.5s。
- 驱动新增 `PlayVoice(cue, priority)`：门控放行时 `voiceCue.Play` 并
  `Signal("Voice","Played")`；阻断时 `Signal("Voice","Blocked")`，Critical 排队。

- [ ] **Step 1: 写失败测试（Guide 冷却）**

```csharp
[Test]
public void Guide_DoesNotRetriggerDuringCooldown()
{
    var g = new GuideDirectorLogic(1.5f, 1.8f, 0.9f, 6f, 8f, 3, 1f, 2f, 15f, 10f);
    // 进入 Success（或 Silent）后回 Idle，10s 内即使不 attending 也不应再 Confused
    Step(g, 1.0f, true, true, false); // 不触发
    Step(g, 1.0f, false, true, false); // Confused
    // ...推进到 Success...
    var e = Step(g, 1f, false, true, false);
    Assert.AreEqual(GuideEvent.None, e, "冷却期内不应重新触发");
}
```

（具体 Step 见 `MeteorLogicTests` 现有辅助方法。）

- [ ] **Step 2: 运行确认失败**

- [ ] **Step 3: 实现 Guide 冷却 + 触发延迟**

`GuideDirectorLogic` 增加 `_retriggerCooldown/_cooldownT`；在 `EnterSuccess`/`EnterSilent`
时设置 `_cooldownT`；`Idle` 分支开头：

```csharp
if (_cooldownT > 0f) { _cooldownT = Mathf.Max(0f, _cooldownT - dt); return GuideEvent.None; }
```

`ExperienceSettings`：`guideConfusedDelay=1.5f`、`guideRetriggerCooldownSeconds=10f`。

- [ ] **Step 4: 实现语音门控**

`VoiceCuePlayer`：

```csharp
public bool IsPlaying => _source != null && _source.isPlaying;
bool _gateBlocked; VoiceCue _gatedCue; Vector3 _gatedPos; float _gatedAt; bool _hasGated;

public void SetGateAllowed(bool allowed)
{
    if (_gateBlocked && allowed && _hasGated && Time.realtimeSinceStartup - _gatedAt <= 2f)
    {
        var c = _gatedCue; var p = _gatedPos; _hasGated = false; _gateBlocked = false;
        Play(c, p, VoicePriority.Critical);
    }
    _gateBlocked = !allowed;
    if (!allowed) _hasGated = false;
}

public void Play(...)
{
    if (_gateBlocked)
    {
        if (priority == VoicePriority.Ambient) return;
        _gatedCue = cue; _gatedPos = worldPos; _gatedAt = Time.realtimeSinceStartup;
        _hasGated = true; return;
    }
    // 原逻辑
}
```

`AudioCuePlayer` 增加 `IsPlaying`（`_source != null && _source.isPlaying`）。

驱动：

```csharp
bool selfAudio = (voiceCue != null && voiceCue.IsPlaying) ||
                 (audioPlayer != null && audioPlayer.IsPlaying);
_conversationGate.Tick(dt, vadSpeaking, selfAudio);
_speaking = _conversationGate.ExternalSpeech;
voiceCue?.SetGateAllowed(_conversationGate.VoiceAllowed);
_listening = _attending && _conversationGate.Listening;
```

把 `voiceCue?.Play(...)` 调用统一替换为 `PlayVoice(...)`（Critical/Guide/Ambient
按现有优先级），以便信号与门控一致。

- [ ] **Step 5: 调整模拟机 I3 口径**

I3 从“看开视线后 1.4s 内有 Voice:Find 信号”改为：

- 看开视线且外界静默 → 1.4s 内 `Voice:Played`；
- 外界讲话期间 → 允许延迟到“讲话结束 + resumeDelay + 1.4s”；
- 新增断言：`speaking=true` 期间 `Voice:Played` 计数为 0。

（sim 的 `speaking` 就是外界讲话；`selfAudio` 为 false。）

- [ ] **Step 6: 跑全量 + 模拟机 3 种子**

Expected: 测试通过；17 分钟模拟的 `GuideConfused` 计数显著下降（目标 ≤20）。

- [ ] **Step 7: 用户手动提交**

---

### Task 7: 建模师 `Listen` 动作接入（阻塞项）

**Files:**
- Modify: `Assets/Art/IP/CH_Sprite.controller`
- Modify: `Assets/Scripts/UI/IPWorldView.cs`
- Modify: `Assets/Scripts/Core/M3DebugDriver.cs`
- External: 建模师 FBX `Listen` Action + 2–3s 预览

**Interfaces:**
- Animator 参数 `Listening`（bool）+ 状态 `Listen`（loop）。
- `IPWorldView` 在 `visual == Listening` 时 `animator.SetBool("Listening", true)`，
  其余状态 false；`Attentive` 仍走 IdleFly。

- [ ] **Step 1: 收到 `Listen` Action 后导入 FBX，确认 Action 名称/时长/循环**
- [ ] **Step 2: 在 Animator 控制器加 `Listen` 状态与 `Listening` 参数**
- [ ] **Step 3: `IPWorldView.DriveAnimator` 映射 `Listening`（替换 IdleFly 回退）**
- [ ] **Step 4: 真机验证手到耳、无 Root 位移、与 IdleFly 过渡自然**
- [ ] **Step 5: 用户手动提交**

**若建模师暂未交付**：Task 4 的 IdleFly+Curious 回退先上线；本任务保持阻塞，
不阻塞其它任务。

---

### Task 8: 真机验证与日志复核

**Files:** 无（验证任务）

- [ ] **Step 1: 构建并安装**

```bash
"D:/Development/Unity/Editor/Unity.exe" -batchmode \
 -projectPath "D:/Development/Unity/ARAttentionTrainer" \
 -executeMethod BuildScript.BuildDevice \
 -logFile "D:/Development/Unity/ARAttentionTrainer/Builds/build-v9.log" -quit
ADB="D:/Development/Unity/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
"$ADB" install -r "D:/Development/Unity/ARAttentionTrainer/Builds/ARAttention-device.apk"
```

- [ ] **Step 2: 手柄启动，测试以下场景**
  1. 看老师 20s：进度星环变大；无挥手；星路方向明显；
  2. 老师/旁人讲话：精灵不出声；讲话结束后语音恢复；
  3. 移开视线：引导触发不再 0.8s 这么急；语音不密集；
  4. 流星雨：celebrate 烟花出现；
  5. 老师侧后方：星路中心线/彗星指向老师。

- [ ] **Step 3: 拉取日志**

```bash
export MSYS_NO_PATHCONV=1
SESS="/sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/sessions"
LATEST=$("$ADB" shell "ls -dt $SESS/* | head -1" | tr -d '\r')
"$ADB" pull "$LATEST" "D:/Development/Unity/ARAttentionTrainer/Builds/device-logs/"
```

检查 `events.jsonl` 中的 `StarPath`/`VoiceGate`/`ListenState` 事件：
星路方向/宽度、讲话期间 `Voice:Played=0`、注视期状态不是 Companion。

- [ ] **Step 4: 记录结果到本 spec（追加“实测结果”一节）**

---

## 自检

- 问题 1 → Task 2；问题 2 → Task 3；问题 3 → Task 4；问题 4 → 明确不做；
  问题 5 → Task 3；问题 6 → Task 6；问题 7 → Task 5/6/7。
- 每个任务都有可执行测试或明确的预览/真机验收；Task 4/6 的行为需用户确认。
- 所有文件/接口名称与现有代码一致：`IPAnimState`、`IpAnim`、`VoicePriority`、
  `FlipbookLibrary.TryGet`、`MeteorSystem.ringStarSize`、`StardustPath.RoadHalfWidthFor`。
- 不包含“TBD/TODO”；Task 7 是明确的阻塞项（等建模师资产），不是占位符。

---

## 执行记录（2026-10-24）

- Task 1–5：完成。EditMode `117/117` 通过（新增 `ConversationGateLogicTests` 5 项、
  `FlipbookCelebrateTests` 1 项、`Guide_RetriggerCooldownAfterSuccess` 1 项）。
- Task 6：完成。
  - 模拟机 3 种子全部通过（20.2 分钟剧本）：`Voice:Played=199`、`Blocked=301`、
    **讲话中播放=0**。
  - `GuideConfused=49/20.2min`。注意：该模拟剧本每一轮都故意“讲话+不看”，
    触发密度远高于真实课堂；实际降噪效果以真机 17 分钟日志复核为准。
  - 为适配“成功后冷却 10s”，模拟机 I3 改为冷却感知（冷却期内不要求语音），
    G1' 离场段从 45s 延长到 68s（最坏链长 15s 静默 + 10s 冷却 + 28.1s 引导链）。
- Task 7：需求文档已写（`docs/specs/2026-10-24-listen-animation-requirements.md`），
  等建模师交付 `Listen` Action；当前 `IdleFly + Curious` 回退已生效。
- Task 8：构建/安装见本轮操作；真机测试结果待补。
