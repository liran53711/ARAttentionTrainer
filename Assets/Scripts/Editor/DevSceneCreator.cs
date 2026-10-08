// Assets/Scripts/Editor/DevSceneCreator.cs
// 菜单工具：一键创建电脑端开发测试场景 DevEditor
//（视频帧源 + 调试显示 + YOLO 检测器 + 检测框 + IP占位 + 音效 + M3总装驱动）。
// 用法：菜单 ARAttention > Dev > Create DevEditor Scene（重复执行会覆盖旧场景）
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;
using UnityEngine;

public static class DevSceneCreator
{
    const string ScenePath = "Assets/Scenes/DevEditor.unity";
    const string SettingsPath = "Assets/Settings/ExperienceSettings.asset";
    const string ModelPath = "Assets/Models/yolov9sentis.sentis";

    [MenuItem("ARAttention/Dev/Create DevEditor Scene")]
    public static void CreateDevScene()
    {
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
        {
            EditorUtility.DisplayDialog("缺少模型",
                $"没有找到 {ModelPath}\n\n" +
                "请切到 Unity 窗口让它先导入模型文件（等右下角转完），再执行本菜单。", "好");
            return;
        }
        settings.modelAsset = model;
        EditorUtility.SetDirty(settings);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var root = new GameObject("VideoSource");

        var source = root.AddComponent<VideoFileFrameSource>();
        root.AddComponent<VideoDebugDisplay>();

        var detector = root.AddComponent<FaceDetector>();
        detector.frameSource = source;
        detector.settings = settings;

        var overlay = root.AddComponent<DetectionDebugOverlay>();
        overlay.detector = detector;

        var ipView = root.AddComponent<IPDebugView>();
        var audio = root.AddComponent<AudioCuePlayer>();
        var vad = root.AddComponent<MicVadService>();

        var driver = root.AddComponent<M3DebugDriver>();
        driver.detector = detector;
        driver.settings = settings;
        driver.ipView = ipView;
        driver.audioPlayer = audio;
        driver.vadService = vad;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ARAttention] DevEditor 场景已生成：{ScenePath}");
        EditorUtility.DisplayDialog("完成",
            $"开发测试场景已创建：{ScenePath}\n\n" +
            "点 Play 后：\n" +
            "· 前2秒安静（噪声底校准），然后对着麦克风讲话＝老师在说话\n" +
            "· 默认（讲话中+未注视）→ IP 圆出现并移向画面中的人\n" +
            "· K 切换模拟注视（持续 0.8s → 隐藏）\n" +
            "· 停止讲话约0.5秒＝一段话讲完（有麦时自动判定）\n" +
            "· R 重置会话；会话数据在 persistentDataPath/sessions/ 下", "好");
    }
}
