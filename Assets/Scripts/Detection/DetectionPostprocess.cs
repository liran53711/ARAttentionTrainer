// Assets/Scripts/Detection/DetectionPostprocess.cs
// 检测后处理：置信度过滤 + NMS（非极大值抑制）。
// NMS 干什么：同一个脸常被模型输出好几个互相重叠的框，只保留其中最可信的一个。
// 本文件是纯 C# 逻辑，由 EditMode 单元测试覆盖（Assets/Tests/EditMode/）。
using System.Collections.Generic;
using UnityEngine;

public static class DetectionPostprocess
{
    /// <summary>先按置信度阈值过滤，再做 NMS。</summary>
    /// <param name="boxes">候选框列表（不修改原列表）</param>
    /// <param name="confThreshold">置信度阈值：低于此值的框直接丢弃</param>
    /// <param name="iouThreshold">IoU 阈值：与高分框重叠超过此值的低分框被抑制</param>
    /// <returns>保留的框，按置信度从高到低排序</returns>
    public static List<DetectionResult> NMS(List<DetectionResult> boxes, float confThreshold, float iouThreshold)
    {
        // 1) 置信度过滤
        var candidates = new List<DetectionResult>();
        foreach (var b in boxes)
            if (b.confidence >= confThreshold)
                candidates.Add(b);

        // 2) 按置信度从高到低排序
        candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));

        // 3) 贪心抑制：每个未被抑制的框，把与它重叠过多的后续框全部标记删除
        var kept = new List<DetectionResult>();
        var suppressed = new bool[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            if (suppressed[i]) continue;
            kept.Add(candidates[i]);

            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (suppressed[j]) continue;
                if (IoU(candidates[i].normalizedBox, candidates[j].normalizedBox) > iouThreshold)
                    suppressed[j] = true;
            }
        }
        return kept;
    }

    /// <summary>交并比（IoU）：交集面积 / 并集面积。完全重合 = 1，不相交 = 0。</summary>
    public static float IoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.xMin, b.xMin);
        float y1 = Mathf.Max(a.yMin, b.yMin);
        float x2 = Mathf.Min(a.xMax, b.xMax);
        float y2 = Mathf.Min(a.yMax, b.yMax);

        float interW = Mathf.Max(0f, x2 - x1);
        float interH = Mathf.Max(0f, y2 - y1);
        float inter = interW * interH;

        float union = a.width * a.height + b.width * b.height - inter;
        return union <= 0f ? 0f : inter / union;
    }
}
