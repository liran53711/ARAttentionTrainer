// Assets/Scripts/Editor/GuideArrowPreview.cs
// 引导路渲染自检（2026-10-12）：批量模式渲染"铺设中/铺完"两张图，验收网格与着色器。
// 用法：-executeMethod GuideArrowPreview.Render
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class GuideArrowPreview
{
    const string OutDir = @"C:\Users\Administrator\.claude\jobs\bbf5228a\tmp\ip_validate";

    public static void Render()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 地板 + 光
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.45f, 0.42f, 0.4f));
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var lightGO = new GameObject("Sun");
            var li = lightGO.AddComponent<Light>();
            li.type = LightType.Directional; li.intensity = 1.2f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);

            // 路（起点近、终点远，沿 +Z）
            var arrowGO = new GameObject("GuideArrow");
            var arrow = arrowGO.AddComponent<GuideArrow>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/GuideArrow.mat");
            if (mat == null)
            {
                var sh = Shader.Find("ARAttention/GuideArrow");
                Debug.LogWarning($"[ArrowPreview] 材质资产缺失，直接建临时材质，shader={sh}");
                mat = new Material(sh);
            }
            arrow.material = mat;
            arrow.ribbonTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/IP/ArrowRibbon.png");
            if (arrow.ribbonTexture == null) Debug.LogWarning("[ArrowPreview] 缺 ArrowRibbon.png");

            // 相机（模拟孩子视线：前方偏下看向地面上的路）
            var camGO = new GameObject("Cam");
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 55f; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.11f, 0.14f);
            camGO.AddComponent<UniversalAdditionalCameraData>();
            arrow.eye = cam.transform; // 头的朝向倾斜以观察者为参照
            cam.transform.position = new Vector3(0f, 1.55f, -0.5f);
            cam.transform.LookAt(new Vector3(0.35f, 1.15f, 1.4f));

            // 图1：铺设中（铺了一半）
            arrow.EditorPreview(new Vector3(0f, 1.10f, 0.5f), new Vector3(0.8f, 1.35f, 2.5f), 1f, 1.2f);
            Shot(cam, Path.Combine(OutDir, "10_road_laying.png"));
            // 图2：铺完
            arrow.EditorPreview(new Vector3(0f, 1.10f, 0.5f), new Vector3(0.8f, 1.35f, 2.5f));
            Shot(cam, Path.Combine(OutDir, "11_road_full.png"));
            Debug.Log("[ArrowPreview] ✅ 引导路预览渲染完成");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ArrowPreview] 异常：{e}");
            EditorApplication.Exit(3);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void Shot(Camera cam, string path)
    {
        var rt = new RenderTexture(640, 480, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(cam, req);
        RenderTexture.active = rt;
        var tex = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        rt.Release();
        Debug.Log($"[ArrowPreview] 渲染 → {path}");
    }
}
