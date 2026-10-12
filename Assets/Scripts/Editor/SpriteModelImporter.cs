#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

// Optional importer preset. Install before importing CH_Sprite.fbx.
public sealed class SpriteModelImporter : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.EndsWith("/CH_Sprite.fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.importBlendShapes = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        // ★2026-10-12 工程实测（IpModelValidator 渲染验证）：Bake Axis Conversion=ON 时
        // 脸朝 -Z（+Z 侧渲染只见披风背影）；关闭后脸朝 +Z ✓。
        // 该 FBX 导出时已按 Unity 轴向转换，再 Bake 等于二次转换（这正是"180° 现象"的根因，
        // 按交付说明要求：修复导入配置，不加 180° 旋转补丁）。
        importer.bakeAxisConversion = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.optimizeGameObjects = false;
        importer.importCameras = false;
        importer.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.EndsWith("/CH_Sprite.fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            var pieces = clip.name.Split('|');
            clip.name = pieces[pieces.Length - 1];
            clip.loopTime = clip.name == "IdleFly" || clip.name == "GuideFly" || clip.name == "CompanionIdle";
            clip.loopPose = false;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }
}
#endif
