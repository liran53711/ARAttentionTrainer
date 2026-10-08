// Assets/Scripts/Editor/BuildScript.cs
// 命令行构建入口（batchmode 专用）：
//   Unity.exe -batchmode -quit -projectPath <工程> -executeMethod BuildScript.BuildAndroid -logFile -
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    static readonly string[] Scenes = { "Assets/Scenes/DevEditor.unity" };

    public static void BuildAndroid()
    {
        ConfigureAndroidTools();

        // 构建前自动重建开发场景，保证场景接线与最新组件一致（防止"忘了重跑菜单"）
        try
        {
            DevSceneCreator.CreateDevScene();
            Debug.Log("[Build] DevEditor 场景已重建");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Build] 场景重建失败（沿用现有场景）：{e.Message}");
        }

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = "Builds/ARAttention-dev.apk",
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        };

        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[Build] 结果={report.summary.result} " +
                  $"大小={report.summary.totalSize} 字节 耗时={report.summary.totalTime}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>把 Android 工具链路径显式写进编辑器偏好（batchmode 下自带 JDK 检测会失灵）。</summary>
    static void ConfigureAndroidTools()
    {
        const string root = @"D:\Development\Unity\Editor\Data\PlaybackEngines\AndroidPlayer";
        EditorPrefs.SetString("JdkPath", $@"{root}\OpenJDK");
        EditorPrefs.SetString("AndroidSdkRoot", $@"{root}\SDK");
        EditorPrefs.SetString("AndroidNdkRoot", $@"{root}\NDK");
        Debug.Log($"[Build] 工具路径已设置：JdkPath={root}\\OpenJDK");
    }
}
