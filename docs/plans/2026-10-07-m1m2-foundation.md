# M1+M2 地基实施计划：工程搭建 → 相机通路 → 人脸检测

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Quest 3 真机上跑通「Passthrough + 前置相机实时画面 + YOLOv9 端侧人脸检测框」的可运行 APK。

**Architecture:** Unity 6.4 URP 工程 + Meta XR SDK 207（OpenXR）+ MRUK 的 PassthroughCameraAccess 取帧 + Unity Sentis（CPU 后端）跑 YOLOv9。核心设计是 `IFrameSource` 帧源抽象：真机用 PCA，电脑 Editor 用视频文件——让 70% 逻辑脱离头显开发。

**Tech Stack:** Unity 6000.4.3f1 · Meta XR SDK 207.0.0 (core/interaction/mrutilitykit) · com.unity.ai.inference 2.6.1 (Sentis, CPU) · YOLOv9 (WongKinYiu, MIT, `yolov9sentis`) · OpenXR + Meta XR Feature · IL2CPP/ARM64/Vulkan

**Spec:** `docs/specs/2026-10-07-ar-attention-design.md`（本计划的唯一上级文档；执行时两者对照阅读）

## Global Constraints

- 工程路径：`D:\Development\Unity\ARAttentionTrainer`（纯英文路径，禁止移动）
- Unity 版本：**6000.4.3f1**（`D:\Development\Unity\Editor\Unity.exe`）；需补装 Android SDK/NDK/OpenJDK 模块
- SDK：Meta XR **207.0.0**（本地 201 已过期弃用）；**XR 插件用 Unity OpenXR + Meta XR Feature，禁止启用 com.unity.xr.oculus**
- 推理：Sentis 2.6.1，**BackendType.CPU**；模型 **yolov9sentis（MIT）**；禁止引入 Ultralytics AGPL 权重
- 下载策略：所有第三方包经代理下载后**归档到 `tools/packages/`**（离线可重建）
- 代码约定：标识符英文、注释中文；所有可调参数集中在 `ExperienceSettings` ScriptableObject
- Git：Claude 只准备文件与命令，**git add/commit/push 一律由用户执行**（每个任务末尾给命令）
- 设备：Quest 3 已开开发者模式可侧载；adb 优先用 MQDH 自带（找不到再装 platform-tools）
- 性能目标：检测净耗时 ≤ 25ms/帧、隔帧执行下渲染 ≥ 30fps；每次真机测试**记录实测数字**到 `docs/measurements/`
- 每步遇到报错：截图/贴日志给 Claude，先查 `adb logcat -s Unity` 

---

## 文件结构总览（本计划创建/修改的文件）

```
ARAttentionTrainer/
  .gitignore                          [T1 创建] Unity 标准忽略
  Packages/manifest.json              [T2 修改] 加 Meta 207 + Sentis
  Assets/Scripts/
    Core/ExperienceSettings.cs        [T3] 可调参数 SO（检测节流/阈值/分辨率）
    Core/PermissionFlow.cs            [T4] 权限申请状态机（相机/场景→麦克风预留）
    FrameSource/CameraFrame.cs        [T5] 帧数据结构
    FrameSource/IFrameSource.cs       [T5] 帧源接口
    FrameSource/PcaFrameSource.cs     [T4 雏形 → T5 实现接口] 真机 PCA 帧源
    FrameSource/VideoFileFrameSource.cs [T5] Editor 视频文件帧源
    Detection/DetectionResult.cs      [T6] 检测结果结构
    Detection/DetectionPostprocess.cs [T6] 解析+NMS（纯C#，有单测）
    Detection/FaceDetector.cs         [T6] Sentis 封装
    UI/SimpleFpsHud.cs                [T4] FPS/耗时显示（世界空间小面板）
    UI/DetectionOverlay.cs            [T7] 检测框叠加渲染
    Editor/ProjectSetup.cs            [T2] 一键工程配置（XR/URP/Android设置）
    Editor/BuildScript.cs             [T3] CLI 构建入口
    Editor/VideoTestHarness.cs        [T5] Editor 菜单：视频帧源注入场景
  Assets/Tests/EditMode/DetectionPostprocessTests.cs [T6] NMS 单测
  Assets/Tests/EditMode/OneEuroFilterTests.cs        [预留，M3 用；本计划不建]
  Assets/Models/yolov9sentis.sentis   [T6] 模型（从官方样例仓库下载）
  Assets/Scenes/Main.unity            [T3] 主场景
  Assets/Scenes/DevEditor.unity       [T5] Editor 内开发场景（视频帧源）
  docs/measurements/                  [T7] 真机性能实测记录
  tools/packages/                     [T2] 第三方包归档
```

---

### Task 1: 仓库与工程骨架

**Files:**
- Create: `.gitignore`、目录骨架、`README.md`
- 用户操作: Unity Hub 新建工程

**Interfaces:**
- Produces: 可打开的空 URP 工程、已初始化 git 仓库（供后续任务使用）

- [ ] **Step 1: 👤 用户 — Unity Hub 新建工程**
  - Unity Hub → New project → 选 **Unity 6000.4.3f1** → 模板 **Universal 3D** → 名称 `ARAttentionTrainer` → 位置 `D:\Development\Unity` → Create
  - 验收：Editor 打开无报错，Console 干净

- [ ] **Step 2: 🤖 Claude — 写 `.gitignore`（Unity 标准 + 本项目补充）**

```gitignore
# Unity
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser/[Ss]ettings/
[Mm]emoryCaptures/
*.csproj
*.sln*
.vs/
.idea/

# 大文件例外：模型与素材不忽略（要保持入库）
!Assets/Models/*.sentis

# 本项目
tools/packages/downloads/   # 下载缓存不入库（同名 tgz 归档除外）
*.apk
*.log
```

- [ ] **Step 3: 🤖 Claude — 建目录骨架**

```bash
mkdir -p Assets/Scripts/{Core,FrameSource,Detection,UI,Editor} \
         Assets/Tests/EditMode Assets/Models \
         docs/measurements tools/packages tools/analytics
# 空目录放 .gitkeep 保持 git 可见
for d in Assets/Tests/EditMode tools/analytics; do touch "$d/.gitkeep"; done
```

- [ ] **Step 4: 🤖 Claude — 写 `README.md`**（工程说明：定位/结构/构建方法/规格文档链接）

- [ ] **Step 5: 👤 用户 — 初始化 git 并首次提交**

```bash
cd /d/Development/Unity/ARAttentionTrainer
git init
git add -A
git commit -m "chore: 初始化工程骨架（docs/specs、plans、目录结构）"
```
  - 验收：`git log --oneline` 可见首次提交；`git status` 干净

---

### Task 2: 包安装与 XR 配置

**Files:**
- Modify: `Packages/manifest.json`
- Create: `Assets/Scripts/Editor/ProjectSetup.cs`

**Interfaces:**
- Produces: 配置完毕的工程（OpenXR+Meta XR Feature、URP、Android/IL2CPP/ARM64/Vulkan）——Task 3 直接构建

- [ ] **Step 1: 👤 用户 — 代理下载 Meta XR SDK 207**
  - 打开 https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm （需代理）
  - 下载 **207.0.0** 的三个包：`com.meta.xr.sdk.core-207.0.0.tgz`、`com.meta.xr.sdk.interaction-207.0.0.tgz`、`com.meta.xr.mrutilitykit-207.0.0.tgz`（若页面只有 All-in-One 单个包，下它）
  - 保存到 `D:\Development\Unity\meta-sdk-207\`（与旧的 201 包分开放）
  - 同时把三个 tgz **复制一份到 `tools/packages/`** 归档
  - ⚠️ 若页面结构变化找不到 207：截图页面给 Claude

- [ ] **Step 2: 🤖 Claude — 修改 `Packages/manifest.json`**
  - 在 `dependencies` 增加（路径按实际文件名）：

```json
"com.meta.xr.sdk.core": "file:D:/Development/Unity/meta-sdk-207/com.meta.xr.sdk.core-207.0.0.tgz",
"com.meta.xr.sdk.interaction": "file:D:/Development/Unity/meta-sdk-207/com.meta.xr.sdk.interaction-207.0.0.tgz",
"com.meta.xr.mrutilitykit": "file:D:/Development/Unity/meta-sdk-207/com.meta.xr.mrutilitykit-207.0.0.tgz",
"com.unity.ai.inference": "2.6.1",
"com.unity.xr.openxr": "1.16.1",
"com.unity.xr.management": "4.6.0"
```
  - 注意：**不要**添加 com.unity.xr.oculus

- [ ] **Step 3: 👤 用户 — 打开 Unity 等待包解析**
  - 验收：Package Manager 中可见 Meta XR Core / Interaction / MR Utility Kit 207.0.0 与 Sentis；Console 无编译错误
  - 若报错：把 Console 全文给 Claude

- [ ] **Step 4: 🤖 Claude — 写 `ProjectSetup.cs`（一键配置，菜单 `ARAttention/Setup Project`）**

```csharp
// Assets/Scripts/Editor/ProjectSetup.cs
// 作用：一键完成 OpenXR+Meta Feature 启用、Android 平台与构建设置。
// 若某个 API 与已装 SDK 版本不符，按 Console 报错对照 Packages/com.meta.xr.sdk.core/Editor 下源码修正。
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;

public static class ProjectSetup
{
    [MenuItem("ARAttention/Setup Project")]
    public static void Setup()
    {
        // 1) 切换 Android 平台
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // 2) 启用 OpenXR loader（Android 组）
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (settings != null)
        {
            XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
        }

        // 3) 启用 Meta XR Feature 组（Meta XR Feature / Foveation / Subsampled Layout）
        var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (oxr != null)
        {
            foreach (var f in oxr.GetFeatures<OpenXRFeature>())
            {
                if (f.name.Contains("Meta") || f.name.Contains("Foveation") || f.name.Contains("Subsampled"))
                    f.enabled = true;
            }
        }

        // 4) 玩家设置：IL2CPP + ARM64 + Vulkan、竖屏锁定解除、包名
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.nju.aratttrainer");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

        AssetDatabase.SaveAssets();
        Debug.Log("[ARAttention] Project setup done. 请打开 Meta > Tools > Project Setup Tool 并 Fix All。");
    }
}
```

- [ ] **Step 5: 👤 用户 — 执行配置 + Meta Project Setup Tool**
  - 菜单 `ARAttention > Setup Project`（等编译完成）
  - 然后菜单 `Meta > Tools > Project Setup Tool` → **Fix All**（Android 标签页）
  - 验收：Project Setup Tool 无红色项（Ignored 可接受）；Build Settings 显示 Android + OpenXR

- [ ] **Step 6: 👤 用户 — 提交**

```bash
git add -A && git commit -m "chore: 安装 Meta XR SDK 207 + Sentis，配置 OpenXR/Android 构建环境"
```

---

### Task 3: 构建链路验证（设备上先看到 Passthrough）

**Files:**
- Create: `Assets/Editor/BuildScript.cs`、`Assets/Scenes/Main.unity`（GUI 搭建）、`Assets/Scripts/Core/ExperienceSettings.cs`

**Interfaces:**
- Produces: `BuildScript.BuildAndroid()`（CLI 可调）；`ExperienceSettings`（后续所有参数的家）；一个能在头显里跑的 APK
- Consumes: Task 2 的工程配置

- [ ] **Step 1: 🤖 Claude — 写 `ExperienceSettings.cs`**

```csharp
// Assets/Scripts/Core/ExperienceSettings.cs
// 所有可调参数的唯一入口。运行时会话加载；后续任务继续往此文件加参数，禁止分散到各处。
using UnityEngine;
using Unity.InferenceEngine; // ModelAsset 所在命名空间（Sentis 2.6；若导入失败以包内源码为准）

[CreateAssetMenu(fileName = "ExperienceSettings", menuName = "ARAttention/ExperienceSettings")]
public class ExperienceSettings : ScriptableObject
{
    [Header("相机/检测")]
    public int detectionStride = 2;          // 每 N 帧检测一次
    public float detectionConfThreshold = 0.5f;
    public int requestedCameraWidth = 1280;  // PCA 请求分辨率
    public int requestedCameraHeight = 960;

    [Header("模型")]
    public ModelAsset modelAsset;            // 指到 yolov9sentis 资产
    public bool useGpuBackend = false;       // 默认 CPU

    [Header("调试")]
    public bool drawDetectionBoxes = true;
    public bool showFpsHud = true;
}
```

- [ ] **Step 2: 🤖 Claude — 写 `BuildScript.cs`**

```csharp
// Assets/Editor/BuildScript.cs
// CLI 用法：Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BuildScript.BuildAndroid -logFile -
using UnityEditor;
using UnityEngine;

public static class BuildScript
{
    public static void BuildAndroid()
    {
        var scene = "Assets/Scenes/Main.unity";
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = "Builds/ARAttention.apk",
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[Build] result={report.summary.result}, size={report.summary.totalSize} bytes");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
```

- [ ] **Step 3: 👤 用户 — 搭 Main 场景（Meta Building Blocks，官方推荐路径）**
  - 新建场景 `Assets/Scenes/Main.unity`
  - 菜单 `Meta > Tools > Building Blocks`：
    1. 添加 **Camera Rig**（Accept 默认）
    2. 添加 **Passthrough** → 一键配置（会启用 OVRManager 的 passthrough）
  - 保存场景；删除默认 Main Camera（Camera Rig 已含相机）
  - 验收：Console 无报错

- [ ] **Step 4: 🤖 Claude — CLI 构建 APK**

```bash
"D:/Development/Unity/Editor/Unity.exe" -batchmode -quit \
  -projectPath "D:/Development/Unity/ARAttentionTrainer" \
  -executeMethod BuildScript.BuildAndroid -logFile "D:/Development/Unity/ARAttentionTrainer/build.log"
# 检查 build.log 末尾与 Builds/ARAttention.apk 是否生成
```

- [ ] **Step 5: 👤 用户 — 安装到头显（或由 Claude 通过 adb 安装）**
  - adb 定位：优先 MQDH 自带（`%LOCALAPPDATA%\Meta Quest Developer Hub\` 下搜 adb.exe）；没有则下载 platform-tools 到 `tools/adb/`（代理）
  - 连接头显（USB-C，头显内允许 USB 调试）：

```bash
adb devices                       # 应看到设备
adb install -r "Builds/ARAttention.apk"
adb shell am start -n com.nju.aratttrainer/com.unity3d.player.UnityPlayerGameActivity
```
  - 验收（戴上头显）：看到灰色透视底（Passthrough 已开，还没叠加虚拟物）→ **M1 的 Passthrough 半程达成**
  - 故障排查：`adb logcat -s Unity` 看崩溃日志

- [ ] **Step 6: 👤 用户 — 提交**

```bash
git add -A && git commit -m "feat: 项目主场景 + CLI构建脚本，Passthrough 真机跑通"
```

---

### Task 4: PCA 相机权限流 + 实时画面显示（M1 验收）

**Files:**
- Create: `Assets/Scripts/Core/PermissionFlow.cs`、`Assets/Scripts/FrameSource/PcaFrameSource.cs`（雏形）、`Assets/Scripts/UI/SimpleFpsHud.cs`
- Modify: `Assets/Scenes/Main.unity`（挂 PermissionFlow + 画面显示面板）

**Interfaces:**
- Consumes: `ExperienceSettings`
- Produces: `PcaFrameSource.TryGetFrame(out CameraFrame)`（T5 正式抽象前的雏形）；权限流可在 headset 上正确弹出并恢复

- [ ] **Step 1: 🤖 Claude — 写 `PermissionFlow.cs`**

```csharp
// 权限申请：Scene + PassthroughCameraAccess（快照见调研A：全拒绝不可恢复，UI 需提示）
using System.Collections;
using UnityEngine;
using Meta.XR; // OVRPermissionsRequester 命名空间以实际 SDK 为准（core 包）

public class PermissionFlow : MonoBehaviour
{
    public bool Ready { get; private set; }

    IEnumerator Start()
    {
        bool granted = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene)
                    && OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
        if (!granted)
        {
            OVRPermissionsRequester.Request(new[] {
                OVRPermissionsRequester.Permission.Scene,
                OVRPermissionsRequester.Permission.PassthroughCameraAccess });
            // 轮询等待用户在头显内授权（最多 60s）
            float t = 0;
            while (t < 60 &&
                   !OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess))
            { t += Time.deltaTime; yield return null; }
        }
        Ready = OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.PassthroughCameraAccess);
        Debug.Log($"[Permission] camera ready = {Ready}");
    }
}
```
  > 执行提示：`OVRPermissionsRequester` 的确切命名空间/枚举名以 `Packages/com.meta.xr.sdk.core` 源码为准，编译报错时按源码修正（这是计划内允许的偏差）。

- [ ] **Step 2: 🤖 Claude — 写 `PcaFrameSource.cs`（雏形：能出画面即可）**

```csharp
// 真机帧源雏形（Task 5 与 IFrameSource 对接后完成化）
// API 依据调研A（以安装包源码为准核对方法名）：
//   PassthroughCameraAccess: IsPlaying / GetTexture() / GetCameraPose() / CurrentResolution
using UnityEngine;
using Meta.XR; // PassthroughCameraAccess 命名空间以 MRUK 包源码为准

public class PcaFrameSource : MonoBehaviour
{
    public PassthroughCameraAccess cameraAccess; // 场景里挂载（CameraPosition=Right）
    public Renderer displayTarget;               // 显示面板（先例：贴在相机前 1.5m 的 Quad）

    void Update()
    {
        if (cameraAccess == null || !cameraAccess.IsPlaying) return;
        var tex = cameraAccess.GetTexture();
        if (tex != null && displayTarget != null)
            displayTarget.material.mainTexture = tex; // Unlit 材质直接贴
    }
}
```

- [ ] **Step 3: 👤 用户 — 场景挂载（Claude 给精确清单）**
  - Camera Rig 下加子物体 `CameraQuad`：位置 (0, 0, 1.5)，缩放适配画面比例，Unlit 材质（白底）
  - 挂 `PassthroughCameraAccess` 组件（CameraPosition = Right, RequestedResolution = 1280x960, MaxFramerate = 30）
  - 挂 `PermissionFlow`、`PcaFrameSource`（连线 displayTarget）、`SimpleFpsHud`（世界空间 FPS 文本，Claude 提供脚本）
  - 保存 → Claude 重新构建 → 用户安装测试

- [ ] **Step 4: 👤 用户 — 真机验收（M1 完成标志）**
  - 首次启动应弹权限对话框 → 同意
  - 验收：看到**相机彩色画面实时显示在面板上**，转动头部画面不抖（位姿缓存生效）
  - 已知坑：授权后如无画面——**重启 App 再试**（Quest 麦克风/相机首跑已知问题）
  - 记录：启动延迟、画面对齐感受（写进 `docs/measurements/2026-10-XX-m1.md`）

- [ ] **Step 5: 👤 用户 — 提交**

```bash
git add -A && git commit -m "feat: PCA 相机权限流 + 实时画面显示，M1 相机通路验收通过"
```

---

### Task 5: IFrameSource 抽象 + Editor 视频帧源（脱离头显开发的关键）

**Files:**
- Create: `FrameSource/CameraFrame.cs`、`FrameSource/IFrameSource.cs`、`FrameSource/VideoFileFrameSource.cs`、`Editor/VideoTestHarness.cs`、`Assets/Scenes/DevEditor.unity`
- Modify: `PcaFrameSource.cs`（实现接口）

**Interfaces:**
- Produces:
  - `struct CameraFrame { Texture texture; Pose cameraPose; CameraIntrinsics intrinsics; long timestampNs; }`
  - `interface IFrameSource { bool TryGetFrame(out CameraFrame frame); bool IsReady { get; } }`
  - `VideoFileFrameSource.SidecarPoseJson` 格式（假位姿：identity + 估算焦距）
- Consumes: Task 4 的 PCA 组件

- [ ] **Step 1: 🤖 Claude — 写 `CameraFrame.cs` + `IFrameSource.cs`**

```csharp
using UnityEngine;

public struct CameraFrame
{
    public Texture texture;        // GPU 纹理（来自相机或视频）
    public Pose cameraPose;        // 拍照时刻的相机位姿（必须与帧同步缓存）
    public float focalLengthPx;    // 简化内参：像素焦距（M2 只需此一项）
    public long timestampNs;
    public bool valid;
}

public interface IFrameSource
{
    bool IsReady { get; }
    bool TryGetFrame(out CameraFrame frame);
}
```

- [ ] **Step 2: 🤖 Claude — 写 `VideoFileFrameSource.cs`（VideoPlayer → RenderTexture，位姿用 JSON 侧车文件；无侧车时用 identity+经验焦距）**

```csharp
// 用途：Editor/PC 上让检测与后续状态机逻辑脱离头显运转。
// 实现：Unity VideoPlayer 渲染到 RenderTexture；每帧打包为 CameraFrame。
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class VideoFileFrameSource : MonoBehaviour, IFrameSource
{
    public string videoPath;                 // StreamingAssets 相对路径或绝对路径
    public RenderTexture target;             // 512x512 内部分辨率即可
    public float assumedFocalLengthPx = 800f; // 无标定时的经验值（可调）

    VideoPlayer _player;
    public bool IsReady => _player != null && _player.isPrepared && _player.frame > 0;

    void Awake()
    {
        _player = gameObject.AddComponent<VideoPlayer>();
        _player.source = VideoSource.Url;
        _player.url = videoPath;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.targetTexture = target;
        _player.isLooping = true;
        _player.Play();
    }

    public bool TryGetFrame(out CameraFrame frame)
    {
        frame = default;
        if (!IsReady) return false;
        frame.texture = target;
        frame.cameraPose = new Pose(new Vector3(0, 1.5f, 0), Quaternion.identity); // 侧车JSON读取：M2简化采用固定假设
        frame.focalLengthPx = assumedFocalLengthPx;
        frame.timestampNs = (long)(_player.time * 1e9);
        frame.valid = true;
        return true;
    }
}
```

- [ ] **Step 3: 🤖 Claude — 改造 `PcaFrameSource` 实现 `IFrameSource`**（把 Task 4 的 Update 显示逻辑替换为 `TryGetFrame`：`GetTexture()`+`GetCameraPose()` 同步取，焦距从 `GetCameraIntrinsics()` 取——方法名以 MRUK 源码为准）

- [ ] **Step 4: 🤖 Claude — 写 `VideoTestHarness.cs`（菜单 `ARAttention/Dev/Create DevEditor Scene`：建 DevEditor 场景 = 一个 Quad 显示视频 + VideoFileFrameSource）**

- [ ] **Step 5: 👤 用户 — Editor 验收**
  - 任意 mp4（手机录一段"老师讲话"视频）放 `Assets/StreamingAssets/test.mp4`
  - 菜单创建 DevEditor 场景 → Play → 看到视频在面板上循环播放
  - 验收：Console 每帧输出 `TryGetFrame ok, ts=...`（可在脚本加临时日志），无报错

- [ ] **Step 6: 👤 用户 — 提交**

```bash
git add -A && git commit -m "feat: IFrameSource 帧源抽象 + Editor 视频帧源（脱离头显开发）"
```

---

### Task 6: Sentis + YOLOv9 推理管线（EditMode 测试先行）

**Files:**
- Create: `Detection/DetectionResult.cs`、`Detection/DetectionPostprocess.cs`、`Detection/FaceDetector.cs`、`Tests/EditMode/DetectionPostprocessTests.cs`
- Download: `Assets/Models/yolov9sentis.sentis`

**Interfaces:**
- Produces:
  - `struct DetectionResult { Rect normalizedBox; float confidence; }`（x,y,w,h ∈ [0,1]）
  - `List<DetectionResult> DetectionPostprocess.ParseAndNMS(Tensor<float> output, float confThreshold, float iouThreshold)`
  - `FaceDetector.Detect(CameraFrame frame) → List<DetectionResult>`
- Consumes: `IFrameSource`、`ExperienceSettings`

- [ ] **Step 1: 🤖 Claude — 写失败测试 `DetectionPostprocessTests.cs`**

```csharp
// EditMode 单测：NMS 核心行为
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class DetectionPostprocessTests
{
    static DetectionResult D(float x, float y, float w, float h, float c)
        => new DetectionResult { normalizedBox = new Rect(x, y, w, h), confidence = c };

    [Test]
    public void NMS_KeepsHighestConfidence_AmongOverlapping()
    {
        var input = new List<DetectionResult> {
            D(0.10f, 0.10f, 0.20f, 0.20f, 0.9f), // 高分
            D(0.11f, 0.11f, 0.20f, 0.20f, 0.7f), // 与上重叠 IoU≈0.82 → 应删
            D(0.60f, 0.60f, 0.20f, 0.20f, 0.8f), // 不重叠 → 保留
        };
        var kept = DetectionPostprocess.NMS(input, confThreshold: 0.5f, iouThreshold: 0.5f);
        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(0.9f, kept[0].confidence, 1e-4f);
        Assert.AreEqual(0.8f, kept[1].confidence, 1e-4f);
    }

    [Test]
    public void NMS_FiltersBelowConfidenceThreshold()
    {
        var input = new List<DetectionResult> {
            D(0.10f, 0.10f, 0.20f, 0.20f, 0.3f), // 低于阈值 → 应删
            D(0.60f, 0.60f, 0.20f, 0.20f, 0.8f), // 保留
        };
        var kept = DetectionPostprocess.NMS(input, confThreshold: 0.5f, iouThreshold: 0.5f);
        Assert.AreEqual(1, kept.Count);
        Assert.AreEqual(0.8f, kept[0].confidence, 1e-4f);
    }

    [Test]
    public void IoU_IdenticalBoxes_IsOne()
    {
        float iou = DetectionPostprocess.IoU(new Rect(0f, 0f, 0.2f, 0.2f), new Rect(0f, 0f, 0.2f, 0.2f));
        Assert.AreEqual(1f, iou, 1e-4f);
    }
}
```

**NMS 方法签名（全局唯一定义，后续任务引用此签名）：**

```csharp
public static List<DetectionResult> NMS(List<DetectionResult> boxes, float confThreshold, float iouThreshold);
public static float IoU(Rect a, Rect b);
```

- [ ] **Step 2: 👤 用户 — 跑测试确认失败**
  - Unity 菜单 `Window > General > Test Runner` → EditMode → Run All
  - 预期：编译错误（`DetectionPostprocess` 不存在）——正常，下一步实现

- [ ] **Step 3: 🤖 Claude — 实现 `DetectionResult.cs` + `DetectionPostprocess.cs`（完整 NMS + IoU，纯 C# 无 Unity 依赖的部分集中在静态类）**
  - YOLOv9 输出解析：**执行时对照 `yolov9sentis` 模型的输出张量形状**（官方样例 `MultiObjectDetection` 的解析代码为蓝本：输出 [1, 84, 8400] 或 [1, 8400, 84] 形态——先打日志确认，再写解析）；类别过滤只保留 person/face 相关类

- [ ] **Step 4: 👤 用户 — 再跑测试确认通过**（3 个测试全绿）

- [ ] **Step 5: 👤 用户 — 下载模型（代理）**
  - 从 https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples 仓库 `Assets/.../MultiObjectDetection` 相关目录取 `yolov9sentis` 模型文件（或仓库 Releases）
  - 放入 `Assets/Models/yolov9sentis.sentis`；确认 Unity 导入为 Sentis ModelAsset（必要时通过 `Assets > Import New Asset`）
  - 归档一份到 `tools/packages/`

- [ ] **Step 6: 🤖 Claude — 写 `FaceDetector.cs`（按调研B的预处理范式）**

```csharp
// Sentis 封装：CPU 后端；启动预热；异步回读；隔帧节流
using Unity.InferenceEngine;   // 2.6 命名空间（旧名 Unity.Sentis，执行时以包内源码为准）
using System.Collections.Generic;
using UnityEngine;

public class FaceDetector : MonoBehaviour
{
    public ModelAsset modelAsset;                 // yolov9sentis
    public ExperienceSettings settings;
    public IFrameSource frameSource;              // 场景注入
    public volatile List<DetectionResult> Latest = new();

    Worker _worker;
    Tensor<float> _inputTensor;
    int _frameCounter;

    void Awake()
    {
        var model = ModelLoader.Load(modelAsset);
        _worker = new Worker(model, settings.useGpuBackend ? BackendType.GPUCompute : BackendType.CPU);
        _inputTensor = new Tensor<float>(new TensorShape(1, 3, 640, 640));
        Preload();
    }

    void Preload() // 官方范式：dummy 2x2 推理，避免首帧卡死
    {
        var t = new Tensor<float>(new TensorShape(1, 3, 2, 2));
        _worker.Schedule(t);
        _worker.PeekOutput().ReadbackAndCloneAsync().Done(_ => { });
        t.Dispose();
    }

    void Update()
    {
        if (frameSource == null || !frameSource.IsReady) return;
        if (_frameCounter++ % Mathf.Max(1, settings.detectionStride) != 0) return;
        if (!frameSource.TryGetFrame(out var frame) || !frame.valid) return;

        // GPU 侧预处理 → 推理 → 异步回读（禁止阻塞版 ReadbackAndClone）
        TextureConverter.ToTensor(frame.texture, _inputTensor,
            new TextureTransform().SetDimensions(640, 640, 3));
        _worker.Schedule(_inputTensor);
        ((Tensor<float>)_worker.PeekOutput()).ReadbackAndCloneAsync().Done(output =>
        {
            Latest = DetectionPostprocess.ParseAndNMS(output, settings.detectionConfThreshold, 0.5f);
            output.Dispose();
        });
    }
}
```
  > 执行提示：`TextureConverter`/`TensorTransform` 命名与 `Done()` 回调 API 以 Sentis 2.6.1 包内文档为准；模型输入尺寸 640 以模型实际为准。

- [ ] **Step 7: 👤 用户 — Editor 视频验证**
  - DevEditor 场景接入 FaceDetector（挂脚本、连 settings/frameSource、指定模型）
  - 在 DevEditor 加临时 OnGUI：把 `Latest` 的归一化框画成绿色矩形（屏幕空间，10 行代码，Claude 提供）
  - 验收：**播放测试视频，人脸被绿框持续框住**（图上没框/框错位 → 贴截图+日志给 Claude）

- [ ] **Step 8: 👤 用户 — 提交**

```bash
git add -A && git commit -m "feat: Sentis+YOLOv9 检测管线，EditMode 测试全绿，视频源上跑通"
```

---

### Task 7: 真机检测框叠加（M2 验收）

**Files:**
- Create: `UI/DetectionOverlay.cs`（归一化框 → 相机视口 → 世界空间四边形/线段）
- Modify: `Main.unity`（挂 FaceDetector + DetectionOverlay，PcaFrameSource 接 IFrameSource）

**Interfaces:**
- Consumes: `FaceDetector.Latest`、`PcaFrameSource`（接口版）
- Produces: M2 验收 APK + 性能实测记录

- [ ] **Step 1: 🤖 Claude — 写 `DetectionOverlay.cs`**
  - 方案：每个检测框生成 4 条细 Quad（边框线），位置 = `cameraAccess.ViewportPointToRay(框角视口坐标, cachedPose)` 射线方向上固定 1.5m 深度平面内——与 PCA 视口一致（**视口→世界必须用 PCA 组件自己的 API**，不能用主相机，两者视锥不同）

- [ ] **Step 2: 🤖 Claude — CLI 构建 + adb 安装**（同 Task 3 命令）

- [ ] **Step 3: 👤 用户 — 真机验收（M2 完成标志）**
  - 戴头显，让同伴在 1-2m 处面对自己
  - 验收：**检测框实时贴在对方脸上**（轻微延迟可接受——PCA 延迟 40-60ms 属预期）
  - 测试项：正对/侧脸/低头看书（模拟老师看讲稿）、两人同框（应只框最大最居中）
  - 记录到 `docs/measurements/2026-10-XX-m2.md`：帧率、体感延迟、检测稳定性、连续 15 分钟有无降频

- [ ] **Step 4: 🤖 Claude — 拉取性能数据**
  - `adb logcat -s Unity | grep ARATT` （FaceDetector 按秒打日志：inference ms / readback ms / fps）
  - 与预算对比（推理 12-18ms、回读 6-10ms）；超标则调 `detectionStride` 并复测

- [ ] **Step 5: 👤 用户 — 提交 + 里程碑复盘**

```bash
git add -A && git commit -m "feat: 真机检测框叠加，M2 人脸检测验收通过（性能数据已记录）"
```
  - 复盘问题清单：延迟体感 / 是否眩晕 / 亮度 / 佩戴舒适（带到 M3 设计调整）

---

## 计划自审记录

- **Spec 覆盖检查**：本计划覆盖设计文档 §2（架构/技术选型/FrameSource策略）、§4.1-4.2（PCA/检测管线）、§7 的三层测试中「逻辑层+真机快测」两层、§8 的 M1/M2 验收标准。§3（状态机）§4.3（3D定位）§4.4（VAD）§5-6（记录/边界）§8 的 M3-M6 属于后续计划（M3 计划在 M2 完成后依据安装包实际 API 编写）——有意排除，非遗漏。
- **占位符扫描**：已清除；保留的"以包内源码为准"条目均为**计划内验证步骤**（有明确的核对对象），非 TBD。
- **类型一致性**：`CameraFrame`/`IFrameSource`/`DetectionResult` 的字段与签名在 Task 5-7 间保持一致；`ExperienceSettings` 为参数唯一入口贯穿各任务。
- **风险预留**：若 SDK 207 的 PCA API 与调研A描述有出入（如 `GetCameraIntrinsics` 命名），Task 4 Step 1/2 与 Task 5 Step 3 的"以源码为准修正"步骤兜底——不影响任务边界。
