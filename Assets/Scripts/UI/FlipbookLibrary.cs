// Assets/Scripts/UI/FlipbookLibrary.cs
// 动效资源库：按约定从 Resources 加载序列帧图集 + 索引元数据。
// 资源位置（组员交付放这里）：
//   Assets/Resources/Effects/flipbooks/<name>.png    —— 序列帧图集（横向一行或网格）
//   Assets/Resources/Effects/flipbooks/index.json    —— 帧数/fps/循环/行数元数据
// 运行时若某条目资源缺失 → TryGet 返回 false，调用方自动回退到旧静态贴图（平滑过渡，不炸）。
using System;
using System.Collections.Generic;
using UnityEngine;

public static class FlipbookLibrary
{
    public const string Root = "Effects/flipbooks/";

    [Serializable]
    public class Entry
    {
        public string name;     // 效果名（cheer/hum/curious/halo/ring/...）
        public int frames = 8;  // 总帧数
        public int rows = 1;    // 行数（默认一行横向）
        public float fps = 12f;
        public bool loop = true;
    }

    [Serializable]
    public class Index
    {
        public Entry[] entries = Array.Empty<Entry>();
    }

    public class Info
    {
        public string name;
        public Texture2D atlas;
        public int cols;
        public int rows;
        public float fps;
        public bool loop;
    }

    static Dictionary<string, Info> _cache;
    static bool _loaded;

    /// <summary>按效果名查询（例如 "cheer"、"hum"、"halo"、"ring"）。</summary>
    public static bool TryGet(string name, out Info info)
    {
        EnsureLoad();
        return _cache.TryGetValue(name ?? "", out info);
    }

    /// <summary>重新加载（编辑器预览/资源更新后调用）。</summary>
    public static void Invalidate()
    {
        _cache = null;
        _loaded = false;
    }

    static void EnsureLoad()
    {
        if (_loaded) return;
        _loaded = true;
        _cache = new Dictionary<string, Info>();

        var idxAsset = Resources.Load<TextAsset>(Root + "index");
        if (idxAsset == null)
        {
            Debug.Log("[Flipbook] 无 index.json —— 动效资源未接入，全部回退静态贴图");
            return;
        }
        Index idx;
        try { idx = JsonUtility.FromJson<Index>(idxAsset.text); }
        catch (Exception e) { Debug.LogWarning("[Flipbook] index.json 解析失败：" + e.Message); return; }
        if (idx?.entries == null) return;

        int missing = 0;
        foreach (var e in idx.entries)
        {
            if (e == null || string.IsNullOrEmpty(e.name)) continue;
            var tex = Resources.Load<Texture2D>(Root + e.name);
            if (tex == null)
            {
                missing++;
                continue;
            }
            int frames = Mathf.Max(1, e.frames);
            int rows = Mathf.Max(1, e.rows);
            if (frames % rows != 0)
            {
                Debug.LogWarning($"[Flipbook] {e.name}: frames({frames}) 不能被 rows({rows}) 整除，已跳过");
                continue;
            }
            _cache[e.name.ToLowerInvariant()] = new Info
            {
                name = e.name,
                atlas = tex,
                cols = frames / rows,
                rows = rows,
                fps = Mathf.Max(1f, e.fps),
                loop = e.loop,
            };
        }
        Debug.Log($"[Flipbook] 已加载 {_cache.Count} 个动效" + (missing > 0 ? $"（{missing} 个条目缺图集，按静态回退）" : ""));
    }
}
