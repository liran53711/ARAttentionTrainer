// Assets/Scripts/Editor/XRRuntimeSetup.cs
// 真机 XR 运行时配置（对应 Meta OVRProjectSetup 的 XR Required 项与 OpenXR 功能勾选）。
// 依据：包内 OVRProjectSetupXRTasks.cs —— v74+ 官方要求 OpenXR 路线（弃用 OculusXR 插件）。
//   · Android 端加入 OpenXR Loader（= XR Plug-in Management UI 里勾选 OpenXR）
//   · 启用 OpenXR 功能：Meta XR Feature（SDK 提供 OVRPlugin-over-OpenXR）
//     与 Meta Quest Support（Unity 提供 Quest 构建配置）
// 用法：菜单 ARAttention > Device > Apply XR Runtime Setup；批量模式经 BuildScript 调用。
using System.Linq;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

public static class XRRuntimeSetup
{
    // 功能 ID 来源：MetaXRFeature.cs / MetaQuestFeature.cs 的公开 const（ID 是稳定标识符）
    const string MetaXRFeatureId = "com.meta.openxr.feature.metaxr";
    const string MetaQuestFeatureId = "com.unity.openxr.feature.metaquest";

    [MenuItem("ARAttention/Device/Apply XR Runtime Setup")]
    public static void Apply()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;

        // ── 1) XR Plug-in Management：确保 Android 设置存在 + 勾选 OpenXR Loader ──
        var settings = GetOrCreateXRGeneralSettings(group);
        var manager = settings.Manager;
        if (manager == null)
        {
            Debug.LogError("[XRSetup] XRGeneralSettings.Manager 为空——请检查 XR Plug-in Management 安装");
            return;
        }

        if (!manager.activeLoaders.OfType<OpenXRLoader>().Any())
        {
            var loader = EnsureLoaderAsset();
            if (manager.TryAddLoader(loader))
                Debug.Log("[XRSetup] OpenXR Loader 已加入 Android 的 XR 设置");
            else
                Debug.LogWarning("[XRSetup] TryAddLoader 返回 false——请检查 XR Plug-in Management");
        }
        else
        {
            Debug.Log("[XRSetup] OpenXR Loader 已在位");
        }
        settings.InitManagerOnStart = true; // = UI 上的 "Initialize XR on Startup"
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(manager);

        // ── 2) OpenXR 功能：Meta XR Feature + Meta Quest Support ──
        foreach (var id in new[] { MetaXRFeatureId, MetaQuestFeatureId })
        {
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
            if (feature == null)
            {
                Debug.LogWarning($"[XRSetup] 未找到功能 {id}（对应包是否已安装？）");
                continue;
            }
            feature.enabled = true;
            Debug.Log($"[XRSetup] 启用功能: {feature.GetType().Name} ({id})");
        }
        FeatureHelpers.RefreshFeatures(group);
        AssetDatabase.SaveAssets();
        Debug.Log("[XRSetup] XR 运行时配置完成");
    }

    /// <summary>复刻 OVRProjectSetupXRTasks.GetXRGeneralSettingsForBuildTarget：不存在就创建。</summary>
    static XRGeneralSettings GetOrCreateXRGeneralSettings(BuildTargetGroup group)
    {
        var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
        if (settings != null) return settings;

        var key = XRGeneralSettings.k_SettingsKey;
        EditorBuildSettings.TryGetConfigObject(key, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            EnsureFolder("Assets/XR");
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            AssetDatabase.SaveAssets();
            EditorBuildSettings.AddConfigObject(key, perTarget, true);
            Debug.Log("[XRSetup] 已创建 Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
        }
        if (!perTarget.HasManagerSettingsForBuildTarget(group))
        {
            perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            Debug.Log("[XRSetup] 已创建 Android 的 XR Manager 设置");
        }

        return XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
    }

    static OpenXRLoader EnsureLoaderAsset()
    {
        var existing = AssetDatabase.FindAssets("t:OpenXRLoader")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<OpenXRLoader>)
            .FirstOrDefault(l => l != null);
        if (existing != null) return existing;

        EnsureFolder("Assets/XR/Loaders");
        var loader = ScriptableObject.CreateInstance<OpenXRLoader>();
        AssetDatabase.CreateAsset(loader, "Assets/XR/Loaders/OpenXRLoader.asset");
        Debug.Log("[XRSetup] 已创建 Assets/XR/Loaders/OpenXRLoader.asset");
        return loader;
    }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var parent = parts[0];
        var full = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            full = full + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, parts[i]);
            parent = full;
        }
    }
}
