// Assets/Scripts/Editor/FlipbookValidator.cs
// 动效资源验收工具（2026-10-20）：检查 Resources/Effects/flipbooks/ 下的序列帧图集是否符合规格。
// 菜单：ARAttention → Effects → Validate Flipbooks
// 检查项：index.json 可解析 / 图集存在 / 宽高能被帧数整除 / 单帧为正方形 / 图集尺寸合理 / fps 合理
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FlipbookValidator
{
    const string Root = "Assets/Resources/Effects/flipbooks/";

    [MenuItem("ARAttention/Effects/Validate Flipbooks")]
    public static void Validate()
    {
        var idxAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "index.json");
        if (idxAsset == null)
        {
            Debug.LogError($"[Flipbook检查] 找不到 {Root}index.json");
            return;
        }
        FlipbookLibrary.Index idx;
        try { idx = JsonUtility.FromJson<FlipbookLibrary.Index>(idxAsset.text); }
        catch (System.Exception e) { Debug.LogError("[Flipbook检查] index.json 解析失败：" + e.Message); return; }
        if (idx?.entries == null || idx.entries.Length == 0)
        {
            Debug.LogWarning("[Flipbook检查] index.json 没有条目");
            return;
        }

        int ok = 0, fail = 0, missing = 0;
        foreach (var e in idx.entries)
        {
            if (e == null || string.IsNullOrEmpty(e.name)) continue;
            int frames = Mathf.Max(1, e.frames);
            int rows = Mathf.Max(1, e.rows);
            string path = Root + e.name + ".png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                Debug.Log($"[Flipbook检查] ○ {e.name}：图片未交付（运行时会回退静态贴图）");
                missing++;
                continue;
            }
            var problems = "";
            if (frames % rows != 0) problems += $"\n    · frames({frames}) 不能被 rows({rows}) 整除";
            int cols = frames / rows;
            if (tex.width % cols != 0) problems += $"\n    · 图集宽 {tex.width} 不能被列数 {cols} 整除（应为 {cols}×单帧宽）";
            if (tex.height % rows != 0) problems += $"\n    · 图集高 {tex.height} 不能被行数 {rows} 整除";
            if (problems == "")
            {
                int fw = tex.width / cols, fh = tex.height / rows;
                if (fw != fh) problems += $"\n    · 单帧不是正方形：{fw}×{fh}";
                if (fw < 64) problems += $"\n    · 单帧过小（{fw}px），泡泡建议 512、光效建议 256";
                if (tex.width > 8192) problems += $"\n    · 图集总宽 {tex.width} 过大（建议 ≤4096）";
            }
            if (e.fps < 4 || e.fps > 60) problems += $"\n    · fps({e.fps}) 不在合理范围 4~60";

            if (problems == "")
            {
                Debug.Log($"[Flipbook检查] ✅ {e.name}：{frames}帧 {cols}×{rows} 网格，单帧 {tex.width / cols}×{tex.height / rows}，{e.fps}fps {(e.loop ? "循环" : "单次")}");
                ok++;
            }
            else
            {
                Debug.LogError($"[Flipbook检查] ❌ {e.name}（{path}）：{problems}");
                fail++;
            }
        }
        Debug.Log(fail == 0
            ? $"[Flipbook检查] 完成：{ok} 个通过、{missing} 个未交付。{(missing > 0 ? "未交付项运行时会回退静态贴图。" : "全部动效就位！")}"
            : $"[Flipbook检查] 完成：{ok} 个通过、{fail} 个有问题（见上方红色错误）");
    }
}
