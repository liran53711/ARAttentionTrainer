// Assets/Tests/EditMode/DetectionPostprocessTests.cs
// EditMode 单元测试：验证 NMS / IoU 的核心行为。
// 跑法：Unity 菜单 Window > General > Test Runner → EditMode 页签 → Run All
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class DetectionPostprocessTests
{
    // 快捷构造：D(x, y, w, h, confidence)
    static DetectionResult D(float x, float y, float w, float h, float c)
        => new DetectionResult { normalizedBox = new Rect(x, y, w, h), confidence = c };

    [Test]
    public void NMS_KeepsHighestConfidence_AmongOverlapping()
    {
        var input = new List<DetectionResult> {
            D(0.10f, 0.10f, 0.20f, 0.20f, 0.9f), // 高分
            D(0.11f, 0.11f, 0.20f, 0.20f, 0.7f), // 与上重叠 IoU≈0.82 → 应被抑制
            D(0.60f, 0.60f, 0.20f, 0.20f, 0.8f), // 不重叠 → 保留
        };
        var kept = DetectionPostprocess.NMS(input, confThreshold: 0.5f, iouThreshold: 0.5f);
        Assert.AreEqual(2, kept.Count);
        Assert.AreEqual(0.9f, kept[0].confidence, 1e-4f);
        Assert.AreEqual(0.8f, kept[1].confidence, 1e-4f);
    }

    [Test]
    public void NMS_FiltersBelowConfidenceThreshold()
    {
        var input = new List<DetectionResult> {
            D(0.10f, 0.10f, 0.20f, 0.20f, 0.3f), // 低于阈值 → 应被过滤
            D(0.60f, 0.60f, 0.20f, 0.20f, 0.8f), // 保留
        };
        var kept = DetectionPostprocess.NMS(input, confThreshold: 0.5f, iouThreshold: 0.5f);
        Assert.AreEqual(1, kept.Count);
        Assert.AreEqual(0.8f, kept[0].confidence, 1e-4f);
    }

    [Test]
    public void IoU_IdenticalBoxes_IsOne()
    {
        float iou = DetectionPostprocess.IoU(new Rect(0f, 0f, 0.2f, 0.2f), new Rect(0f, 0f, 0.2f, 0.2f));
        Assert.AreEqual(1f, iou, 1e-4f);
    }
}
