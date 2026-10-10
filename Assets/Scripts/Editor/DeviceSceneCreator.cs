// Assets/Scripts/Editor/DeviceSceneCreator.cs
// 菜单工具：一键创建真机 MR 场景 Device
//（OVRCameraRig(含 OVRManager) + 前置相机取帧 PCA + 权限流 + 相机画面调试面板 + 检测/VAD/M3 驱动）。
// 用法：菜单 ARAttention > Device > Create Device Scene（重复执行会覆盖旧场景）
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;
using UnityEngine;
using Meta.XR;

public static class DeviceSceneCreator
{
    const string ScenePath = "Assets/Scenes/Device.unity";
    const string SettingsPath = "Assets/Settings/ExperienceSettings.asset";
    const string ModelPath = "Assets/Models/yolov9sentis.sentis";
    const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
    const string FeedMatPath = "Assets/Materials/PcaFeedUnlit.mat";

    [MenuItem("ARAttention/Device/Create Device Scene")]
    public static void CreateDeviceScene()
    {
        // ── 1) 共享配置资产（与 PC 开发场景同一份 ExperienceSettings）──
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        var settings = AssetDatabase.LoadAssetAtPath<ExperienceSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ExperienceSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        if (model == null)
            Fail($"没有找到 {ModelPath}（先在编辑器里等它导入完）");
        settings.modelAsset = model;
        EditorUtility.SetDirty(settings);

        // ── 2) 空场景：不要模板自带相机/灯光（OVRCameraRig 自带相机，双相机会双渲染）──
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 3) OVRCameraRig（头显跟踪；根节点带 OVRManager）──
        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (rigPrefab == null)
            Fail($"没有找到 {RigPrefabPath}");
        var rigGO = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
        rigGO.name = "OVRCameraRig";
        var rig = rigGO.GetComponent<OVRCameraRig>();
        var manager = rigGO.GetComponent<OVRManager>();

        // ── 4) 透视：运行时开关 + 中心相机背景透明 + 显式提交透视图层 ──
        manager.isInsightPassthroughEnabled = true;
        // ★ OVRPassthroughLayer 必须加：它向合成器提交透视 Underlay 层。
        //   实测证据（2026-10-10 日志）：不加时 "PT is: ON numLayers: 0" + "renderIteration: PT is: OFF"，
        //   合成器无透视层可渲染 → 透明背景露出的是黑色而不是透视画面。
        rigGO.AddComponent<OVRPassthroughLayer>();
        var centerCam = rig.centerEyeAnchor.GetComponent<Camera>();
        if (centerCam != null)
        {
            centerCam.clearFlags = CameraClearFlags.SolidColor;
            centerCam.backgroundColor = Color.clear; // 透明 → 透视画面透出来
        }

        // ── 5) 前置相机帧源 ──
        var camGO = new GameObject("PassthroughCamera");
        var pca = camGO.AddComponent<PassthroughCameraAccess>();
        pca.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
        pca.RequestedResolution = new Vector2Int(settings.requestedCameraWidth, settings.requestedCameraHeight);
        // URP 的 Unlit 着色器纹理属性是 _BaseMap；默认 _MainTex 在 URP 里不生效（材质保持纯白）
        pca.TexturePropertyName = "_BaseMap";
        var source = camGO.AddComponent<PcaFrameSource>();

        // ── 6) 相机画面调试面板：世界空间 Quad + 材质（PCA 每帧把相机纹理写进 TargetMaterial）──
        var feedMat = EnsureFeedMaterial();
        pca.TargetMaterial = feedMat;
        var feedQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        feedQuad.name = "CameraFeedPanel";
        Object.DestroyImmediate(feedQuad.GetComponent<Collider>()); // 纯显示，不需要碰撞
        feedQuad.GetComponent<MeshRenderer>().sharedMaterial = feedMat;
        feedQuad.transform.SetParent(rig.centerEyeAnchor, false);
        feedQuad.transform.localPosition = new Vector3(0.30f, -0.08f, 0.75f); // 视野右下方浮一块"监视器"
        feedQuad.transform.localScale = new Vector3(0.53f, 0.40f, 1f);        // 4:3，与 1280×960 对应

        // ── 7) 权限流（相机权限弹窗 + 轮询）──
        rigGO.AddComponent<PermissionFlow>();

        // ── 8) 检测 + VAD + 音效 + M3 总装（真机模式）──
        var detector = camGO.AddComponent<FaceDetector>();
        detector.frameSource = source;
        detector.settings = settings;

        var audio = camGO.AddComponent<AudioCuePlayer>();
        var vad = camGO.AddComponent<MicVadService>();

        var driver = camGO.AddComponent<M3DebugDriver>();
        driver.detector = detector;
        driver.settings = settings;
        driver.audioPlayer = audio;
        driver.vadService = vad;
        driver.simulateGazeWithKeyboard = false; // 真机：几何注视代理（脸在视野中心+新鲜度）
        driver.useMicVad = true;                 // 真机：真实麦克风 VAD
        // ipView 留空：M1 先验通路（透视+相机画面+检测日志），IP 可视化随 M3.5 做世界空间版

        // ── 9) 保存 ──
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ARAttention] Device 场景已生成：{ScenePath}");
    }

    static Material EnsureFeedMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FeedMatPath);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, FeedMatPath);
        return mat;
    }

    static void Fail(string msg)
    {
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("缺少资源", msg, "好");
        // 统一抛异常：批量模式靠它中止；菜单模式控制台能看到红字
        throw new System.Exception("[DeviceScene] " + msg);
    }
}
