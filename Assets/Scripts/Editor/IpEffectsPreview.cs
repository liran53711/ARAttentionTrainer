// Assets/Scripts/Editor/IpEffectsPreview.cs
// 特效批量预览（2026-10-12）：一键渲染全部状态效果静帧（供审阅）。
// 用法：-executeMethod IpEffectsPreview.RenderBatch
// 每张图 = 某个时机下精灵 + 柔光/粒子/微动画的代表帧。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class IpEffectsPreview
{
    const string OutDir = @"C:\Users\Administrator\.claude\jobs\bbf5228a\tmp\fx_preview";
    const string SpriteFbx = "Assets/Art/IP/CH_Sprite.fbx";
    const string SpriteMat = "Assets/Art/IP/Sprite_URP.mat";

    static Camera _cam;
    static GameObject _model;
    static IpEffects _fx;
    static GuideArrow _arrow;

    public static void RenderBatch()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            SetupScene();

            var purple = new Color(0.88f, 0.8f, 1f, 0.5f);
            var gold = new Color(1f, 0.92f, 0.6f, 0.6f);

            // 1 出场：聚拢光尘 + 出场小跳
            Set(IPWorldView.IPAnimState.Appearing, 0.3f);
            _fx.PreviewBurst(18, purple, 0.5f);
            Shot("01_出场_光尘聚拢");

            // 2 稳定锁定：柔光确认环
            Set(IPWorldView.IPAnimState.Guiding, 0.25f);
            _fx.Pulse("Lock");
            Advance(0.3f);
            Shot("02_锁定老师_柔光确认环");

            // 3 引导飞行：星尘尾迹
            Set(IPWorldView.IPAnimState.Guiding, 1.2f);
            _fx.PreviewEmit(9f, 1.0f);
            Advance(0.1f);
            Shot("03_引导飞行_星尘尾迹");

            // 4 注视达成
            Set(IPWorldView.IPAnimState.Companion, 0.35f);
            _fx.Pulse("Achieve");
            _fx.PreviewBurst(12, purple, 0.4f);
            Shot("04_注视达成_星尘与柔环");

            // 5 陪伴：萤火虫绕飞
            Set(IPWorldView.IPAnimState.Companion, 2.6f);
            Shot("05_陪伴_萤火虫绕飞");

            // 6 正反馈：金色涟漪 + 星尘雨 + 弹跳
            Set(IPWorldView.IPAnimState.Feedback, 0.5f);
            _fx.Pulse("Feedback");
            _fx.PreviewBurst(30, gold, 0.45f);
            Shot("06_正反馈_金色涟漪与星尘");

            // 7 探头：星光细尾
            Set(IPWorldView.IPAnimState.Peek, 0.3f);
            _fx.PreviewEmit(6f, 0.55f);
            Shot("07_探头_星光细尾");

            // 8 等待注视：极淡呼吸光（半透明待命）
            Set(IPWorldView.IPAnimState.Hidden, 1.6f);
            Shot("08_等待注视_极淡呼吸光");

            // 9 陪伴"困困"下沉（now≈3.5s 处是下沉谷）
            Set(IPWorldView.IPAnimState.Companion, 3.5f);
            Shot("09_陪伴_困困下沉再弹起");

            // 10 重新引导脉冲 + 指向（Guiding + ReGuide 脉冲）
            Set(IPWorldView.IPAnimState.Guiding, 0.3f);
            _fx.Pulse("ReGuide");
            Advance(0.3f);
            Shot("10_重新引导_柔光脉冲");

            // 13 欢呼泡泡（找到老师 → 对话泡泡 + 欢呼表情）
            Set(IPWorldView.IPAnimState.Guiding, 0.3f);
            _fx.Pulse("Lock");
            { var bub = _fx.Bubble; for (float t2 = 0f; t2 < 1.05f; t2 += 0.02f) bub.Tick(t2, 0.02f); }
            Shot("13_泡泡_欢呼(找到老师)");

            // 14 哼歌泡泡（陪伴环绕 → 音符飘出）
            Set(IPWorldView.IPAnimState.Companion, 8.5f);
            _fx.Bubble.Play("Hum", 5f);
            { var bub = _fx.Bubble; for (float t2 = 0f; t2 < 1.55f; t2 += 0.02f) bub.Tick(t2, 0.02f); }
            Shot("14_泡泡_哼歌(音符飘出)");

            // 15 好奇泡泡（探头）
            Set(IPWorldView.IPAnimState.Peek, 0.3f);
            { var bub = _fx.Bubble; for (float t2 = 0f; t2 < 1.1f; t2 += 0.02f) bub.Tick(t2, 0.02f); }
            Shot("15_泡泡_好奇(探头)");

            // ── 箭头场景（独立构图）──
            ArrowScene();
            // 11 引导路：流光 + 信标
            _arrow.EditorPreview(new Vector3(-0.35f, 1.05f, 0.25f), new Vector3(0.55f, 1.18f, 2.0f));
            _arrow.ManualVisual(1.4f); // 流光走到中段
            Shot("11_引导路_流光与信标");

            // 12 铺设中段
            _arrow.EditorPreview(new Vector3(-0.35f, 1.05f, 0.25f), new Vector3(0.55f, 1.18f, 2.0f), 1f, 1.15f);
            _arrow.ManualVisual(0.6f);
            Shot("12_引导路_铺设中");

            Debug.Log($"[FxPreview] 全部完成 → {OutDir}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FxPreview] 异常：{e}");
            EditorApplication.Exit(3);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void SetupScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
        var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        floorMat.SetColor("_BaseColor", new Color(0.42f, 0.4f, 0.38f));
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

        var lightGO = new GameObject("Sun");
        var li = lightGO.AddComponent<Light>();
        li.type = LightType.Directional; li.intensity = 1.15f;
        lightGO.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

        var camGO = new GameObject("Cam");
        _cam = camGO.AddComponent<Camera>();
        _cam.fieldOfView = 42f; _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.12f, 0.12f, 0.16f);
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 20f;
        camGO.AddComponent<UniversalAdditionalCameraData>();
        _cam.transform.position = new Vector3(0f, 1.42f, -0.85f);
        _cam.transform.LookAt(new Vector3(0f, 1.12f, 0f));

        // 精灵（中立姿态即可；特效才是主角）
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SpriteFbx);
        _model = fbx != null ? (GameObject)PrefabUtility.InstantiatePrefab(fbx) : new GameObject("IP_Model3D");
        _model.transform.position = new Vector3(0f, 1.12f, 0f);
        _model.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 面向镜头
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SpriteMat);
        if (mat != null)
            foreach (var smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.sharedMaterial = mat;

        _fx = _model.GetComponent<IpEffects>();
        if (_fx == null) _fx = _model.AddComponent<IpEffects>();
        _fx.eye = _cam.transform;
        _fx.glowMat = TexMat("Assets/Art/IP/glow_soft.png");
        _fx.starMat = TexMat("Assets/Art/IP/star_soft.png");
        _fx.ringMat = TexMat("Assets/Art/IP/ring_soft.png");
        _fx.bubbleMat = TexMat("Assets/Art/IP/bubble.png");
        _fx.cheerMat = TexMat("Assets/Art/IP/face_cheer.png");
        _fx.humMat = TexMat("Assets/Art/IP/face_hum.png");
        _fx.noteMat = TexMat("Assets/Art/IP/note_sing.png");
    }

    static void ArrowScene()
    {
        var arrowGO = new GameObject("GuideArrow");
        _arrow = arrowGO.AddComponent<GuideArrow>();
        _arrow.material = TexMat("Assets/Art/IP/ArrowRibbon.png");
        _arrow.ribbonTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/IP/ArrowRibbon.png");
        _arrow.eye = _cam.transform;
        _arrow.beaconMaterial = TexMat("Assets/Art/IP/glow_soft.png");
        _arrow.cometMaterial = TexMat("Assets/Art/IP/star_soft.png");
        _cam.transform.position = new Vector3(-0.55f, 1.5f, -0.55f);
        _cam.transform.LookAt(new Vector3(0.2f, 1.15f, 1.1f));
    }

    static Material TexMat(string texPath)
    {
        var m = new Material(Shader.Find("ARAttention/GuideArrow"));
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null) m.SetTexture("_MainTex", tex);
        return m;
    }

    /// <summary>设定状态并把特效推进到时间 t（秒）。</summary>
    static void Set(IPWorldView.IPAnimState s, float t)
    {
        _fx.SetMood(s);
        _fx.enabled = false; // 阻止 Update 抢跑（编辑模式本来就不跑，保底）
        float step = 0.02f;
        for (float now = 0f; now < t; now += step)
            _fx.Tick(now, step);
        _model.transform.position = new Vector3(0f, 1.12f, 0f) + _fx.ModelMicroOffset;
    }

    static void Advance(float t)
    {
        float step = 0.02f;
        for (float now = 0f; now < t; now += step)
            _fx.Tick(now, step);
    }

    static void Shot(string name)
    {
        var rt = new RenderTexture(640, 520, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(_cam, req);
        RenderTexture.active = rt;
        var tex = new Texture2D(640, 520, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 640, 520), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        rt.Release();
        Debug.Log($"[FxPreview] {name}");
    }
}
