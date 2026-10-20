// Assets/Scripts/Editor/IpModelRenderBatch.cs
// 精灵模型批量参考渲染（2026-10-20）：8 个动作姿态 + 5 个表情 + 5 个观察角度，共 18 张 720×720。
// 用途：模型交付验收 / 组员看效果 / 宣传素材。
// 用法：Unity.exe -batchmode -quit -projectPath <工程> -executeMethod IpModelRenderBatch.Run
// 输出：C:\Users\Administrator\.claude\jobs\bbf5228a\tmp\ip_render_v2（可改 OutDir）
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class IpModelRenderBatch
{
    const string FbxPath = "Assets/Art/IP/CH_Sprite.fbx";
    const string MatPath = "Assets/Art/IP/Sprite_URP.mat";
    static readonly string OutDir = Path.GetFullPath(Path.Combine("Builds", "ip_render")); // 工程根/Builds/ip_render
    const int Size = 720;

    [MenuItem("ARAttention/IP/Render Reference Batch")]
    public static void Run()
    {
        try { RunInner(); }
        catch (Exception e) { Debug.LogError("[IPRender] 异常：" + e); EditorApplication.Exit(2); }
    }

    static void RunInner()
    {
        Directory.CreateDirectory(OutDir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError($"[IPRender] 未找到 {FbxPath}"); EditorApplication.Exit(2); return; }
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);

        var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name).ToArray();

        var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat != null) foreach (var s in smrs) s.sharedMaterial = mat;
        // ★2026-10-20 关键：蒙皮矩阵默认每帧只算一次——batchmode 单帧内"采样→渲染"多次时
        // 会一直复用首帧缓存矩阵（8 张动作图渲染完全相同）。强制每次渲染重算。
        foreach (var s in smrs) s.forceMatrixRecalculationPerRender = true;

        // ── 三灯布光（主光 + 补光 + 轮廓光），深蓝紫背景衬托紫色角色 ──
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.50f);
        AddLight(new Vector3(40f, -35f, 0f), 1.35f, new Color(1f, 0.98f, 0.94f), "KeyLight");
        AddLight(new Vector3(15f, 130f, 0f), 0.45f, new Color(0.75f, 0.82f, 1f), "FillLight");
        AddLight(new Vector3(15f, 180f, 0f), 1.1f, new Color(0.9f, 0.85f, 1f), "RimLight");

        var camGO = new GameObject("Cam");
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 30f; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.085f, 0.09f, 0.125f);
        cam.nearClipPlane = 0.03f; cam.farClipPlane = 10f;
        camGO.AddComponent<UniversalAdditionalCameraData>();

        // ── A. 8 个动作姿态（3/4 视角全身）──
        string[] actions = { "IdleFly", "GuideFly", "CompanionIdle", "AppearFly", "HappyFeedback", "Peek", "Point", "Wave" };
        float[] times    = { 0.6f,      0.5f,      1.5f,            0.45f,       0.6f,            0.3f,   0.5f,    0.5f     };
        var full3q = new Vector3(0.58f, 0.30f, 0.62f);
        var hand = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Hand.L"));
        for (int i = 0; i < actions.Length; i++)
        {
            var clip = clips.FirstOrDefault(c => c.name == actions[i]);
            if (clip == null) { Debug.LogWarning($"[IPRender] 缺动作 {actions[i]}"); continue; }
            ClearExpressions(smrs);
            var before = hand != null ? hand.position : Vector3.zero;
            clip.SampleAnimation(go, times[i]);
            var after = hand != null ? hand.position : Vector3.zero;
            Debug.Log($"[IPRender] {actions[i]}@t={times[i]} 采样前后 Hand.L {before.ToString("F3")} → {after.ToString("F3")} (变化 {(after - before).magnitude:F4})");
            Render(cam, go, full3q, $"动作 {actions[i]}@{times[i]}s",
                   Path.Combine(OutDir, $"{10 + i:00}_anim_{actions[i]}.png"));
        }

        // ── B. 5 个表情（正面近景，脸占画面）──
        var faceSmr = smrs.FirstOrDefault(s => s.sharedMesh.blendShapeCount > 3);
        var idle = clips.FirstOrDefault(c => c.name == "IdleFly");
        string[] exprs = { "Smile", "Surprised", "Curious", "Encourage", "Blink" };
        var facePos = new Vector3(0f, 0.06f, 0.42f);
        for (int i = 0; i < exprs.Length; i++)
        {
            if (faceSmr == null) break;
            ClearExpressions(smrs);
            if (idle != null) idle.SampleAnimation(go, 0f);
            int si = faceSmr.sharedMesh.GetBlendShapeIndex(exprs[i]);
            if (si >= 0) faceSmr.SetBlendShapeWeight(si, 100f);
            Render(cam, go, facePos, $"表情 {exprs[i]}", Path.Combine(OutDir, $"{20 + i:00}_expr_{exprs[i]}.png"));
        }
        ClearExpressions(smrs);
        if (idle != null) idle.SampleAnimation(go, 0f);

        // ── C. 5 个观察角度（IdleFly 待机姿态）──
        (string name, Vector3 pos, string label)[] shots =
        {
            ("front",        new Vector3(0f, 0.06f, 0.85f),        "正面"),
            ("side",         new Vector3(0.85f, 0.06f, 0.02f),     "纯侧面"),
            ("back",         new Vector3(0f, 0.08f, -0.85f),       "背面（披风）"),
            ("hero_low",     new Vector3(0.45f, -0.18f, 0.70f),    "仰视英雄角"),
            ("top_down",     new Vector3(0.40f, 0.55f, 0.45f),     "俯视"),
        };
        foreach (var shot in shots)
            Render(cam, go, shot.pos, $"角度 {shot.label}", Path.Combine(OutDir, $"30_angle_{shot.name}.png"));

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Debug.Log($"[IPRender] ✅ 全部渲染完成（{Directory.GetFiles(OutDir, "*.png").Length} 张，见 {OutDir}）");
        EditorApplication.Exit(0);
    }

    static void AddLight(Vector3 euler, float intensity, Color color, string name)
    {
        var go = new GameObject(name);
        var li = go.AddComponent<Light>();
        li.type = LightType.Directional;
        li.intensity = intensity; li.color = color;
        // direction 光转向：rotation 的 -Z 朝目标；这里按"从某个方位照过来"设绕原点朝向
        go.transform.rotation = Quaternion.Euler(euler.x, euler.y, euler.z);
    }

    static void ClearExpressions(SkinnedMeshRenderer[] smrs)
    {
        foreach (var s in smrs)
            for (int i = 0; i < s.sharedMesh.blendShapeCount; i++)
                s.SetBlendShapeWeight(i, 0f);
    }

    static void Render(Camera cam, GameObject target, Vector3 camPos, string label, string path)
    {
        cam.transform.position = camPos;
        cam.transform.LookAt(target.transform.position + new Vector3(0f, 0.02f, 0f));
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(cam, req);
        RenderTexture.active = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        rt.Release();
        Debug.Log($"[IPRender] 渲染 {label} → {Path.GetFileName(path)}");
    }
}
