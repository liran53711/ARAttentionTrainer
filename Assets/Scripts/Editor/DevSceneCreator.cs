// Assets/Scripts/Editor/DevSceneCreator.cs
// 菜单工具：一键创建电脑端开发测试场景 DevEditor
//（视频帧源 + 调试显示 + YOLO 检测器 + 检测框叠加，并自动配置 ExperienceSettings 资产）。
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
        // 0) 确保 Assets/Settings 目录存在
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");

        // 1) 配置资产：没有就创建；把模型指上
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

        // 2) 建场景并挂组件
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var root = new GameObject("VideoSource");
        var source = root.AddComponent<VideoFileFrameSource>();
        root.AddComponent<VideoDebugDisplay>();

        var detector = root.AddComponent<FaceDetector>();
        detector.frameSource = source;
        detector.settings = settings;

        var overlay = root.AddComponent<DetectionDebugOverlay>();
        overlay.detector = detector;

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ARAttention] DevEditor 场景已生成：{ScenePath}");
        EditorUtility.DisplayDialog("完成",
            $"开发测试场景已创建：\n{ScenePath}\n\n" +
            "点 Play：应看到视频播放 + 绿色检测框跟随画面中的人。\n" +
            "（检测框数量/延迟信息显示在画面左上角）", "好");
    }
}
