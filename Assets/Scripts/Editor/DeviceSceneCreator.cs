// Assets/Scripts/Editor/DeviceSceneCreator.cs
// 菜单工具：一键创建真机 MR 场景 Device
//（OVRCameraRig(含 OVRManager) + 前置相机取帧 PCA + 权限流 + 相机画面调试面板 + 检测/VAD/M3 驱动）。
// 用法：菜单 ARAttention > Device > Create Device Scene（重复执行会覆盖旧场景）
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;
using Meta.XR;
using Meta.XR.MRUtilityKit;

public static class DeviceSceneCreator
{
    const string ScenePath = "Assets/Scenes/Device.unity";
    const string SettingsPath = "Assets/Settings/ExperienceSettings.asset";
    const string ModelPath = "Assets/Models/yolov9sentis.sentis";
    const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
    const string LineTargetMatPath = "Assets/Materials/BoxTarget.mat";
    const string LineOtherMatPath = "Assets/Materials/BoxOther.mat";
    const string OccluderMatPath = "Assets/Materials/MaskErase.mat";
    /// <summary>遮挡（MaskOccluder）总开关——2026-10-11 用户定调"难做就不要了"，默认关。</summary>
    const bool EnableOcclusion = false;

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
        settings.detectionConfThreshold = 0.55f; // 2026-10-12：人偶误识别 → 提高置信度门槛
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

        // ── 7) 权限流（相机 + 空间数据权限弹窗 + 轮询）──
        rigGO.AddComponent<PermissionFlow>();
        // ── 7.5) 空间数据（MRUK）：启动加载/扫描，为射线定距与 3D 精灵房间锚定铺垫 ──
        rigGO.AddComponent<MrukBootstrap>();

        // ── 8) 检测 + VAD + 音效 + M3 总装（真机模式）──
        var detector = camGO.AddComponent<FaceDetector>();
        detector.frameSource = source;
        detector.settings = settings;

        var audio = camGO.AddComponent<AudioCuePlayer>();
        var voice = camGO.AddComponent<VoiceCuePlayer>(); // 语音指导（Resources/Voice/*.mp3）
        var vad = camGO.AddComponent<MicVadService>();

        var driver = camGO.AddComponent<M3DebugDriver>();
        driver.detector = detector;
        driver.settings = settings;
        driver.audioPlayer = audio;
        driver.voiceCue = voice;
        driver.vadService = vad;
        driver.simulateGazeWithKeyboard = false; // 真机：几何注视代理（脸在视野中心+新鲜度）
        driver.useMicVad = true;                 // 真机：真实麦克风 VAD
        // ipView 留空：IP（小精灵）可视化是下一步的世界空间版

        // ── 9) 数据面板（世界空间 Canvas + 文本；替代旧的"相机画面监视器"）──
        var panelGO = new GameObject("DataPanel");
        panelGO.transform.SetParent(rig.centerEyeAnchor, false);
        panelGO.transform.localPosition = new Vector3(0.28f, 0.02f, 0.75f); // 视野右侧浮一块状态屏
        panelGO.transform.localScale = Vector3.one * 0.0016f;               // 560px≈0.9m 宽
        var canvas = panelGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(560, 260);

        var bgGO = new GameObject("BG");
        bgGO.transform.SetParent(panelGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.07f, 0.10f, 0.85f);
        Stretch(bgImg.rectTransform);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(panelGO.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 26;
        txt.color = new Color(0.72f, 1f, 0.78f);
        txt.alignment = TextAnchor.UpperLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.text = "等待数据…";
        Stretch(txt.rectTransform, 14, 12);

        // ── 10) 3D 检测框 + 面板驱动 ──
        var overlay = camGO.AddComponent<Detection3DOverlay>();
        overlay.detector = detector;
        overlay.frameSource = source;
        overlay.targetMaterial = EnsureLineMaterial(LineTargetMatPath, new Color(0.25f, 1f, 0.45f));
        overlay.otherMaterial = EnsureLineMaterial(LineOtherMatPath, new Color(0.55f, 0.72f, 1f));

        var panel = camGO.AddComponent<DeviceDataPanel>();
        panel.detector = detector;
        panel.vad = vad;
        panel.driver = driver;
        panel.mruk = rigGO.GetComponent<MrukBootstrap>();
        panel.text = txt;

        // ── 10.5) 世界空间 IP（小精灵）：贴合老师头部 —— 驱动层与电脑版共用同一套动画状态
        var ipWorld = camGO.AddComponent<IPWorldView>();
        ipWorld.frameSource = source;
        ipWorld.detector = detector;
        ipWorld.mruk = rigGO.GetComponent<MrukBootstrap>(); // 空间围栏：锚点不出房间
        driver.ipWorldView = ipWorld;

        // ── 10.6) 人体轮廓分割（陪伴模式：精灵沿真实轮廓贴合互动）
        var seg = camGO.AddComponent<SelfieSegRunner>();
        seg.frameSource = source;
        seg.modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/selfie_seg_landscape.onnx");
        if (seg.modelAsset == null)
            Debug.LogWarning("[DeviceScene] 未找到 selfie_seg_landscape.onnx（轮廓贴合暂不可用）");
        ipWorld.seg = seg;

        // ── 10.7) A：人物遮挡（屏幕空间擦除）——2026-10-11 用户定调："遮挡难做就不要了"
        // 默认关闭：Scene 里不再生成 MaskOccluder（MaskOccluder.cs / MaskErase.shader 保留，
        // 想再试把 EnableOcclusion 改 true 重建场景即可）。
        if (EnableOcclusion)
        {
            var occGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            occGO.name = "MaskOccluder";
            Object.DestroyImmediate(occGO.GetComponent<Collider>());
            occGO.transform.SetParent(rig.centerEyeAnchor, false);
            occGO.GetComponent<MeshRenderer>().sharedMaterial = EnsureShaderMaterial(OccluderMatPath, "ARAttention/MaskErase");
            var occluder = occGO.AddComponent<MaskOccluder>();
            occluder.seg = seg;
            occluder.frameSource = source;
            occluder.pca = pca;
            occluder.eyeCamera = centerCam;
            Debug.Log("[DeviceScene] 遮挡（MaskOccluder）已启用");
        }
        else
        {
            Debug.Log("[DeviceScene] 遮挡（MaskOccluder）未启用——用户定调 2026-10-11（代码保留，开关 EnableOcclusion）");
        }

        // ── 10.8) B：环境射线（MRUK 深度 → 实测距离）
        var envRay = rigGO.AddComponent<EnvironmentRaycastManager>();
        ipWorld.envRaycast = envRay;

        // ── 10.9) C：3D 模型接口节点（2026-10-12：组员正式模型 CH_Sprite 接入；缺资产回退火柴人）
        var modelGO = BuildSpriteIp3D();
        modelGO.transform.SetParent(rigGO.transform, false);
        ipWorld.model3D = modelGO;
        ipWorld.modelNativeHeight = 0.3593f; // 实测身高（IpModelValidator）：0.35±0.01 内；FBX 无需改尺寸
        ipWorld.animator = modelGO.GetComponentInChildren<Animator>(); // 动画参数（Guiding/Companion/Appear/Feedback/Peek）由 IPWorldView 驱动
        var ipFx = modelGO.GetComponent<IpEffects>();
        if (ipFx != null) ipFx.eye = rig.centerEyeAnchor; // 特效朝向以观察者为参照

        // ── 10.95) 引导"路"（2026-10-12 v3；PPT 第10页：视线离开 → 精灵当路标 + 地板箭头铺向老师）──
        // 世界空间物体（不再是相机贴片）：贴地 + 真实透视 = 空间感
        // 2026-10-12 用户定调："箭头是低效信息" → 改为"IP 飞向老师方位 + 方向性立体声"方案。
        // 箭头组件与资产保留备用（EnableGuideArrow 改 true 即恢复）。
        const bool EnableGuideArrow = false;
        if (EnableGuideArrow)
        {
            var arrowGO = new GameObject("GuideArrow");
            arrowGO.transform.SetParent(rigGO.transform, false);
            var arrow = arrowGO.AddComponent<GuideArrow>();
            arrow.material = EnsureGuideArrowMaterial();
            arrow.ribbonTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/IP/ArrowRibbon.png");
            arrow.eye = rig.centerEyeAnchor;
            arrow.beaconMaterial = EnsureTexturedMat("Assets/Materials/FxGlow.mat", "Assets/Art/IP/glow_soft.png");
            arrow.cometMaterial = EnsureTexturedMat("Assets/Materials/FxStar.mat", "Assets/Art/IP/star_soft.png");
            driver.guideArrow = arrow;
        }

        // ── 11) 保存 ──
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ARAttention] Device 场景已生成：{ScenePath}");
    }

    /// <summary>柔光/柔星/柔环材质（ARAttention/GuideArrow 通用透明贴图着色器）。</summary>
    static Material EnsureTexturedMat(string path, string texPath)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("ARAttention/GuideArrow");
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader && shader != null) mat.shader = shader;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null) mat.SetTexture("_MainTex", tex);
        mat.SetColor("_Color", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>引导箭头材质（ARAttention/GuideArrow 半透明着色器资产——被场景引用保证进包）。</summary>
    static Material EnsureGuideArrowMaterial()
    {
        const string path = "Assets/Materials/GuideArrow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null && mat.shader != null && mat.shader.name == "ARAttention/GuideArrow") return mat;
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        var shader = Shader.Find("ARAttention/GuideArrow");
        if (shader == null)
        {
            Debug.LogError("[DeviceScene] 找不到 ARAttention/GuideArrow 着色器，回退 URP/Unlit（将丢失顶点色渐变）");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        mat.SetColor("_Color", Color.white); // 整体淡入淡出（运行时驱动）
        // 设计贴图（2026-10-12 第六轮：箭头外观=设计资产）
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/IP/ArrowRibbon.png");
        if (tex != null) mat.SetTexture("_MainTex", tex);
        else Debug.LogWarning("[DeviceScene] 缺 ArrowRibbon.png——箭头将没有图案（请检查 Assets/Art/IP/）");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>线框材质（做成资产并被场景引用，保证着色器进构建包不被裁剪）。</summary>
    static Material EnsureLineMaterial(string path, Color color)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color; // URP/Unlit 的 [MainColor] _BaseColor
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>按名称创建/加载材质资产（被场景引用 → 着色器进包不被裁剪）。</summary>
    static Material EnsureShaderMaterial(string path, string shaderName)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[DeviceScene] 找不到着色器 {shaderName}，回退 URP/Unlit");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    const string SpriteFbxPath = "Assets/Art/IP/CH_Sprite.fbx";
    const string SpriteCtrlPath = "Assets/Art/IP/CH_Sprite.controller";
    const string SpriteMatPath = "Assets/Art/IP/Sprite_URP.mat";

    /// <summary>C 项正式 3D 精灵（2026-10-12 组员交付 CH_Sprite 验收后接入）：
    /// FBX 预置体 + Animator 控制器 + SpriteExpressionController + URP 图集材质。
    /// 验收记录见 IpModelValidator（+Z 朝向：bakeAxisConversion=false）；缺资产时回退火柴人占位。</summary>
    static GameObject BuildSpriteIp3D()
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SpriteFbxPath);
        if (fbx == null)
        {
            Debug.LogWarning("[DeviceScene] 未找到 CH_Sprite.fbx → 回退火柴人占位体");
            return BuildStickFigureIp();
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        go.name = "IP_Model3D";

        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            var ctrl = IpAnimatorBuilder.Ensure();
            if (ctrl != null) animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false; // 世界轨迹由 IPWorldView 全权负责
        }
        else Debug.LogWarning("[DeviceScene] CH_Sprite 无 Animator——动画不会播放");

        if (go.GetComponent<SpriteExpressionController>() == null)
            go.AddComponent<SpriteExpressionController>();

        // 精灵特效系统（2026-10-12：柔光光晕/萤火虫/星尘/脉冲环/表情微动画）
        if (go.GetComponent<IpEffects>() == null) go.AddComponent<IpEffects>();
        var fx = go.GetComponent<IpEffects>();
        fx.glowMat = EnsureTexturedMat("Assets/Materials/FxGlow.mat", "Assets/Art/IP/glow_soft.png");
        fx.starMat = EnsureTexturedMat("Assets/Materials/FxStar.mat", "Assets/Art/IP/star_soft.png");
        fx.ringMat = EnsureTexturedMat("Assets/Materials/FxRing.mat", "Assets/Art/IP/ring_soft.png");
        fx.bubbleMat = EnsureTexturedMat("Assets/Materials/EmoteBubble.mat", "Assets/Art/IP/bubble.png");
        fx.cheerMat = EnsureTexturedMat("Assets/Materials/EmoteCheer.mat", "Assets/Art/IP/face_cheer.png");
        fx.humMat = EnsureTexturedMat("Assets/Materials/EmoteHum.mat", "Assets/Art/IP/face_hum.png");
        fx.noteMat = EnsureTexturedMat("Assets/Materials/EmoteNote.mat", "Assets/Art/IP/note_sing.png");
        // 表情更醒目（用户反馈"表情变化不太清楚"）：Curious 歪头 10°→14°（序列化字段，用 SerializedObject 设置）
        var expr = go.GetComponent<SpriteExpressionController>();
        var so = new SerializedObject(expr);
        var tiltProp = so.FindProperty("curiousTiltDegrees");
        if (tiltProp != null)
        {
            tiltProp.floatValue = 14f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SpriteMatPath);
        if (mat != null)
        {
            // ★袍子/披风"内侧透明"修复（2026-10-12 用户实测第二报）：披风等是单层薄面几何，
            // Unity 默认背面剔除 → 从内侧看直接被裁掉=像透明。改成双面渲染（URP Lit Render Face=Both）。
            mat.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(mat);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.sharedMaterial = mat;
        }
        else Debug.LogWarning("[DeviceScene] 缺 Sprite_URP.mat——将沿用 FBX 自带材质（可能发粉，请先跑 IpModelValidator）");

        // ★蒙皮包围盒冗余（2026-10-12 用户实测"袍子看起来透明、里面黄色" + 组员文档预警"边缘消失"）：
        // 绑定包围盒不够大时，动画中摆动的披风/袍子会被整体剔除（消失后透出里面），
        // 先按组员建议检查/扩大包围盒（而不是 updateWhenOffscreen 兜底）。
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var lb = smr.localBounds;
            smr.localBounds = new Bounds(lb.center, lb.size * 1.8f);
            Debug.Log($"[DeviceScene] {smr.name} 包围盒 {lb.size.ToString("F3")} → {smr.localBounds.size.ToString("F3")}（防动作裁切）");
        }

        Debug.Log("[DeviceScene] 已接入 CH_Sprite（Animator 控制器 + 表情控制器 + URP 图集材质）");
        return go;
    }

    /// <summary>火柴人占位（无 FBX 时的回退）：骨架/姿态由 StickFigureIp 运行时程序化搭建与平滑驱动。</summary>
    static GameObject BuildStickFigureIp()
    {
        var root = new GameObject("IP_Model3D");
        var stick = root.AddComponent<StickFigureIp>();
        stick.bodyMaterial = EnsureLineMaterial("Assets/Materials/IpBody.mat", new Color(0.62f, 0.47f, 1f));
        stick.eyeMaterial = EnsureLineMaterial("Assets/Materials/IpEye.mat", new Color(0.12f, 0.10f, 0.20f));
        return root;
    }

    static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    static void Fail(string msg)
    {
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog("缺少资源", msg, "好");
        // 统一抛异常：批量模式靠它中止；菜单模式控制台能看到红字
        throw new System.Exception("[DeviceScene] " + msg);
    }
}
