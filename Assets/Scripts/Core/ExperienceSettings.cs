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

    [Tooltip("注视判定：人脸检测必须比这个时间更新（脸不在画面里=没在看，而不是记住10秒前）")]
    public float gazeFreshnessSeconds = 0.4f;

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

    [Header("干预档位（设计文档 §3.3；MVP 手动选档）")]
    [Tooltip("0=强 1=中 2=弱（真机按手柄 A 键循环切换；电脑按键盘 1/2/3）")]
    [Range(0, 2)] public int interventionLevel = 1;

    [Tooltip("精灵尺寸系数（由档位设置）")]
    public float ipSizeFactor = 1f;

    [Tooltip("音效强弱系数（由档位设置）")]
    public float cueVolumeScale = 1f;

    [Header("麦克风降级（设计文档 §4.4）")]
    [Tooltip("麦克风不可用时：注视累计满这么久直接给反馈（替代'讲话结束'条件）")]
    public float micFallbackFeedbackGazeSeconds = 5f;

    [Header("开场吸引（用户定调 2026-10-10）")]
    [Tooltip("应用启动即进入引导（精灵立即出现、带孩子四处找老师），不等讲话；" +
             "持续到首次检测到目标或超过此时长（秒）。0=关闭")]
    public float startupAttractionSeconds = 25f;

    public static readonly string[] LevelNames = { "强", "中", "弱" };
    public string CurrentLevelName => LevelNames[Mathf.Clamp(interventionLevel, 0, 2)];

    /// <summary>
    /// 应用干预档位（设计 §3.3 表）：强/中/弱 = 提示更多更快更强 / 平衡 / 提示更少要求更高。
    /// 会写回 gazeStartSeconds / reGuideCooldownSeconds / peekIntervalSeconds / ipSizeFactor / cueVolumeScale；
    /// 驱动层在切换后需把新值同步给运行时模块（GazeLogic / 状态机 / 音效）。
    /// </summary>
    public void ApplyLevel(int level)
    {
        interventionLevel = Mathf.Clamp(level, 0, 2);
        switch (interventionLevel)
        {
            case 0: // 强：一见注视就交付、提示更频繁
                gazeStartSeconds = 0.5f;
                reGuideCooldownSeconds = 3f;
                peekIntervalSeconds = 8f;
                ipSizeFactor = 1.25f;
                cueVolumeScale = 1.0f;
                break;
            case 1: // 中：平衡（默认）
                gazeStartSeconds = 0.8f;
                reGuideCooldownSeconds = 5f;
                peekIntervalSeconds = 12f;
                ipSizeFactor = 1.0f;
                cueVolumeScale = 0.6f;
                break;
            default: // 弱：提示更少、要求更高、频率更低
                gazeStartSeconds = 1.2f;
                reGuideCooldownSeconds = 8f;
                peekIntervalSeconds = 20f;
                ipSizeFactor = 0.8f;
                cueVolumeScale = 0.15f;
                break;
        }
    }

    /// <summary>会话数据要的可复现参数快照（JSON 片段；用不变文化保证小数点正确）。</summary>
    public string SnapshotJson()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "{" +
               $"\"level\":{interventionLevel},\"levelName\":\"{CurrentLevelName}\"," +
               $"\"gazeStart\":{gazeStartSeconds.ToString(inv)},\"gazeSustain\":{gazeSustainSeconds.ToString(inv)}," +
               $"\"reGuideCooldown\":{reGuideCooldownSeconds.ToString(inv)},\"peekInterval\":{peekIntervalSeconds.ToString(inv)}," +
               $"\"ipSizeFactor\":{ipSizeFactor.ToString(inv)},\"cueVolumeScale\":{cueVolumeScale.ToString(inv)}," +
               $"\"detectionStride\":{detectionStride},\"confThreshold\":{detectionConfThreshold.ToString(inv)}" +
               "}";
    }
}
