// Assets/Scripts/Editor/DevSceneCreator.cs
// 菜单工具：一键创建电脑端开发测试场景 DevEditor（视频帧源 + 调试显示）。
// 用法：菜单 ARAttention > Dev > Create DevEditor Scene
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DevSceneCreator
{
    const string ScenePath = "Assets/Scenes/DevEditor.unity";

    [MenuItem("ARAttention/Dev/Create DevEditor Scene")]
    public static void CreateDevScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var root = new GameObject("VideoSource");
        root.AddComponent<VideoFileFrameSource>();
        root.AddComponent<VideoDebugDisplay>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();
        Debug.Log($"[ARAttention] 开发测试场景已生成：{ScenePath}（重复执行会覆盖旧场景）");
        EditorUtility.DisplayDialog("完成",
            $"开发测试场景已创建：\n{ScenePath}\n\n" +
            "下一步：把测试视频放到 Assets/StreamingAssets/ 下\n" +
            "（文件名默认 test.mp4），点 Play 即可看到画面。", "好");
    }
}
