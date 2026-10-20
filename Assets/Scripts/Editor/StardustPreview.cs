// Assets/Scripts/Editor/StardustPreview.cs
// 星尘路渲染自检（2026-10-20 v6）：在不戴头显的情况下，用固定相机把星路渲染成图片，
// 验收：①路头是否在视野内 ②是否贴地形成 3D 透视 ③老师在前/侧/后时方向是否正确
//       ④是否绕开孩子（打印路径到孩子的最近水平距离）。
// 用法：-executeMethod StardustPreview.Render
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class StardustPreview
{
    const string OutDir = @"C:\Users\Administrator\.claude\jobs\bbf5228a\tmp\stardust_preview";
    const int W = 1024, H = 768;

    public static void Render()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 地板 + 光（提供空间参照，看清"贴地/透视"）
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.34f));
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
            var lightGO = new GameObject("Sun");
            var li = lightGO.AddComponent<Light>();
            li.type = LightType.Directional; li.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            // 相机：模拟孩子站在原点、眼睛高 1.6m、朝 +Z（老师离开视野时孩子不看向老师）
            var camGO = new GameObject("Cam");
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 90f; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f);
            cam.nearClipPlane = 0.05f;
            camGO.AddComponent<UniversalAdditionalCameraData>();
            camGO.transform.position = new Vector3(0f, 1.6f, 0f);
            camGO.transform.rotation = Quaternion.identity;

            // 星路（用真实 star_dot 贴图；该材质由 DeviceSceneCreator 在打包时生成）
            var mat = new Material(Shader.Find("ARAttention/GuideArrow"));
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/IP/star_dot.png"));
            mat.SetColor("_Color", Color.white);

            var sdGO = new GameObject("StardustPath");
            var sd = sdGO.AddComponent<StardustPath>();
            sd.starMat = mat;
            sd.eye = camGO.transform;
            sd.eyeHeight = 1.6f; // 地板 y=0
            sd.EnsureBuilt();

            var cases = new (string name, Vector3 teacher)[]
            {
                ("a_front",       new Vector3( 0.0f, 1.35f,  2.6f)),
                ("b_front_right", new Vector3( 1.9f, 1.35f,  1.9f)),
                ("c_right",       new Vector3( 2.8f, 1.35f,  0.3f)),
                ("d_behind_right",new Vector3( 1.9f, 1.35f, -1.7f)),
                ("e_behind",      new Vector3( 0.0f, 1.35f, -2.6f)),
                ("f_far_front",   new Vector3( 0.7f, 1.35f,  4.5f)),
            };

            foreach (var c in cases)
            {
                // 老师标记（预览专用）：1.7m 高圆柱
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Teacher";
                marker.transform.position = new Vector3(c.teacher.x, 0.85f, c.teacher.z);
                marker.transform.localScale = new Vector3(0.36f, 0.85f, 0.36f);
                var mm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mm.SetColor("_BaseColor", new Color(0.95f, 0.45f, 0.25f));
                marker.GetComponent<MeshRenderer>().sharedMaterial = mm;

                // 重置平滑 → 推进 40 帧让星点稳定
                sd.SetOn(false);
                sd.SetOn(true);
                float t = 0f;
                for (int i = 0; i < 40; i++) { t += 1f / 30f; sd.Tick(t, 1f / 30f, c.teacher); }
                Shot(cam, Path.Combine(OutDir, c.name + ".png"));

                // 几何诊断：路径中心线到孩子的最近水平距离（越小越可能穿身）
                float minD = float.MaxValue, minT = 0f;
                Vector3 p0 = default, c1 = default, c2 = default, p3 = default;
                StardustPath.ComputePath(new Vector3(0f, 1.6f, 0f), Vector3.forward, 0f, c.teacher,
                    0.90f, 0.65f, 0.95f, 0.3f, out p0, out c1, out c2, out p3);
                for (int i = 0; i <= 100; i++)
                {
                    Vector3 p = StardustPath.CubicBezier(p0, c1, c2, p3, i / 100f);
                    float d = new Vector2(p.x, p.z).magnitude; // 孩子站在原点
                    if (d < minD) { minD = d; minT = i / 100f; }
                }
                Debug.Log($"[Preview] {c.name}: 老师={c.teacher.ToString("F2")} 起点={p0.ToString("F2")} " +
                          $"终点={p3.ToString("F2")} 最近孩子距离={minD:F2}m@t={minT:F2}");
                Object.DestroyImmediate(marker);
            }

            Debug.Log("[Preview] ✅ 星尘路预览渲染完成 → " + OutDir);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Preview] 异常：{e}");
            EditorApplication.Exit(3);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void Shot(Camera cam, string path)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        RenderPipeline.SubmitRenderRequest(cam, req);
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        rt.Release();
        Debug.Log($"[Preview] 渲染 → {path}");
    }
}
