# 技术方案：流星引导（Meteor Guide）实现设计

> 版本：v1.0 草案（2026-10-20）｜ 配套交互方案：`docs/specs/2026-10-20-meteor-guide-interaction.md`
> 目标：在不推翻现有架构的前提下，扩展出"流星/星星/三段式引导"系统。本文档剖析
> **系统架构、模块改动、素材清单、技术支持、性能与风险、分阶段实施**。

---

## 一、系统架构（现有 → 新增）

### 1.1 现有可复用的技术资产（不重造轮子）

| 现有资产 | 位置 | 在新方案中的角色 |
|---|---|---|
| 老师空间锚点 | `IPWorldView._teacherPos / CurrentTeacherPos` | **方案地基**：老师的世界 3D 位置（丢失时冻结=定位不丢） |
| 方位提示映射 | `IPWorldView.TeacherViewportHint()` | 老师方位→视口方向（精灵航向、流星方向共用） |
| 老师追踪+滤波 | `TargetTrackerLogic`（OneEuro） | 平滑检测抖动 |
| 深度测距 | `EnvironmentRaycastManager` + 身高公式回退 | 老师锚点的真实距离 |
| 房间数据 | `MrukBootstrap`（保留！） | 精灵/特效不穿墙 |
| 精灵动画 | CH_Sprite：8 动作 + 5 表情 | 疑惑(歪头)/发现(Point)/带领(GuideFly)/成功(HappyFeedback)——**无需新做动画** |
| 表情泡泡 | `IpEmoteBubble`（Cheer/Hum/Curious + 新 flipbook 接口） | 话术泡泡 |
| 特效系统 | `IpEffects`（halo/ring/firefly/dust + Pulse） | 星光脉冲、光晕 |
| 语音 | `VoiceCuePlayer`（空间化，Resources/Voice） | 8 句新语音（同一套机制） |
| 音效合成 | `AudioCuePlayer.BuildTone`（零素材正弦合成） | 流星"咻"/星星"叮"/流星雨——**优先程序化合成** |
| 动效接口 | `FlipbookEffect / FlipbookLibrary`（10-20 已建） | 流星尾焰、星星闪烁的序列帧播放 |
| 参数中心 | `ExperienceSettings` | 全部新参数进这里 |
| 测试机 | `M3SimScenario / M3SimHarness` | 新交互的自动化验证 |
| 数据记录 | `SessionLogger` | 新增事件（引导轮次/星星/流星雨） |

### 1.2 新增模块（4 个）

```
M3DebugDriver（总指挥，现有）
   │
   ├─ MeteorSystem        【新】流星/星星/星环/流星雨 的世界空间表现与生命周期
   ├─ GuideDirector       【新】三段式引导导演（疑惑→发现→带领→成功/强化/静默）
   ├─ StarProgress        【新】星星进度（注视计时→落星→集满→流星雨）
   └─ GuideVoice          【新】话术系统（找老师/追星星两套 + 语音调度）
```

> 四个模块都是**纯逻辑 + 视图分离**：逻辑类可脱离 Unity 跑（测试机直接驱动），
> 视图类挂 `MeteorSystem` 子节点。与现有 `M3DebugDriver.sim` 测试机架构一致。

---

## 二、模块技术设计

### 2.1 MeteorSystem（流星系统）—— 最大的技术模块

**核心对象**
```csharp
class Meteor {
    Vector3 startWorld, endWorld;   // 起点=天际（老师锚点方向反向高处），终点=星环目标位
    float t, duration;               // 0.9~1.2s 弧线飞行
    Transform head;                  // 发光头（billboard quad）
    TrailRenderer trail;             // 拖尾
    int targetSlot;                  // 落点星环槽位（0..7）
}
class StarRing {                     // 星环（围绕老师的 8 个槽位）
    Vector3[] slots;                 // 世界坐标，随老师位置/头姿每帧更新（平滑）
    bool[] lit;                      // 已点亮
}
```

**轨迹与落点**
- 起点：老师锚点 + 上方 2.2m + 远离孩子的方向 1.2m（"从天际划来"）
- 终点：星环槽位（老师脸周半径 0.35m，8 槽按角度均布，面朝观察者）
- 路径：**二次贝塞尔**（起点→控制点(上方中段)→终点），t 用 easeInQuad，尾迹自然弯曲
- 落地：销毁流星 → 点亮槽位星星（出现动画：缩放弹入+闪烁）
- **世界空间**（不是屏幕贴片）——转头时流星保持世界真实方向，符合"定位不丢"

**未注视时的"指路流星"**
- 不是落到老师脸旁（不可见），而是沿 `TeacherViewportHint` 方向**飞出屏幕**
- 技术：终点 = 老师方位方向射线延长 30m（飞离视野即回收）
- 每 5~7s 一条（降频）

**流星雨**
- 一次性生成 8~12 条流星，起点散布在视野上方 120° 扇形，终点=老师周围随机点，1.5s 内错峰划完
- 星环爆闪：8 颗星同时放大+透明度爆闪 0.8s → 全部淡出 → 清空

**渲染与性能**
- 头：billboard quad + 发光材质（复用 `GuideArrow.shader`）
- 拖尾：`TrailRenderer`（宽度渐变+颜色渐变，生命周期=飞行时长）——比粒子便宜、可控
- **对象池**：流星实例池（最大 12）、星星 quad 固定 8 个——**运行时零 GC**
- 同屏上限：普通 3 条、流星雨 12 条（一次性）

### 2.2 StarProgress（星星进度）

- 输入：复用人脸居中判定（现有 `_faceHold` 逻辑）——**注视累计 ≥ 2.5s 触发一次"落星"**（每颗星=2.5s 注视，8 颗=20s；与现有人脸奖励的 20s 对齐）
- 星星只增不减：注视中断只停止落星，不清空
- 集满 8 颗 → 触发流星雨（回调 `MeteorSystem.PlayMeteorShower()` + 现有 `IPWorldView.Celebrate()` 跳舞 6s）
- 冷却 8s（沿用）
- 与现有 FaceHold 的关系：**替代**现有人脸奖励的"每 5s 星光脉冲"（每颗星 2.5s 的节奏更细腻）

### 2.3 GuideDirector（引导导演）—— 三段式状态机

```csharp
enum GuidePhase { Idle, Confused, Found, Leading, Success, Silent }
// 触发: 目标丢失≥0.8s 且 曾稳定锁定
// Confused 1.8s → Found 0.9s → Leading 6s(第3轮8s)
//   Leading 成功(老师入画+居中1s) → Success → 回 Idle（接 StarProgress）
//   Leading 失败 → round++（≤3）→ Confused（强化版参数）
//   3 轮后 → Silent（静默陪伴，等目标重获 or 超时 15s 回到可再触发）
```
- 输出到现有动画系统：`guidePhase → IPAnim 状态`（Guiding/Appearing/Companion…）+ 表情泡泡 + 语音
- 强化参数表（轮次→时长/语音间隔/特效强度）放 ExperienceSettings
- **与 M3InteractionStateMachine 的关系**：状态机继续管"引导/隐藏/反馈"的大状态；
  GuideDirector 是其**子流程**——当状态机处于"引导中（Guiding）且目标丢失"时由 GuideDirector 接管叙事。

### 2.4 GuideVoice（话术系统）

- 两套话术集：`FindTeacher`（找老师）/ `ChaseStars`（追星星）——切换规则见交互方案 B3
- 语音资源：`Assets/Resources/Voice/voice_*.mp3`（现有同机制，VoiceCuePlayer 枚举扩展 5→13 条）
- 调度：事件驱动（相位进入时播放一次 + 重复间隔）；空间音源=老师方位（保留现有 spatialBlend 0.6）

---

## 三、素材清单（按交付方分类）

### 3.1 美术素材（组员制作）

| # | 素材 | 规格 | 放置位置 | 用途 | 优先级 |
|---|---|---|---|---|---|
| 1 | `meteor_head` 流星头 | 256×256 单帧，发光星点+尾焰根部（透明，白/紫可染色） | `Assets/Art/Effects/` | 流星头部 quad | P0 |
| 2 | `meteor_trail` 尾焰序列帧（可选） | 8 帧 128×128，横向图集 | `Assets/Resources/Effects/flipbooks/meteor_trail.png` + index 条目 | 拖尾纹理（也可用渐变 TrailRenderer 替代，可不做） | P1 |
| 3 | `star_twinkle` 星星闪烁序列帧 | 8 帧 128×128，**白色**（代码染色金/紫），循环 | `Assets/Resources/Effects/flipbooks/star.png` | 落地星星的闪烁（动效接口已就绪，index.json 已有 `star` 条目） | P0 |
| 4 | `star_ring_glow` 星环轨道光（可选） | 512×512 单帧，极淡的外圈轨道 | `Assets/Art/Effects/` | 暗示星星将落在哪一圈 | P2 |
| 5 | `stardust_path` 地面星尘路径（**待确认**） | 序列帧 1024×256，柔和星尘带 | `Assets/Resources/Effects/flipbooks/stardust_path.png` | 未注视时地面指向老师的路径 | 待定 |
| 6 | 泡泡话术更新（可选） | 复用现有 bubble/face_cheer/face_hum | — | 有 flipbook 接口可升级动效 | P2 |

> 现有可直接复用：`star_soft.png`（星点）、`ring_soft.png`（光环）、`glow_soft.png`（柔光）、精灵 8 动画 + 5 表情。

### 3.2 音频素材

| # | 素材 | 制作方式 | 说明 |
|---|---|---|---|
| 1 | 8 句语音（清单见交互方案 §9） | 真人录音（推荐）或 AI 合成；同现有 mp3 机制 | `Assets/Resources/Voice/voice_meteor_*.mp3`，VoiceCue 枚举扩展 |
| 2 | 流星"咻" | **程序化合成**（`BuildTone`：高频下滑音 0.2s） | 零素材，先合成 |
| 3 | 星星"叮"（8 音阶上行） | 程序化合成（C 大调音阶，每颗星升一度；集满=琶音+和弦） | **设计亮点**：落星如音阶，集满奏和弦 |
| 4 | 流星雨爆发 | 程序化合成（琶音 + 低音铺垫 1.5s） | 零素材 |
| 5 | 精灵动作音（可选） | 现有音效复用 | — |

### 3.3 不需要新做的（复用）
精灵模型/动画/表情、泡泡贴图（静态）、光效贴图、现有 4 条语音。

---

## 四、技术支持与关键技术点

| 技术点 | 方案 | 风险 |
|---|---|---|
| 世界空间流星 | 贝塞尔轨迹 + billboard quad + TrailRenderer，全部世界坐标 | 低（标准渲染用法） |
| 星环跟随老师 | 每帧由 `_teacherPos + 观察者朝向` 计算 8 槽位（SmoothDamp 平滑） | 低（8 点计算量可忽略） |
| 对象池 | 流星池 12、星星固定 8、TrailRenderer 池化 | 低 |
| 动效（尾焰/星星闪烁） | 已有 `FlipbookEffect/Library` 接口（10-20 落地） | 低（素材到位即生效） |
| 语音扩展 | `VoiceCuePlayer` 枚举 + Resources 加载（现有机制） | 低 |
| 音效合成 | 复用 `AudioCuePlayer.BuildTone`（增补 3 个音型） | 低 |
| 注视→落星时序 | 复用现有 FaceHold（20s）改造为"8×2.5s" | 低（测试机可验证） |
| 引导导演与现有状态机整合 | GuideDirector 作为子流程，输出复用现有动画/泡泡/语音接口 | **中**——时序耦合需仔细；靠测试机断言覆盖 |
| 性能预算 | 新增对象：≤12 流星+8 星+若干 Trail；CPU 开销 ≈ 可忽略（纯渲染+简单轨迹）；不新增 AI 推理 | 低。真机验证：72fps 基线下帧率无损 |

**新增/修改代码文件预估**
```
Assets/Scripts/UI/MeteorSystem.cs          （新）流星/星环/流星雨 世界表现
Assets/Scripts/Core/GuideDirector.cs       （新）三段式引导导演（纯逻辑，可测）
Assets/Scripts/Core/StarProgress.cs        （新）星星进度（纯逻辑，可测）
Assets/Scripts/Core/MeteorLogic.cs         （新）流星轨迹/槽位计算（纯函数，可测）
Assets/Scripts/Audio/VoiceCuePlayer.cs     （改）枚举 5→13 条
Assets/Scripts/Audio/AudioCuePlayer.cs     （改）新增 3 个合成音型
Assets/Scripts/Core/M3DebugDriver.cs       （改）接线 4 个新模块
Assets/Scripts/Core/ExperienceSettings.cs  （改）新增参数（~12 项）
Assets/Scripts/UI/IPWorldView.cs           （改）暴露星环锚点/老师朝向
Assets/Scripts/Editor/DeviceSceneCreator.cs（改）场景接线
Assets/Scripts/Core/M3SimScenario.cs       （改）新剧情桥段
Assets/Scripts/Editor/M3SimHarness.cs      （改）新断言
Assets/Tests/EditMode/MeteorLogicTests.cs  （新）轨迹/槽位/进度单测
```

---

## 五、分阶段实施计划（建议）

| 阶段 | 内容 | 验证方式 |
|---|---|---|
| **P1 流星系统（视觉）** | MeteorSystem：流星生成/飞行/落地/星环/流星雨 + 对象池 | 真机看效果（先不接引导逻辑） + 单测（轨迹/槽位） |
| **P2 星星进度** | StarProgress：注视 2.5s/颗 → 8 颗 → 流星雨（接现有庆典） | 测试机：注视 20s → 8 星 → 流星雨断言 |
| **P3 引导导演** | GuideDirector 三段式 + 三轮强化 + 话术切换 | 测试机：丢失→三段时序、跟随/未跟随、三轮后静默 |
| **P4 声音与打磨** | 语音 8 句 + 合成音效 + 音量/节奏调优 | 真机体验 |
| **P5 全量回归** | 69+ 单测、模拟测试机多种子、真机验收 | 全绿 |

> 每阶段独立可验证、可回退；P1/P2 是纯视觉惊喜，P3 才改变现有引导逻辑（风险最高，放最后接线）。

---

## 六、与现有功能的取舍（明确变更点）

| 现有机制 | 处理 |
|---|---|
| 人脸居中 20s → 跳舞庆典 | **保留**，触发方式改为"集满 8 颗星"（同一 20s 计时，视觉由星光脉冲升级为流星/星环） |
| 每 5s 里程碑星光脉冲 | **移除**（被"每 2.5s 落一颗星"自然替代，反馈更细腻） |
| 方位提示（立体声+IP飞向） | **重构**为 GuideDirector 三段式；立体声降为辅助（保留音源方位） |
| 重获"找到啦"反馈 | 并入 A4（"找到老师了，真棒"） |
| Peek 探头 / Hum 哼歌泡泡 | **保留**（老师讲话期的陪伴行为） |
| 房间空间数据（MRUK） | **保留**（不穿墙 + 测距 + 锚点） |

---

## 七、待确认（技术侧）

1. 流星尾焰：用 **TrailRenderer 渐变**（零素材）还是序列帧（更华丽）？建议先 TrailRenderer，视觉不够再要素材。
2. 地面星尘路径（素材 #5）做不做？影响"指路"清晰度。
3. 语音：真人录音 or 继续 AI 合成？（edge-tts 目前被微软 403，需要别的 TTS 或真人）
4. P1 先做一版"纯视觉流星小样"给你看效果，再继续 P2/P3——同意这样分步吗？
