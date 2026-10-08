// Assets/Scripts/Core/ExperienceSettings.cs
// 所有可调参数的唯一入口（设计文档 §3.2 阈值 / §4 检测参数都在这里集中管理）。
// 创建配置资产：Project 窗口右键 → Create → ARAttention → ExperienceSettings
using UnityEngine;
using Unity.InferenceEngine;

[CreateAssetMenu(fileName = "ExperienceSettings", menuName = "ARAttention/ExperienceSettings")]
public class ExperienceSettings : ScriptableObject
{
    [Header("相机 / 检测")]
    [Tooltip("每 N 帧检测一次（隔帧推理省算力）")]
    public int detectionStride = 2;

    [Range(0f, 1f)]
    [Tooltip("置信度阈值：低于此值的检测框丢弃")]
    public float detectionConfThreshold = 0.5f;

    [Tooltip("PCA 请求分辨率（真机用）")]
    public int requestedCameraWidth = 1280;
    public int requestedCameraHeight = 960;

    [Header("模型")]
    [Tooltip("指向 yolov9sentis 模型资产（Assets/Models/ 下）")]
    public ModelAsset modelAsset;

    [Tooltip("GPU 后端在 Quest 上有卡顿报告，默认 CPU")]
    public bool useGpuBackend = false;

    [Header("调试")]
    public bool drawDetectionBoxes = true;
    public bool showFpsHud = true;
}
