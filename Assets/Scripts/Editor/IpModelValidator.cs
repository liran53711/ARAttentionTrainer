// Assets/Scripts/Editor/IpModelValidator.cs
// 组员 IP 模型（CH_Sprite 精灵）工程侧验收（2026-10-12）：
//   ① 子资产清单：8 个动画剪辑（名称/时长/循环）+ 形态键清单
//   ② 实测尺寸/原点：包围盒身高应 0.350±0.01m、中心≈原点（需求文档硬指标）
//   ③ URP 材质（图集→URP/Lit 不透明）后 渲染五张验收图：
//      +Z 侧（应见"正脸"→ 验证 +Z 朝向）/ -Z 侧 / 三分之四 / GuideFly 动画采样（蒙皮）/ Smile 表情
//   ④ 骨骼抽查（Root/Head/HeadExpression/手指/披风）
// 用法：Unity.exe -batchmode -quit -projectPath <p> -executeMethod IpModelValidator.Validate -logFile <log>
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class IpModelValidator
{
    const string FbxPath = "Assets/Art/IP/CH_Sprite.fbx";
    const string AtlasPath = "Assets/Art/IP/Sprite_Atlas_1024.png";
    const string MatPath = "Assets/Art/IP/Sprite_URP.mat";
    const string OutDir = @"C:\Users\Administrator\.claude\jobs\bbf5228a\tmp\ip_validate";

    public static void Validate()
    {
        try { Run(); }
        catch (System.Exception e)
        {
            Debug.LogError($"[IPVal] 异常：{e}");
            EditorApplication.Exit(3);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void Run()
    {
        Directory.CreateDirectory(OutDir);
        // ★先清出空场景：InstantiatePrefab 进"当前活动场景"，之后再 NewScene 会把实例销毁（2026-10-12 踩坑）
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError($"[IPVal] 未找到 {FbxPath}——先放入并等导入完成"); EditorApplication.Exit(2); return; }
        // 强制重导入：使 SpriteModelImporter 的最新预设立即生效（调导入配置用）
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);

        // ── ① 动画剪辑清单 ──
        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name).ToArray();
        Debug.Log($"[IPVal] 动画剪辑 {clips.Length} 个：");
        foreach (var c in clips)
            Debug.Log($"[IPVal]   剪辑 {c.name} | {c.length:F3}s | {(c.isLooping ? "循环" : "单次")} | {c.frameRate:0}fps");

        // ── ② 实例化 + 结构/尺寸 ──
        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
            Debug.Log($"[IPVal] Animator | avatar={animator.avatar != null} valid={animator.avatar != null && animator.avatar.isValid} " +
                      $"human={animator.avatar != null && animator.avatar.isHuman} rootMotion={animator.applyRootMotion}");

        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var b = new Bounds(); bool first = true;
        foreach (var s in smrs)
        {
            if (first) { b = s.bounds; first = false; } else b.Encapsulate(s.bounds);
            Debug.Log($"[IPVal] 蒙皮网格 {s.name} | 三角面 {s.sharedMesh.triangles.Length / 3} | 骨骼 {s.bones.Length} | " +
                      $"形态键 {s.sharedMesh.blendShapeCount} | 材质 {(s.sharedMaterial != null ? s.sharedMaterial.name : "无")}");
            for (int i = 0; i < s.sharedMesh.blendShapeCount; i++)
                Debug.Log($"[IPVal]   形态键[{i}] {s.sharedMesh.GetBlendShapeName(i)}");
        }
        // 实例在原点、无旋转 → bounds 即模型空间测量
        Debug.Log($"[IPVal] ★尺寸 身高={b.size.y:F4}m×宽{b.size.x:F3}×深{b.size.z:F3}，中心={b.center.ToString("F4")}（要求 0.350±0.01、中心≈原点）");

        // 骨骼/节点全清单 + 关键点抽查
        var allNames = go.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
        Debug.Log($"[IPVal] 节点总数={allNames.Length}：{string.Join(", ", allNames)}");
        foreach (var key in new[] { "Root", "Head", "HeadExpression", "Hand", "Fingers", "Cape", "Chest" })
            Debug.Log($"[IPVal] 关键节点含'{key}'：{(allNames.Any(n => n.Contains(key)) ? "✓" : "✗")}");

        // ── ③ URP 材质 + 渲染 ──
        var mat = EnsureUrpLitMaterial();
        foreach (var s in smrs) s.sharedMaterial = mat;

        var lightGO = new GameObject("Sun");
        var li = lightGO.AddComponent<Light>();
        li.type = LightType.Directional; li.intensity = 1.35f; li.color = new Color(1f, 0.98f, 0.94f);
        lightGO.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);

        var camGO = new GameObject("Cam");
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 32f; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 10f;
        camGO.AddComponent<UniversalAdditionalCameraData>();

        // +Z 侧（若模型面朝 +Z，这里应看到"正脸"）
        RenderShot(cam, go, new Vector3(0f, 0.02f, 0.95f), "01_+Z侧(应见正脸)", Path.Combine(OutDir, "01_front.png"));
        // -Z 侧（应见后脑/披风外侧）
        RenderShot(cam, go, new Vector3(0f, 0.02f, -0.95f), "02_-Z侧(应见背影)", Path.Combine(OutDir, "02_back.png"));
        // 三分之四
        RenderShot(cam, go, new Vector3(0.62f, 0.30f, 0.62f), "03_三分之四", Path.Combine(OutDir, "03_threequarter.png"));

        // 动画采样（蒙皮验证）：GuideFly @0.5s
        var guide = clips.FirstOrDefault(c => c.name == "GuideFly");
        if (guide != null)
        {
            guide.SampleAnimation(go, 0.5f);
            RenderShot(cam, go, new Vector3(0.62f, 0.30f, 0.62f), "04_GuideFly@0.5s(蒙皮)", Path.Combine(OutDir, "04_guidefly.png"));
        }

        // 表情采样：Smile=100
        var face = smrs.FirstOrDefault(s => s.sharedMesh.blendShapeCount > 3);
        if (face != null)
        {
            int si = face.sharedMesh.GetBlendShapeIndex("Smile");
            if (si >= 0)
            {
                if (guide != null) guide.SampleAnimation(go, 0f);
                face.SetBlendShapeWeight(si, 100f);
                RenderShot(cam, go, new Vector3(0f, 0.05f, 0.72f), "05_Smile=100(表情)", Path.Combine(OutDir, "05_smile.png"));
                face.SetBlendShapeWeight(si, 0f);
            }
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // 清场
        Debug.Log("[IPVal] ✅ 验证完成（图见 " + OutDir + "）");
    }

    static void RenderShot(Camera cam, GameObject target, Vector3 camPos, string label, string path)
    {
        cam.transform.position = camPos;
        cam.transform.LookAt(target.transform.position + new Vector3(0f, 0.02f, 0f));
        var rt = new RenderTexture(560, 560, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(cam, req);
        RenderTexture.active = rt;
        var tex = new Texture2D(560, 560, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 560, 560), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        rt.Release();
        Debug.Log($"[IPVal] 渲染 {label} → {path}");
    }

    static Material EnsureUrpLitMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat != null) return mat;
        Directory.CreateDirectory("Assets/Art/IP");
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(sh);
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (atlas != null) mat.SetTexture("_BaseMap", atlas);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(mat, MatPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[IPVal] 已创建 URP 材质 {MatPath}（图集={(atlas != null ? "已接" : "缺失!")}）");
        return mat;
    }
}
