# AR 注意力引导系统 — 设计文档

> 项目：南京大学大创「面向孤独症儿童的社交融合系统 — AR眼镜注意力训练」
> 版本：v1.0（2026-10-07）｜ 结题目标：2026年12月
> 参与确认：本设计的交互逻辑、技术选型、工程安排均经项目负责人逐项确认

---

## 1. 项目概述

### 1.1 一句话定义
基于 Meta Quest 3 的 MR 应用：通过前置相机实时检测互动对象（教师/家长）人脸，用卡通 IP 精灵的「引导→隐藏→重现」循环，帮助高功能孤独症（ASD）儿童建立"别人说话时看对方"的注意力习惯。

### 1.2 使用场景
- **使用者**：高功能 ASD 儿童（佩戴 Quest 3）
- **互动对象**：教师/家长/康复师（与儿童面对面坐，距离 1-2 米）
- **单次训练**：10-20 分钟（与 Quest 3 热降频窗口吻合）
- **操作者**：治疗师/家长通过极简面板开始/结束训练、选干预档位

### 1.3 结题目标（2026年12月）
1. 可在 Quest 3 上运行的完整原型（核心循环全通）
2. 苏州特教学校实测数据（≥20名 ASD 儿童）
3. 结题报告 + 1-2 篇论文

### 1.4 关键约束
- 从零开发（此前无功能代码），团队 4 人，开发主力 1 人（每周约10小时）
- 所有依赖需在 GFW 环境下可获取（有代理；能离线的尽量离线）
- Quest 3 **无眼动追踪** → 注视判断必须用头姿代理（论文中如实说明）
- 所有素材/模型许可证必须干净（项目可能开源、发论文）

---

## 2. 系统架构

### 2.1 技术栈

| 层级 | 选型 | 版本 | 理由 |
|------|------|------|------|
| 引擎 | Unity | 6 (6000.4.3f1) | Sentis 2.5+ / 官方样例（6000.0.38+）都要求 Unity 6；2022.3 从 SDK 203 起不再支持 |
| MR SDK | Meta XR All-in-One | **207.0.0** | 最新稳定版（2026-09）；MRUK 205/207 修复 Unity 6.4 兼容；本地 201 已过期 |
| XR 插件 | Unity OpenXR plugin + Meta XR Feature | ≥1.15.1 | 官方推荐；`com.unity.xr.oculus` 已弃用 |
| 相机 | PassthroughCameraAccess (PCA，MRUK 包内) | 随 SDK 207 | 官方相机接口；自 SDK v76 公开，无审批 |
| 推理 | Unity Sentis (`com.unity.ai.inference`) | 2.6.1，**CPU (Burst) 后端** | Quest 无 NNAPI；官方样例用 CPU；GPU 有卡顿报告 |
| 人脸模型 | **YOLOv9 (WongKinYiu, MIT)** `yolov9sentis` | ~2.4MB | 满足"YOLO"叙事、MIT 许可、官方 PCA 样例同款已验证 |
| 备用模型 | BlazeFace (Apache-2.0) / YuNet (MIT) | — | 接口抽象留好，检测率不足时切换 |
| VAD | RMS 能量阈值 + 自适应噪声底 | 自研 ~20行 | 只需判断"在不在讲话"；升级路径 Silero VAD(~2MB ONNX) |
| 音频 | Unity 内置 AudioSource (spatialBlend=1) | — | MVP 零风险；升级路径 Meta XR Audio SDK |
| 空间定位 | EnvironmentRaycastManager + 世界坐标锚 | core SDK 内 | 官方推荐管线：射线取真实距离；锚点由 SLAM 维护 |

> ⚠️ 许可证记录：**禁用 AGPL 的 Ultralytics YOLOv8/v11/v12 权重**、禁用 SCRFD/RetinaFace 预训练权重（禁商用）。所有采用的模型/素材在入库时记录来源与许可证。

### 2.2 模块划分

```
Assets/Scripts/
  Core/          AppController(启动/权限/会话生命周期)、ExperienceSettings(全部可调参数)、SessionContext
  FrameSource/   IFrameSource + PcaFrameSource(真机) / VideoFileFrameSource(编辑器) / WebcamFrameSource(编辑器)
  Detection/     FaceDetector(Sentis封装)、DetectionPostprocess(NMS)、DetectionResult
  Tracking/      TargetTracker(3D定位+滤波+锚点维护)、OneEuroFilter、TeacherAnchor
  Gaze/          GazeJudge(头姿代理注视判断)
  Interaction/   InteractionStateMachine(核心状态机)、IPCharacterController(精灵动画)
  Vad/           VadService(麦克风采集+能量VAD)
  Audio/         AudioCuePlayer(空间音效)
  Logging/       SessionLogger(事件+采样记录)
  UI/            ConfigPanel(治疗师面板)、DebugHud(帧率/状态/延迟)
  Editor/        EditorTestHarness(视频文件注入)、BuildScript
```

**FrameSource 抽象是本设计的关键开发策略**：真机用 PCA 相机；电脑 Editor 里用视频文件（配一份 JSON 记录假位姿/内参）或 PC 摄像头。约 70% 的逻辑（状态机/后处理/日志）在电脑上即可调试，不用反复戴头显。

### 2.3 数据流

```
PCA相机帧 ─► FaceDetector(隔帧推理) ─► TargetTracker(深度射线+滤波) ─► 老师锚点(世界坐标)
麦克风 ───► VadService(讲话中?) ──┐                                      │
头姿 ─────► GazeJudge(在看老师?) ─┴─► InteractionStateMachine ─► IP精灵动画 + 空间音效
                                              │
                                       SessionLogger(事件+采样)
```

---

## 3. 核心交互设计（状态机）

### 3.1 状态定义与转换

```
待机 ──(讲话中 且 未注视)──► 引导                      [吸引模式：可无锚点]
引导 ──(孩子开始注视：头姿朝向达标 ≥0.8s)──► 隐藏
隐藏 ──(孩子又看别处 且 讲话继续；冷却≥3s)──► 引导     [温和重新引导]
隐藏 ──(注视持续累积 ≥2s 且 讲话结束)──► 反馈
反馈 ──(播放2s 后)──► 待机/引导                        [冷却≥2s]
任意状态 ──(老师长时间不说话≥10s / 摘头显 / 暂停)──► 待机
隐藏/反馈 ──(锚点失效)──► 待机
```

| 状态 | 进入条件 | 行为 | 退出条件 |
|------|---------|------|---------|
| **待机 Idle** | 初始 / 退出其它态 | 无引导；精灵消失 | 讲话中+未注视（锚点可选——还没检测到人也先引导"四处找"） |
| **引导 Guiding** | 讲话中 且 未注视 | 精灵渐入(0.5s)→出现在视野中央 →检测到人后跑动至老师头旁(约1.2s)；同步空间音效；**无锚点时留在中央吸引（吸引模式）** | 开始注视(≥0.8s) → 隐藏 |
| **隐藏 Hidden** | 开始注视 | 精灵渐隐(1s)并"躲"到老师头后；**注意力交付给人脸** | ①注视累积≥2s 且 讲话结束 → 反馈；②看别处且讲话继续 → 引导 |
| **反馈 Feedback** | 注视达成+讲话结束 | 精灵从老师头后随机方位(左/右/头顶)重现 + 音效 + 闪光，约2s | 2s 后回待机/引导 |
| **暂停 Paused** | 摘下头显/手动暂停 | 停止一切输出，记录暂停点 | 恢复 |

> 📌 **与中期答辩PPT的差异（答辩口径需同步更新）**：
> ① 隐藏触发从"人脸进入视野"改为"孩子开始注视"（更符合"隐藏=交付注意力"的逻辑）；
> ② 注视判断为头姿代理（无眼动追踪），论文中需说明该近似及其局限。
>
> 📌 **2026-10-10 修正（对齐最终版 PPT 三阶段设计 + 用户实测反馈）**：引导入口**放开"锚点有效"要求**（吸引模式）——孩子没看人/还没找到人时精灵先出现在视野中央"带孩子四处找"，检测到人后再跑向人；仅隐藏/反馈在锚点失效时回待机（此前"必须先锁目标才能引导"会把孩子扭开头时的引导直接掐死，实测确认错过引导时机）。

### 3.2 判断量的精确定义（全部参数在 ExperienceSettings 可调）

| 量 | 定义 | 默认初值 |
|----|------|---------|
| 讲话中 speaking | VAD：RMS > 噪声底+8dB 进入，低于+4dB 退出（滞回）；最短语音段 0.5s；≤0.4s 停顿视为同段 | — |
| 注视 looking | 头显前向与"老师锚点方向"的水平夹角 ≤ 20°（垂直放宽） | 20° |
| 开始注视 gazeStart | looking 连续累计 ≥ 0.8s | 0.8s |
| 注视达成 gazeSustained | 隐藏期间 looking 累计 ≥ 2.0s（离开时缓慢衰减，不硬清零） | 2.0s |
| 讲话段 speechSegment | SpeechStarted→SpeechEnded 为一个段；"讲话结束"=本段结束 | — |
| 引导冷却 | 两次引导出现间隔 | ≥3s |
| 反馈冷却 | 反馈结束到下次可引导 | ≥2s |

### 3.3 干预档位（强/中/弱，MVP 手动选档）

| 参数 | 强 | 中 | 弱 |
|------|----|----|----|
| 精灵尺寸 | 大 | 中 | 小 |
| 出现音效 | 有（明显） | 有（轻） | 无/极轻 |
| 反馈特效 | 闪光+音效+动画 | 轻度特效 | 仅轻微音效 |
| gazeStart 阈值 | 0.5s（一见注视就交付） | 0.8s | 1.2s（更扎实的注视才交付） |
| 引导冷却 | 3s | 5s | 8s |

> 注：弱档设计为"提示更少、要求更高、频率更低"，具体数值在 M4 联调时按体验调整；"根据表现自动降档"为结题后增强项（依赖 M5 数据）。
>
> **实现（2026-10-10）**：档位表固化在 `ExperienceSettings.ApplyLevel`；真机**手柄 A/X 键**、电脑**键盘 1/2/3** 循环切档；切换即时生效（GazeLogic 起始阈值 / 状态机重导冷却 / 音效音量 / 精灵尺寸），数据面板显示当前档位，并记 `LevelChange` 事件。

---

### 3.4 隐藏期微提示（Peek）（2026-10-08 用户提出）

问题：教师单段讲话过长时，孩子在"隐藏"（已注视）期间容易丧失兴趣。
设计：Hidden 状态下若讲话仍在继续，系统每隔 `peekIntervalSeconds`（默认10s，档位联动
预留：强8s / 中12s / 弱20s）让 IP 精灵从老师头部左/右侧做一次**短暂探头**（约0.8s：
0.2s淡入 → 轻微摆动 → 0.25s淡出），然后继续隐藏。作用是"注意力维持脉冲"——不打断注视，
只在视线边缘轻轻提一下兴趣。参数：`peekIntervalSeconds` / `peekDurationSeconds`（已入
ExperienceSettings）。实现位置：M3DebugDriver 的 IP 动画层（不改状态机）。

---

## 4. 关键算法管线

### 4.1 相机帧获取（PCA）
- `PassthroughCameraAccess` 组件（MRUK）：`GetTexture()` 拿 GPU 纹理；`GetCameraPose()` 拿位姿——**必须在取帧同时缓存位姿**（异步推理期间头部会转动，用当前位姿会漂移）
- 分辨率选 1280×960（v74+ 上限；更高需 Horizon OS v83）；~30fps，端到端延迟 40-60ms
- 左右相机均可访问；MVP 用单侧（默认右/实验确定哪侧更接近儿童视角中心）

### 4.1.1 真机取帧与权限（2026-10-10 落地）

**帧源抽象**：`FrameSourceBehaviour` 统一接口——电脑端 `VideoFileFrameSource`（视频文件）、真机 `PcaFrameSource`（包装 MRUK 的 `PassthroughCameraAccess`）。上层（检测/跟踪/驱动）完全不感知差异，同一套 M3 逻辑两边跑。

**权限链路（源码级验证）**：
- manifest 权限 `horizonos.permission.HEADSET_CAMERA` **无需手写**——构建时 `OVRManifestPreprocessor` 依据 `OculusProjectConfig` 两个开关自动生成：`insightPassthroughSupport != None` + `isPassthroughCameraAccessEnabled == true`（`MRProjectSetup.Apply()` 负责设置）
- 运行时弹窗走 `OVRPermissionsRequester.Request(PassthroughCameraAccess)`（`PermissionFlow` 组件，轮询 60s）——**M1 只请求相机权限**（Scene 权限等用到 MRUK 锚点再加，少一个弹窗→少一次误点"拒绝"的机会）
- `PassthroughCameraAccess` **自带等待**：无权限时 `OnEnable` 起协程逐帧检查，授权后自动开播，无需外部开关组件
- ⚠️ 权限弹窗若"全部拒绝"，应用不可恢复（须卸载重装）——正式版 UI 需大字提示

**XR 路线（SDK v207 的 Required 级要求）**：Unity OpenXR 1.18.0（v74+ 官方弃用 OculusXR 插件路线）＋ Android 端 OpenXR Loader ＋ 两个功能：**Meta XR Feature**（`com.meta.openxr.feature.metaxr`，SDK 提供 OVRPlugin-over-OpenXR）与 **Meta Quest Support**（`com.unity.openxr.feature.metaquest`，Unity 提供 Quest 构建配置）。批量配置由 `XRRuntimeSetup.Apply()` 完成（对应 `OVRProjectSetupXRTasks` 工具规则）。

**透视显示**：运行时 `OVRManager.isInsightPassthroughEnabled = true` + 中心眼相机 `clearFlags=SolidColor、backgroundColor α=0`（透明背景→透视透出）。ARM64 架构为透视硬性要求。

**真机场景 Device.unity**（`DeviceSceneCreator` 生成）：OVRCameraRig（含上述透视配置）＋ PassthroughCamera 帧源 ＋ 权限流 ＋ 检测/VAD/M3 驱动（键盘模拟关闭、麦克风 VAD 开启）＋ 右下"监视器"浮窗（PCA `TargetMaterial` 自动刷相机纹理，供 M1 验收与调试）。

**M1 首测踩坑记录（2026-10-10 实测，均已自动修复）**：
- **URP HDR 必须关闭**：HDR 开启时眼缓冲为 R10G10B10A2（无有效 alpha），透明背景失效 → 透视被不透明黑覆盖。`MRProjectSetup` 已自动关 HDR（官方 URP 透视要求清单：HDR 关 / Post-processing 关 / Intermediate Texture=Auto）
- **URP Unlit 的纹理属性是 `_BaseMap`**：PCA 默认写 `_MainTex` 在 URP 中不生效 → 监视器面板纯白。`DeviceSceneCreator` 已设 `TexturePropertyName = "_BaseMap"`
- **必须显式提交透视图层（M1 最终修复项，最关键）**：仅有"透明背景 + isInsightPassthroughEnabled"不够——合成器日志 `PT is: ON numLayers: 0`（应用没有提交任何透视图层）时系统会把透视渲染整个熄掉（`renderIteration: PT is: OFF`）→ 眼缓冲透明区露出的是虚无（黑）。修复：场景中挂 **`OVRPassthroughLayer`** 组件（向合成器提交 Underlay 层），M1 随即验收通过
- 排查依据：`adb logcat` 里 `[Insight MR] Successfully initialized passthrough`（初始化成功）+ `MRUK Shared: Camera started`（相机在流）+ 采样日志 `targetValid=1`（检测链路全通）——三链路各自独立验证，把问题精确定位到"渲染合成"层
- **PCA `ViewportPointToRay` 的视口约定是"左上原点"**（与检测框归一化坐标一致）——**不要做 Unity 式 y 翻转**，翻转会让射线上下颠倒、精灵/检测框全部画到人脚下方向（2026-10-10 由 `[IP]` 日志实锤：目标在画面上方却算出 -20° 向下的射线）。修复：直接传归一化坐标
- **语音指导（2026-10-10 新增）**：Quest 无系统 TTS 引擎（实测 `tts_default_synth`=null）→ 用 edge-tts 预生成语音随包发布：引导"看这里呀"/吸引模式"看看老师在哪儿呀"/反馈"太棒啦"/探头"我在这儿"；`VoiceCuePlayer` 支持空间定位（声音从精灵方位传来）与档位音量联动；**后续真人录音同名替换文件即可，代码零改动**
- **空间数据（MRUK，2026-10-10 新增，用户建议）**：`PermissionFlow` 加回 **Scene 权限**；`MrukBootstrap` 启动时 `LoadSceneFromDevice`（已存数据秒载；无数据触发系统空间设置流程），记录房间数 / 地板高度 / 锚点数——为"MRUK 环境射线实测距离"（§4.3 主路径）与"3D 精灵房间锚定定位"铺垫。前提：头显完成过一次空间设置
- **人体轮廓分割（2026-10-10 新增，用户定调）**：MediaPipe Selfie Segmentation（462KB）——**实测推理 27–42ms**（远快于预算）；关键坑：模型需要 [-1,1] 输入而 Unity 管线给 [0,1] → 已向 ONNX **注入 `2x-1` 归一化节点**（零运行时改动）。`SelfieSegRunner`（0.5s 节流）+ 轮廓边缘提取 → 陪伴模式精灵贴真实轮廓；**"精灵被真人遮挡"效果已具备速度条件（下一步）**
- **A·遮挡"躲到人后"（2026-10-10 深夜，用户定调 ABC 全量）**：`MaskOccluder` 全屏擦除板——解析计算 屏幕UV↔PCA掩码UV 的 3×3 单应（两次针孔投影复合，`ComputeRows` 已被 3 项单测锁死；推导中修正两处符号/比例错误）；着色器 `ARAttention/MaskErase` 以 `Blend Zero OneMinusSrcAlpha` 在人体轮廓内把已绘制的精灵/画线擦除。已知取舍：屏幕空间不辨深度——数据面板若被真人"挡在后面"也会被擦（v1 接受，后续可按面板区域豁免）
- **B·环境射线实测距离（同夜）**：`EnvironmentRaycastManager` 射线（MRUK 深度）优先、身高公式回退——空间数据就绪后自动启用，框/精灵距离更准
- **C·3D 接入框架（同夜）**：`IPWorldView.model3D + animator`（Animator 参数名=需求文档动画名：`Flying/Companion/Appear/Feedback/Peek`；无 Animator 时程序化起伏）；场景内置"胶囊+双眼"3D 占位体先验证**定位/面朝用户/动画状态转发**——组员正式模型到位后替换一个引用即接管

### 4.2 人脸检测（Sentis + YOLOv9）
- 预处理照官方样例：`TextureConverter.ToTensor(tex, tensor, new TextureTransform().SetDimensions(w,h,3))`（GPU侧）→ `worker.Schedule` → `((Tensor<float>)worker.PeekOutput(0)).ReadbackAndCloneAsync()`（**禁用阻塞版 ReadbackAndClone**）
- **启动时 `PreloadModel()`**（2×2 dummy 推理）——首次推理会阻塞数秒编译 compute shader
- 节流：每 2-3 帧检测一次 + 框保持；张量只分配一次
- NMS：少量候选框用 CPU 后处理即可（备选 Sentis Functional.NMS）
- 输出：bbox（归一化）+ 置信度；置信阈值 ~0.5（可调）

### 4.2.1 检测能力演进（2026-10-08 记录，来自首次视频验证反馈）

当前模型是 COCO 通用 YOLOv9（官方样例同款），输出**人框**（含全身）而非纯人脸框。演进路线：

1. **M3 起步（零成本，优先做）**：用"人框的上部中心"作为头部/面部位置的启发式——取框顶部往下约 25% 高度的水平中心点。引导目标、躲藏位、方向锚全部使用该点。
2. **增强步骤（M3 之后视时间）**：替换为**人脸专用模型**（首选 BlazeFace——Unity 官方 Sentis 示例自带；备选 YuNet/MIT）。架构已为模型替换做好准备：`DetectionResult` 与具体模型解耦，仅需新增一个解析分支 + 换模型资产。
3. **实例分割（2026-10-10 用户定调为最终目标之一）**：人/脸轮廓分割，让虚拟精灵与真实人脸"贴合"（沿轮廓探头、躲藏、轮廓发光等）。首选 **YOLOv9-seg**（与现检测器同族，Sentis 兼容性好、接口改动最小）。下游演进：`DetectionResult` 增加**可选 mask 字段**（默认无，现有管线不受影响）；`IPWorldView` 后续按轮廓计算遮挡与探头位。
   > **实现（2026-10-10 晚）**：改选 **MediaPipe Selfie Segmentation**（onnx-community 版，462KB；ONNX 输入 1×3×144×256 → 输出 1×1×144×256 alpha 掩码；Apache-2.0）。原因：YOLOv9-c-seg（27M 参数）在 Quest CPU 预计 0.5–1s/帧，且其公开导出版含 TensorRT 插件节点、Sentis 无法导入；**1对1训练场景下"整体人体轮廓"与实例分割效果等效、快约 20 倍**。管线：`SelfieSegRunner`（0.5s 节流推理 + 掩码）→ `TryGetEdgeStatic`（轮廓边缘提取，4 项单测覆盖）→ `IPWorldView` 陪伴模式沿真实轮廓边缘贴合。**最终形态**：掩码进渲染管线做深度遮罩（精灵被真人部分遮挡的"躲到人后"效果）——待真机测速后推进。
4. **"识别特定的人"（结题增强 / 论文展望，不入 MVP）**：三条路线——①空间锁定（1对1对坐场景 = 最大最居中 + 时序滞回，M3 已规划）；②外观记忆（颜色特征找回，行为上近似"认识"）；③真实人脸识别（embedding + 预录入照片；涉及儿童隐私与伦理，列入 Future Work）。

### 4.3 人脸 3D 位置估计（"空间粗定位 + 视觉精定位"）
1. **主路径**：`ViewportPointToRay(bboxCenter, cachedPose)` → `EnvironmentRaycastManager.Raycast` → 命中点即真实米制距离 → 位置 = pose.position + dir × dist
2. **兜底**：尺度估计 `dist = f_px × 0.15m / bbox宽px`（人脸宽按 150mm；后续可升级 IPD 估计）
3. **滤波**：One-Euro 滤波（位置+距离），平滑且低延迟
4. **锚点维护**：`Tracked`（持续更新）→ 丢失 <1s 保持不切换 → 丢失 >1s 锚点冻结（**世界坐标由 SLAM 锁定，视野外依然有效**——"人脸可丢，方向不丢"）→ 重新捕获用最近邻匹配恢复
5. **多人处理**：取置信度×尺寸最大的脸为目标；目标切换加滞回（新目标需更"明显"才切），避免来回跳

### 4.4 VAD（轻声段检测）
- `Microphone.Start(null, false, 60, 16000)`——16kHz 单声道；**先 `RequestUserPermission` 再启动**（Quest 已知坑：授权后首次运行可能无音频，对策：加载场景预请求权限、授权完成再启动采集）
- 起始 2 秒静音校准噪声底；运行时对噪声底做慢跟踪（适应教室环境）
- 滞回阈值 + 最短语音段 + 停顿桥接（见 3.2 表）
- 麦克风不可用时：**降级为"始终引导"模式**（speaking 恒真）——此时"讲话结束"条件失效，改用替代规则：**注视持续满 5s 即进入反馈**（该替代规则同样写入 ExperienceSettings 可调）
  > **实现（2026-10-10）**：`M3DebugDriver` 检测到麦克风不可用即进降级；注视累计满 `micFallbackFeedbackGazeSeconds`（默认 5s）合成一次"讲话结束"边沿触发反馈（每轮 Hidden 一次）；记 `MicFallback` 事件，数据可追溯使用了降级规则。

---

## 5. 数据记录设计（SessionLogger）

**存储**：`Application.persistentDataPath/sessions/<yyyyMMdd_HHmmss>/`，JSONL(事件) + CSV(采样) + session.json(元数据)。**不存任何图像/音频帧**（隐私 + Meta 数据政策要求）。

| 级别 | 字段 |
|------|------|
| 会话级 | sessionId、开始/结束时间戳、时长、干预档位、儿童昵称、IP 昵称、设备/版本 |
| 事件级 | 引导出现（触发原因、老师距离/方位）、开始注视、注视达成、反馈发出、讲话段起止、**反应时间**（引导出现→开始注视的间隔）、目标切换/丢失 |
| 采样级（每2秒） | looking(bool)、speaking(bool)、anchorValid(bool)、距离(m)、FPS、检测延迟(ms) |

> **实现（2026-10-10）**：session.json 增加 `version`（应用版本）/ `device`（设备型号）/ `settings`（完整参数快照——实验可复现，含档位与各项阈值）。PC 侧工具（`tools/`）：`pull_sessions.sh` 一键拉取设备数据到 `data/`（已 gitignore）；`analyze_sessions.py` 生成 summary.md/csv + 每会话行为/状态/性能图（含合成数据自测）。

**导出**：MQDH 文件浏览器或 `adb pull` 拉到电脑 → `tools/analytics/` 下 Python 脚本聚合（专注时长占比、平均反应时间、引导次数趋势、各档位对比）→ 出图（结题报告/论文直接用）。

---

## 6. 边界情况与降级策略

| 情况 | 处理 |
|------|------|
| 相机权限被拒 | 显示引导页（说明如何授权；**全拒绝后不可恢复，只能重装**——UI 里提示） |
| 麦克风权限被拒/失败 | 降级"始终引导"模式，日志标记降级运行 |
| 检测不到人脸 >3s | 回待机，精灵消失（不空引导） |
| 脸短暂丢失 <1s | 保持锚点，不触发状态跳变（防闪烁） |
| 多人入镜 | 最大+最居中优先；滞回切换；多人时段记入日志（供分析） |
| 孩子完全背对老师 | 方向锚 + 精灵在视野边缘方向跑动提示 |
| 摘下头显/系统暂停 | 自动 Paused；恢复时重新校准 |
| 过热降频 FPS<12 持续5s | 自动加大检测间隔（保稳定），日志记录降频时段 |
| PCA 延迟导致框漂移 | 位姿缓存 + One-Euro 滤波 + 框外推（可选） |

---

## 7. 测试策略

| 层 | 环境 | 内容 | 频率 |
|----|------|------|------|
| 逻辑层 | Unity Editor（视频文件帧源） | 状态机、后处理、日志、参数调优 | 日常开发 ~70% |
| 真机快测 | Quest 3 (Build&Run + adb logcat) | 相机权限流、PCA帧率、Sentis性能、麦克风 | 每个小模块 |
| 场景测试 | 两人对坐（1-2m，室内） | 完整循环体感、音效空间感、舒适度 | 每个里程碑 |
| 现场实测 | 苏州特教学校 | 真实儿童、数据采集 | M6（已约时间） |

> 📌 键盘模拟（K/L）只是**机制验证**：电脑上相机固定，"人脸是否在画面中心"不能代表孩子视线。
> **真实注视验收以 POV 对话视频（相机随头转动，几何代理成立）与真机头姿为准**（2026-10-08 确认）。
> 真机前关键闸门：Meta XR SDK 207 获取通道（节点恢复或 Asset Store 路线）。

**性能预算**（来自调研）：相机→张量 2-3ms；推理 12-18ms；GPU→CPU 回读 6-10ms；检测隔帧执行目标净帧率 ≥30（渲染）/ ≥10（检测）。**M2 起每次真机测试记录实测数字**（结题材料需要）。

---

## 8. 里程碑与验收标准

| # | 时间 | 内容 | 验收标准（可演示/可测量） |
|---|------|------|--------------------------|
| M1 | 10/08-10/18 | 环境搭建：Unity6.4补Android模块、SDK207(含MRUK)下载、新工程+OpenXR配置、PCA相机画面叠加、权限流、FPS显示 | 戴上头显看到相机画面叠加真实环境，帧率显示正常 |
| M2 | 10/18-10/28 | FrameSource抽象、Sentis+YOLOv9集成、检测框叠加渲染、性能实测 | 人脸被框住实时跟随；记录实测推理/回读耗时与帧率 |
| M3 | 10/28-11/08 | TargetTracker(3D定位+锚点+滤波)、GazeJudge、状态机v1、空间音效、占位精灵全部动作、SessionLogger v1 | 两人对坐演示完整循环（引导→注视→隐藏→反馈）；日志文件可导出可读 |
| M4 | 11/08-11/18 | VadService、三档干预参数、ConfigPanel、DebugHud完善 | 只在讲话时触发；三档差异可感知可切换 |
| M5 | 11/18-11/28 | 数据导出+统计脚本、稳定性(长时运行/过热/重进)、边界处理逐项过 | 一次10-20分钟会话完整跑完，数据导出出图 |
| M6 | 11/28-12/20 | 现场实测（苏州）、按数据调参、结题报告+PPT+论文 | 学校测试完成、数据报告、结题材料齐 |

**预留缓冲**：M6 含约1周缓冲；若前序延期，优先砍弱档细节而非核心循环。

> 📌 **路线权威（2026-10-10 用户确认）**：对外路线以最终版中期答辩 PPT `D:\大创\中期答辩\revise1.1(1)(1).pptx`（第 13 页）为准——改进视觉引导（6月）→ 语音识别模块·检测说话状态（7月）→ 数据收集系统（8月）→ 集成+实地测试（9-11月）→ 结题（12月）；结题交付 = 完整系统 + ≥20 名 ASD 儿童测试数据 + 1-2 篇论文 + 可运行原型。本表 M1-M6 是内部执行分解，对外口径用 PPT 说法。
> 📌 **进度（2026-10-10）**：M1 ✅ 真机透视验收；M2 ✅ 检测真机 190ms；M3 大部分 ✅（状态机/VAD/日志双端验证）——**当前执行 M3 收尾 = 世界空间 IP（官方路线"改进视觉引导"的核心）**。
> 📌 **实例分割（2026-10-10 用户定调）**：最终要做实例分割，让虚拟精灵与真实人脸的交互更贴合（沿脸轮廓探头/躲藏等）。技术候选：**YOLOv9-seg**（与现检测器同族、接口改动最小）> 其他方案待评估；下游为 `DetectionResult` 预留可选 mask 字段。列为视觉引导增强项。

---

## 9. 风险与应对

| 风险 | 等级 | 应对 |
|------|------|------|
| PCA/推理性能不达标 | 高 | 隔帧检测+分辨率调节+模型切换接口(BlazeFace/YuNet)；M2 就定性 |
| SDK 207 与 Unity 6.4 兼容问题 | 中 | 采用官方样例验证过的组合；样例仓库版本为准 |
| 麦克风首跑 bug / 权限坑 | 中 | 加载场景预请求；M1 就真机测麦克风（Day1 验证项） |
| 检测率在真实教室光线/距离下不足 | 中 | 兜底模型切换接口；实地测试前做室内多光照自测 |
| ASD 儿童实际接受度 | 高 | 渐进引入（强→弱）、可随时摘除；实测收集反馈快速调参 |
| 时间风险（10周从零到原型） | 高 | 范围锁定：不做 ASR、不做自动档位、不做 3D 精灵（见下） |
| 网络（GFW） | 低 | 有代理；所有依赖包本地缓存归档（tools/packages/） |
| 许可证风险 | 已控 | 已避开 AGPL/非商用权重；入库记录许可证 |

**明确不做（MVP 范围外）**：全句语音识别(ASR)、课本内容联动（旧方向，已废弃）、干预档位自动切换、IP 3D 模型、眼动追踪、多儿童同时、联网功能。

---

## 10. IP 素材需求（给组员）简版

- 形式：**2D 精灵序列帧**，透明背景 PNG；始终面向儿童（billboard）
- 状态清单：`idle_float`(待机漂浮,循环) / `point_run`(朝老师方向跑动) / `fade`(渐隐,可用透明度实现) / `hide_peek`(躲藏) / `pop_L`,`pop_R`,`pop_Top`(随机探头) / `celebrate`(反馈闪光)
- 尺寸建议：单帧 ≤512×512；风格与 PPT 形象一致（紫色星星小精灵）
- 命名规范：`ip_<状态>_<序号>.png`；放 `Assets/Art/IP/`
- 儿童可对 IP **自主命名**（配置面板填写，日志记录；MVP 不做语音呼叫）

---

## 11. 附录

- 调研报告存档（详细证据与链接）：
  - `D:\大创\CCAssistant\开发调研\2026-10-07_调研A_MetaSDK与相机接口.md`
  - `D:\大创\CCAssistant\开发调研\2026-10-07_调研B_端侧人脸检测与VAD.md`
  - `D:\大创\CCAssistant\开发调研\2026-10-07_调研C_空间锚定与音频.md`
- 官方蓝本：https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples
- 关键 API：`PassthroughCameraAccess.GetTexture()/GetCameraPose()/ViewportPointToRay()`；`OVRPermissionsRequester.Request()`；`EnvironmentRaycastManager.Raycast()`；`TextureConverter.ToTensor()`

---

## 12. 2026-10-11 迭代记录：火柴人占位 + 移动逻辑平滑（用户定调）

**背景（用户真机反馈）**：ABC 验收后反馈——"抖动、小人动作太快会闪现、没有遮挡效果"；拍板：**遮挡难做就不要了**；目标对齐 Horizon 商店精品 MR（参考 Meta《First Encounters》）："模型位置相对稳定（空间定位）+ 移动逻辑合理"；要求先搭**火柴人**作为给组员建模预留的接口，**重点做移动逻辑的平滑**。

### 12.1 抖动/闪现的根因与修复
| 根因 | 修复 |
|---|---|
| 世界位置在检测批次（~5Hz）离散重建 → 瞬跳 | `IPWorldView` 增加**连续追赶**：渲染位置每帧 `SmoothDamp`（tc=0.17s，限速 2.5m/s）趋近锚点；>1.2m 视为重生直接吸附 |
| 目标短暂丢失 → 目标点回退屏幕中央（闪回中间再飞回） | `TargetOrCenter` 目标保持：丢失 <2.5s 保持最后位置 |
| 陪伴换边左右瞬移 | `_companionSideSmooth` 平滑滑过人脸（tc=0.45s） |
| 反馈/探头从当前位置瞬移 | 改为 0.35s/0.3s smoothstep **飞入**（从当前位到目标位） |
| 检测框逐帧抖动 | 视口层 `SmoothTarget`（tc=0.15s）+ 世界层追赶，双道平滑 |
| 姿态/透明度瞬变 | `StickFigureIp` 关节角度 SmoothDamp 混合（tc=0.18s）；透明度=材质 alpha 平滑（不 SetActive 闪现）；引导↔companion 状态 0.18s 去抖 |
| 动作频率过快（脉冲 10rad/s 等） | 全面降频：起伏 0.2~0.6Hz，反馈脉冲 4.5rad/s |

### 12.2 空间定位感（房间围栏）
- `MrukBootstrap.TryGetRoomBounds()`（世界坐标，含地板/天花板）→ `IPWorldView.ClampToRoom`（纯函数，单测锁死）：x/z 留 0.35m 边距，y∈[地板+0.45, 天花板−0.35]；**锚定规则**：目标有效=跟随；丢失=冻结（不随头转漂移）；从未锁过目标=吸引模式跟随；陪伴=贴轮廓。

### 12.3 火柴人接口（IP_Model3D）
- `StickFigureIp.cs`：运行时程序化搭建（头/躯干/双臂/双腿 + 双眼），原生身高 1m；各状态姿态语言（飞行前倾/陪伴招手/登场举手/反馈欢呼/探头侧倾）；作为组员 FBX 的**接口对照**。
- `IPWorldView.modelNativeHeight`：模型按 `baseWorldSize/modelNativeHeight` 归一缩放——FBX 按真实米制建模，无需改源文件尺寸。

### 12.4 遮挡（A 项）状态
- 用户定调"难做就不要了" → `DeviceSceneCreator.EnableOcclusion=false`（默认不生成 MaskOccluder 节点）；`MaskOccluder.cs`/`MaskErase.shader`/单测保留，随时可开关回来。
