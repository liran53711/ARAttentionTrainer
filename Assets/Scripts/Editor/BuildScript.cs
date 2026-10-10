// Assets/Scripts/Editor/BuildScript.cs
// 命令行构建入口（batchmode 专用）：
//   PC:   Unity.exe -batchmode -quit -projectPath <工程> -executeMethod BuildScript.BuildAndroid -logFile <log>
//   真机: ... -executeMethod BuildScript.PrepareDevice   （只配置+建场景，便宜的自检步骤）
//         ... -executeMethod BuildScript.BuildDevice     （配置+建场景+打 MR APK）
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    static readonly string[] DevScenes = { "Assets/Scenes/DevEditor.unity" };
    static readonly string[] DeviceScenes = { "Assets/Scenes/Device.unity" };

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

        Build(DevScenes, "Builds/ARAttention-dev.apk");
    }

    /// <summary>真机准备（不打包）：项目配置 + Device 场景生成。先跑它自检，再跑 BuildDevice。</summary>
    public static void PrepareDevice()
    {
        try
        {
            ConfigureAndroidTools();
            MRProjectSetup.Apply();
            XRRuntimeSetup.Apply();
            DeviceSceneCreator.CreateDeviceScene();
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Build] 真机准备失败：{e}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log("[Build] 真机配置与场景准备完毕（未打包）");
        EditorApplication.Exit(0);
    }

    /// <summary>打真机 MR APK：项目配置 + Device 场景 + 构建。</summary>
    public static void BuildDevice()
    {
        ConfigureAndroidTools();
        try
        {
            MRProjectSetup.Apply();
            XRRuntimeSetup.Apply();
            DeviceSceneCreator.CreateDeviceScene();
            Debug.Log("[Build] Device 场景已重建");
        }
        catch (System.Exception e)
        {
            // 场景/配置失败就别浪费一次 20 分钟的打包了
            Debug.LogError($"[Build] Device 准备失败：{e}");
            EditorApplication.Exit(1);
            return;
        }

        Build(DeviceScenes, "Builds/ARAttention-device.apk");
    }

    static void Build(string[] scenes, string output)
    {
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
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
