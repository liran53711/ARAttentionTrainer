// 开发工具：打印 CH_Sprite 的动画片段时长（排查"开场打招呼动画太长"）。
// 用法：-executeMethod AnimClipProbe.Run
using UnityEditor;
using UnityEngine;

public static class AnimClipProbe
{
    public static void Run()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/IP/CH_Sprite.fbx");
        foreach (var a in assets)
            if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                Debug.Log($"[Clip] {c.name} 长度={c.length:F2}s 循环={c.isLooping}");
        EditorApplication.Exit(0);
    }
}
