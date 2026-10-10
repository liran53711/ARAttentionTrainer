// Assets/Scripts/Editor/MRProjectSetup.cs
// 真机 MR 项目配置（对应 Meta OVRProjectSetup 工具的 Required 项，批量模式下手动执行）。
// 依据：包内 Editor/OVRProjectSetup/Tasks/Implementations/OVRProjectSetupPassthrough.cs（工具规则原文）
//   · 开启透视支持 + 前置相机访问 → 构建时 OVRManifestPreprocessor 自动生成：
//     <uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />
//   · ARM64（透视能力硬性要求）
//   · 系统加载屏背景 = ContextualPassthrough、关闭 VR 闪屏（MR 应用推荐，避免黑屏撕裂感）
// 用法：菜单 ARAttention > Device > Apply MR Project Setup；或批量模式 BuildScript.PrepareDevice
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MRProjectSetup
{
    [MenuItem("ARAttention/Device/Apply MR Project Setup")]
    public static void Apply()
    {
        // 1) 项目配置：透视 + 相机访问（这两个开关是 manifest 权限的生成依据）
        var cfg = OVRProjectConfig.CachedProjectConfig;
        cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
        cfg.isPassthroughCameraAccessEnabled = true;
        cfg.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
        OVRProjectConfig.CommitProjectConfig(cfg);
        Debug.Log("[MRSetup] 项目配置：透视=Supported，相机访问=开，加载屏=ContextualPassthrough");

        // 2) ARM64（Quest 唯一支持的架构；透视能力要求）
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        Debug.Log("[MRSetup] 目标架构：ARM64");

        // 2.5) 空间锚点/Scene 支持（MRUK 空间数据所需；生成旧命名空间权限 + 运行时能力声明）
        //     新命名空间 horizonos.permission.USE_ANCHOR_API 由 AndroidManifestPatcher 构建后补齐
        cfg.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
        cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
        OVRProjectConfig.CommitProjectConfig(cfg);
        Debug.Log("[MRSetup] 空间支持：Anchor=Enabled, Scene=Supported");

        // 2.6) 立体渲染 = SinglePass/Multiview（Depth API 前置条件，也是 Quest 性能最佳实践）
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.SinglePass;
        Debug.Log("[MRSetup] 立体渲染：SinglePass（Multiview）");

        // 3) MR 推荐：关闭启动闪屏（否则启动画面黑底与透视背景不一致）
        PlayerSettings.virtualRealitySplashScreen = null;
        PlayerSettings.SplashScreen.show = false;
        Debug.Log("[MRSetup] 已关闭 VR 闪屏/启动画面");

        // 4) URP：关闭 HDR —— Meta 官方要求（HDR 眼缓冲无有效 alpha 位，透视会黑屏。
        //    官方推荐：HDR 关、Post-processing 关、Intermediate Texture=Auto）
        ApplyUrpHdrFix(GraphicsSettings.defaultRenderPipeline);
        ApplyUrpHdrFix(AssetDatabase.LoadAssetAtPath<Object>("Assets/Settings/PC_RPAsset.asset"));
        ApplyUrpHdrFix(AssetDatabase.LoadAssetAtPath<Object>("Assets/Settings/Mobile_RPAsset.asset"));
    }

    static void ApplyUrpHdrFix(Object rp)
    {
        if (rp == null) return;

        var so = new SerializedObject(rp);
        var hdr = so.FindProperty("m_SupportsHDR");
        if (hdr == null) return; // 不是 URP 资产

        if (hdr.boolValue)
        {
            hdr.boolValue = false;
            so.ApplyModifiedProperties();
            Debug.Log($"[MRSetup] 已关闭 HDR（透视要求）：{rp.name}");
        }
        else
        {
            Debug.Log($"[MRSetup] HDR 已是关闭状态：{rp.name}");
        }
    }
}
