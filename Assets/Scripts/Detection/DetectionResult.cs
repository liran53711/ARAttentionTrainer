// Assets/Scripts/Detection/DetectionResult.cs
// 检测结果的统一结构。坐标是归一化值（x,y,w,h ∈ [0,1]），
// 与具体模型（YOLOv9/BlazeFace/...）的输出格式解耦——换模型不用改下游代码。
using UnityEngine;

/// <summary>一个人脸/头部的检测结果（归一化坐标）。</summary>
public struct DetectionResult
{
    /// <summary>归一化框：x,y = 左上角坐标，width,height = 宽高（都除以图像宽高）</summary>
    public Rect normalizedBox;
    /// <summary>置信度 [0,1]</summary>
    public float confidence;

    public override string ToString()
        => $"[{normalizedBox.x:F3},{normalizedBox.y:F3} {normalizedBox.width:F3}x{normalizedBox.height:F3}] conf={confidence:F3}";
}
