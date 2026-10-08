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
    [Tooltip("置信度阈值：低于此值的检测框丢弃（官方样例推荐 0.23，起步取 0.35，可按效果调）")]
    public float detectionConfThreshold = 0.35f;

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

    [Header("M3 交互参数（设计文档 §3.2/§3.3）")]
    [Tooltip("开始注视：连续看这么久才触发'隐藏'")]
    public float gazeStartSeconds = 0.8f;

    [Tooltip("注视达成：累计到这么久才算'完成注视'（可给反馈）")]
    public float gazeSustainSeconds = 2.0f;

    [Tooltip("注视判定：目标离屏幕中心多近算'在看'（视口半径）")]
    public float gazeViewportRadius = 0.28f;

    [Tooltip("重新引导冷却：两次引导出现的最小间隔")]
    public float reGuideCooldownSeconds = 3f;

    [Tooltip("反馈动画时长")]
    public float feedbackDurationSeconds = 2f;

    [Tooltip("老师没讲话这么久 → 回待机")]
    public float idleTimeoutSeconds = 10f;

    [Tooltip("训练日志采样间隔（秒）")]
    public float logSampleEverySeconds = 2f;

    [Header("隐藏期微提示（Peek）")]
    [Tooltip("讲话继续这么久，精灵探头一次（吸引持续注意力）")]
    public float peekIntervalSeconds = 10f;

    [Tooltip("探头动作持续时间（秒）")]
    public float peekDurationSeconds = 0.8f;
}
