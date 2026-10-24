# ARAttentionTrainer — AR 注意力引导系统

> 南京大学大创项目「面向孤独症儿童的社交融合系统」核心软件
> 结题目标：2026 年 12 月｜目标平台：Meta Quest 3

## 这是什么

一个跑在 **Meta Quest 3 混合现实眼镜**上的训练应用。

孩子戴上眼镜上课时，系统用眼镜前置摄像头实时看着周围的大人（老师/家长），
并有一个**卡通小精灵**在孩子的真实视野里飞来飞去「陪着」他：

- 孩子**没看老师** → 小精灵飞向老师的方向，语音从老师那边传来（「看看老师在哪儿呀」），
  地面同时亮起一条**星尘路**（中心线 + 指路彗星，指向老师）；
- 孩子**看向老师** → 小精灵安静下来（`IdleFly` 陪伴，**不挥手**），老师头周开始积累**进度星**；
- 孩子**持续看着老师** → 每 2.5 秒落一颗星，集满 8 颗触发**流星雨 + 烟花庆典**；
- **老师/旁边人讲话** → 小精灵进入**倾听态**（最终版为扶耳动作），**完全不插话**；
  讲话结束约 0.5 秒后才恢复语音；
- 全程**小精灵绝不消失**——它像一个小伙伴，用"引导 → 陪伴 → 奖励"的循环，
  帮孩子一点点养成"别人说话时看对方"的习惯。

这套思路参考了 MIT 背景的 Empowered Brain（Face2Face）项目，并针对课堂场景做了改进。

## 技术全景（一张图读懂）

```
              ┌──────────────── 眼镜前置摄像头 ────────────────┐
              │                                               │
        ┌─────▼─────┐   ┌──────────┐   ┌──────────────┐
        │ YOLOv9    │   │ BlazeFace│   │ SelfieSeg    │   三个 AI 模型
        │ 检测"人"  │   │ 检测"脸" │   │ 分割人体轮廓 │   （各自独立节奏运行，
        └─────┬─────┘   └────┬─────┘   └──────┬───────┘    人脸约 3 次/秒）
              └──────┬───────┴────────────────┘
                     ▼
        ┌────────────────────────┐   ┌──────────────────┐
        │ 逻辑大脑（纯代码）      │   │ 麦克风语音检测   │
        │ · 老师在哪、孩子在不在看 │◄──┤ 判断老师是否在讲话│
        │ · 注视累计 / 人脸居中计时│   └──────────────────┘
        └───────────┬────────────┘
                    ▼
        ┌────────────────────────┐
        │ 小精灵（3D 模型+动画）  │ 状态机驱动：
        │ 引导 / 陪伴 / 倾听 /    │ 引导→陪伴→落星→流星雨
        │ 反馈 / 庆典 + 特效      │ + 语音&音效&星尘路&情绪泡泡
        └────────────────────────┘
```

小知识：AI 模型通过 **Unity Sentis**（Unity 官方的模型运行库）在眼镜本地运行，
**不需要联网**，摄像头画面也不上传。

## 组员快速上手

### 只想看看项目 / 改文档
不用装任何软件，从本 README 和 `docs/` 开始读就行（文档导航见文末）。

### 想运行起来看效果（电脑上预览）
1. 安装 **Unity Hub** + **Unity 6000.4.3f1**（安装时勾选 Android Build Support）
2. Unity Hub → Add → **Add project from disk** → 选中本文件夹
3. 打开后双击场景 `Assets/Scenes/DevEditor.unity` → 点 Play
   （场景缺失时：菜单栏 **ARAttention → Dev → Create DevEditor Scene** 重建）
4. 电脑端没有眼镜，用键盘操作：**K** = 切换"孩子在注视"，**L** = 切换"老师在讲话"，**R** = 重置会话

### 想装到 Quest 3 上（真机）
1. Quest 3 用 USB 连电脑（开启开发者模式），确认 `adb devices` 能看到设备
2. （首次或场景缺失时）菜单栏 **ARAttention → Device → Create Device Scene** 重建真机场景
3. 命令行打包（暂无一键菜单，在工程根目录执行，Unity 路径换成你自己的）：
   ```
   "<Unity安装路径>\Editor\Unity.exe" -batchmode -quit -projectPath "<本工程路径>" -executeMethod BuildScript.BuildDevice
   ```
   产物：`Builds/ARAttention-device.apk`
4. 安装到眼镜（adb 在 Unity 安装目录的 `PlaybackEngines/AndroidPlayer/SDK/platform-tools/` 下）：
   ```
   adb install -r -g Builds/ARAttention-device.apk
   ```
5. 在眼镜里**拿起手柄**启动应用（Horizon 系统要求必须拿手柄）

## 目录结构（哪里放着什么）

| 路径 | 内容 |
|------|------|
| `Assets/Scripts/Core/` | **总装胶水层**：M3DebugDriver（所有模块的总指挥）、经验参数（ExperienceSettings） |
| `Assets/Scripts/Detection/` | 三个 AI 模型的运行器（人体/人脸检测、人体分割）+ 解码 |
| `Assets/Scripts/Interaction/` | 交互状态机（引导→隐藏→反馈 的规则） |
| `Assets/Scripts/Tracking/` | 目标跟踪（锁定"老师"这个人并平滑他的位置） |
| `Assets/Scripts/Gaze/` | 注视判定（孩子在看哪个方向） |
| `Assets/Scripts/UI/` | 小精灵的可视化（3D 模型、特效、情绪泡泡）+ 调试面板 |
| `Assets/Scripts/FrameSource/` | 摄像头画面接入 |
| `Assets/Scripts/Audio/`、`Vad/` | 语音提示 / 麦克风说话检测 |
| `Assets/Scripts/Logging/` | 会话数据记录（双眼见闻都被记下来供分析） |
| `Assets/Tests/EditMode/` | 单元测试（117 个，改代码后必须全绿） |
| `Assets/Art/IP/` | **美术资源**：精灵模型、贴图（清单见 `docs/assets-index.md`） |
| `Assets/Models/` | AI 模型文件（.onnx / .sentis） |
| `Assets/Scenes/` | Device（真机）、DevEditor（电脑调试） |
| `docs/` | 文档（设计/计划/实测/资产清单，导航见 `docs/README.md`） |

## 各角色怎么参与

### 🎨 美术组
- 改精灵模型：从交付包 `source/CH_Sprite_master.blend` 改 → 重新导出 FBX → 覆盖 `Assets/Art/IP/CH_Sprite.fbx`
- 改特效/贴图样式：改 `docs/design/*.html` → 截图导出 PNG → 覆盖 `Assets/Art/IP/` 同名文件
- 资源清单与说明：`docs/assets-index.md`

### 🧪 测试组
- **模拟测试机**（不用眼镜，把 20 分钟课堂剧本演一遍验证逻辑）：
  Unity 菜单 或命令行 `-executeMethod M3SimHarness.Run`（3 个随机种子必须全绿）
- **单元测试**：Unity 菜单 Window → General → Test Runner → EditMode → Run All
- 真机测试清单与观察要点：`docs/test-video-guide.md`

### 📝 文档/其他
- 想看设计意图和每个参数怎么定的 → `docs/specs/2026-10-07-ar-attention-design.md`
- 提出改进意见 → 项目群里说，或写进对应文档

## 目前进度（2026-10-24）

### ✅ 已完成

- **真机全链路**：透视 + 人体/人脸检测 + 引导/隐藏/反馈已在 Quest 3 跑通。
- **流星引导交互 v2**：注视累计落星 → 8 颗触发流星雨 + 庆典；
  三段式引导（疑惑 → 发现 → 带领 → 成功/静默）。
- **v9 注意力引导修复**（本轮，对应 2026-10-24 真机问题清单）：
  - **星尘路重做**：60% 星星固定在中心线（暖金、1.5 倍大）+ 两侧淡紫护栏 +
    起点宽终点窄的锥形宽度 + 一颗沿路飞向老师的**指路彗星**，方向一眼可辨；
  - **进度星环**：单颗 0.16m → 0.24m，半径 0.42m，注视时清晰可见；
  - **注视期不再挥手**：新增 `Attentive` / `Listening` 状态，播放 `IdleFly`，
    不再进入带挥手的 `CompanionIdle`；
  - **对话门控**：老师/旁人讲话时精灵**完全不插话**（语音延后或丢弃），
    系统自己的 TTS/音效播完后 0.3s 内忽略麦克风，防止自听；
  - **引导降频**：触发延迟 0.8 → 1.5s，成功后 10s 冷却，短暂移开不再频繁喊话；
  - **celebrate 烟花**：接入流星雨庆典，集满 8 星时在精灵旁播放。
- **动效素材**：7 个 flipbook（表情/光效/celebrate/star）已接入并通过 7/7 校验。
- **质量**：EditMode **117/117**；模拟测试机 20.2 分钟 × 3 种子全绿；
  讲话期间精灵语音播放数 = **0**。
- **交付**：v9 APK 已构建并安装到 Quest 3（`Builds/ARAttention-device.apk`），**待真机复核**。

### 🔜 当前工作

1. **v9 真机复核（最优先）**：验证星路方向/宽度、进度星大小、注视不挥手、
   对话避让、celebrate 烟花；拉取会话日志中的 `StarPath` / `VoiceGate` /
   `VoicePlayed` / `ListenState` 事件做客观核对。
2. **`Listen`（扶耳倾听）动作**：需求已交建模师
   （`docs/specs/2026-10-24-listen-animation-requirements.md`），
   等 FBX 到位后接 Animator；当前先用 `IdleFly + Curious` 回退。
3. **表情/动画素材升级**：后续替换更精美的表情与动作素材，程序侧只换资源、不改逻辑。
4. **真机数据回归**：对比 17 分钟课堂日志，确认 `GuideConfused` 明显下降、
   讲话期间无精灵语音。

### 📌 待办

- 庆典专属语音真人录音（放 `Assets/Resources/Voice/voice_celebrate.mp3` 即生效）。
- 结题材料中的演示视频与截图更新为 v9 效果。

---

## 文档导航

| 想去哪 | 看这个 |
|---|---|
| 全部文档索引 | `docs/README.md` |
| 设计文档（为什么这样做） | `docs/specs/2026-10-07-ar-attention-design.md` |
| 建模与贴图清单 | `docs/assets-index.md` |
| 测试视频/真机检查指南 | `docs/test-video-guide.md` |
| Git 使用指南（组员必读） | `docs/git-guide.md` |
| 模型交付要求（给美术） | `docs/specs/2026-10-10-ip-3d-model-requirements.md` |
| 扶耳倾听动作需求（给美术） | `docs/specs/2026-10-24-listen-animation-requirements.md` |
| 动效素材需求（给美术） | `docs/specs/2026-10-20-flipbook-effects-requirements.md` |
| 2026-10-24 真机问题与修复计划 | `docs/specs/2026-10-24-attention-guide-issues.md`、`docs/plans/2026-10-24-attention-guide-fixes.md` |
| 里程碑计划与实测记录 | `docs/plans/`、`docs/measurements/` |
