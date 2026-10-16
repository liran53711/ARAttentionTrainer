// Assets/Tests/EditMode/FaceBoxDecodeTests.cs
// BlazeFace 解码（FaceBoxRunner.Decode）单元测试（2026-10-13）：
// 合成 reg/logits/anchors，验证 softmax 阈值 → SSDBox 解码 → 框宽过滤 → 分数降序。
// 解码公式与 Python 参考实现（ref_decode.py，真实人脸照片已验证）一致。
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class FaceBoxDecodeTests
{
    const float Thresh = 0.6f;
    const float MinW = 0.03f;

    static float[] Anchors(params float[][] boxes)
    {
        var a = new float[boxes.Length * 4];
        for (int i = 0; i < boxes.Length; i++)
            for (int j = 0; j < 4; j++)
                a[i * 4 + j] = boxes[i][j];
        return a;
    }

    [Test]
    public void ZeroRegressor_ReturnsAnchorBox_WhenScoreHigh()
    {
        // 锚点 (0.4,0.4)-(0.6,0.6)；回归量全 0 → 输出框 = 锚点本身
        var reg = new float[] { 0, 0, 0, 0 };
        var cls = new float[] { 0f, 10f };            // face 类 softmax ≈ 1
        var anchors = Anchors(new float[] { 0.4f, 0.4f, 0.6f, 0.6f });

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 1, Thresh, MinW);
        Assert.AreEqual(1, r.Count, "高分框应保留");
        Assert.AreEqual(0.4f, r[0].normalizedBox.x, 1e-4f);
        Assert.AreEqual(0.4f, r[0].normalizedBox.y, 1e-4f);
        Assert.AreEqual(0.2f, r[0].normalizedBox.width, 1e-4f);
        Assert.Greater(r[0].confidence, 0.99f);
    }

    [Test]
    public void PositiveXRegressor_ShiftsCenterRight()
    {
        // out_x = cx + reg_x * w * 0.1 = 0.5 + 0.5*0.2*0.1 = 0.51 → x1 = 0.41
        var reg = new float[] { 0.5f, 0, 0, 0 };
        var cls = new float[] { 0f, 10f };
        var anchors = Anchors(new float[] { 0.4f, 0.4f, 0.6f, 0.6f });

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 1, Thresh, MinW);
        Assert.AreEqual(1, r.Count);
        Assert.AreEqual(0.41f, r[0].normalizedBox.x, 1e-4f, "中心应右移 0.01");
    }

    [Test]
    public void LowFaceScore_FilteredOut()
    {
        var reg = new float[] { 0, 0, 0, 0 };
        var cls = new[] { 10f, 0f };                  // face 类 ≈ 0.00005 < 阈值
        var anchors = Anchors(new float[] { 0.4f, 0.4f, 0.6f, 0.6f });

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 1, Thresh, MinW);
        Assert.AreEqual(0, r.Count, "低分框应被过滤");
    }

    [Test]
    public void TinyBox_BelowMinWidth_FilteredOut()
    {
        var reg = new float[] { 0, 0, 0, 0 };
        var cls = new float[] { 0f, 10f };
        var anchors = Anchors(new float[] { 0.4f, 0.4f, 0.42f, 0.6f }); // 宽 0.02 < 0.03

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 1, Thresh, MinW);
        Assert.AreEqual(0, r.Count, "过小框（远距离误检）应被过滤");
    }

    [Test]
    public void MultipleCandidates_SortedByScoreDescending()
    {
        var reg = new float[8];                        // 两个框回归量全 0
        var cls = new float[] { 0f, 8f, 0f, 3f };      // 框0 分数高 > 框1
        var anchors = Anchors(
            new float[] { 0.3f, 0.3f, 0.5f, 0.5f },
            new float[] { 0.6f, 0.6f, 0.8f, 0.8f });

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 2, Thresh, MinW);
        Assert.AreEqual(2, r.Count);
        Assert.Greater(r[0].confidence, r[1].confidence, "应按分数降序");
        Assert.AreEqual(0.3f, r[0].normalizedBox.x, 1e-4f, "最高分应是框0");
    }

    [Test]
    public void CountClamped_ToAvailableData()
    {
        // 声明 5 个但只给 1 组数据 → 不越界（防御模型输出尺寸变化）
        var reg = new float[] { 0, 0, 0, 0 };
        var cls = new float[] { 0f, 10f };
        var anchors = Anchors(new float[] { 0.4f, 0.4f, 0.6f, 0.6f });

        var r = FaceBoxRunner.Decode(reg, cls, anchors, 5, Thresh, MinW);
        Assert.AreEqual(1, r.Count);
    }

    // ── 人脸-人体关联（2026-10-13 假脸过滤；M3DebugDriver.FaceBelongsToPerson）──
    // 人体框：x∈[0.4,0.7], y∈[0.2,0.7]（宽 0.3 高 0.5）——头部区域 = y < 0.2 + 0.5*0.55 = 0.475
    static readonly Rect Person = new Rect(0.4f, 0.2f, 0.3f, 0.5f);

    [Test]
    public void Face_OnUpperBody_Belongs()
    {
        Assert.IsTrue(M3DebugDriver.FaceBelongsToPerson(new Vector2(0.55f, 0.30f), Person), "框顶部的脸应属于该人体");
    }

    [Test]
    public void Face_OnLowerBody_NotBelongs()
    {
        Assert.IsFalse(M3DebugDriver.FaceBelongsToPerson(new Vector2(0.55f, 0.65f), Person), "框下部（腰部/胸前）的'脸'应被拒（假脸）");
    }

    [Test]
    public void Face_OutsideBox_NotBelongs()
    {
        Assert.IsFalse(M3DebugDriver.FaceBelongsToPerson(new Vector2(0.85f, 0.30f), Person), "框外的脸（海报等）应被拒");
    }

    [Test]
    public void Face_NearEdge_WithinMargin_Belongs()
    {
        Assert.IsTrue(M3DebugDriver.FaceBelongsToPerson(new Vector2(0.37f, 0.24f), Person), "检测抖动的边距内应容忍");
    }

    [Test]
    public void EmptyPersonBox_NotBelongs()
    {
        Assert.IsFalse(M3DebugDriver.FaceBelongsToPerson(new Vector2(0.5f, 0.3f), new Rect(0.5f, 0.3f, 0f, 0f)), "无效人体框应拒绝");
    }
}
