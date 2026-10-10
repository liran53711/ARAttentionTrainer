// Assets/Scripts/Detection/SelfieSegRunner.cs
// MediaPipe Selfie Segmentation（ONNX: 输入 1×3×144×256，输出 1×1×144×256 人体 alpha 掩码）。
// 用途（用户定调 2026-10-10）：让精灵与真实人体/人脸轮廓"贴合"互动——精灵沿真实轮廓探出/躲藏。
// 策略：轮廓比人脸位置变化慢 → 节流 0.5s 一帧（约 2Hz），省 CPU 且够用。
// 本组件只产数据（掩码统计 + 轮廓边缘查询）；视觉消费在 IPWorldView。
using System.Collections;
using Unity.InferenceEngine;
using UnityEngine;

public class SelfieSegRunner : MonoBehaviour
{
    [Header("引用")]
    public FrameSourceBehaviour frameSource;
    public ModelAsset modelAsset;   // Assets/Models/selfie_seg_landscape.onnx

    [Header("参数")]
    [Tooltip("推理间隔（秒）")]
    public float intervalSeconds = 0.5f;
    [Tooltip("边缘判定阈值（掩码值）")]
    public float edgeThreshold = 0.5f;

    /// <summary>最近一次推理+回读耗时（毫秒）。</summary>
    public float LastMs { get; private set; }
    /// <summary>掩码覆盖率 [0,1]（人体占画面比例）。</summary>
    public float Coverage { get; private set; }
    /// <summary>掩码峰值（诊断用：正常有人时应接近 1）。</summary>
    public float MaxValue { get; private set; }
    /// <summary>掩码版本号：每完成一帧自增（消费方用于"仅在新掩码时更新"）。</summary>
    public int Version { get; private set; }
    /// <summary>人体掩码纹理（RFloat，W×H；供 MaskOccluder 的擦除着色器采样）。</summary>
    public Texture2D MaskTexture { get; private set; }
    /// <summary>是否已产出过至少一帧有效掩码。</summary>
    public bool HasMask { get; private set; }
    public int MaskWidth => W;
    public int MaskHeight => H;

    const int W = 256, H = 144;
    Worker _worker;
    Tensor<float> _input;
    readonly float[] _mask = new float[H * W];
    readonly byte[] _maskBytes = new byte[H * W]; // R8 纹理上传（移动 GPU 上 R8 必然支持线性滤波；RFloat 常不支持）
    int _runs;

    void Awake()
    {
        if (modelAsset == null || frameSource == null)
        {
            Debug.LogWarning("[Seg] 缺少模型或帧源，已禁用（不影响其他功能）");
            enabled = false;
            return;
        }
        var model = ModelLoader.Load(modelAsset);
        _worker = new Worker(model, BackendType.CPU);
        _input = new Tensor<float>(new TensorShape(1, 3, H, W));

        MaskTexture = new Texture2D(W, H, TextureFormat.R8, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Debug.Log($"[Seg] SelfieSeg 模型就绪（输入 {W}x{H}，CPU）");
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervalSeconds);
            if (frameSource == null || !frameSource.IsReady) continue;
            if (!frameSource.TryGetFrame(out var frame) || !frame.valid || frame.texture == null) continue;
            yield return RunOnce(frame);
        }
    }

    IEnumerator RunOnce(CameraFrame frame)
    {
        float t0 = Time.realtimeSinceStartup;

        var textureTransform = new TextureTransform();
        TextureConverter.ToTensor(frame.texture, _input, textureTransform);
        _worker.Schedule(_input);

        var await = (_worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
        while (!await.IsCompleted) yield return null;

        using var outTensor = await.GetResult();
        var arr = outTensor.DownloadToArray();
        float sum = 0f, maxV = 0f;
        int n = Mathf.Min(arr.Length, _mask.Length);
        for (int i = 0; i < n; i++)
        {
            float v = arr[i];
            _mask[i] = v;
            _maskBytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            sum += v;
            if (v > maxV) maxV = v;
        }
        Coverage = n > 0 ? sum / n : 0f;
        MaxValue = maxV;
        LastMs = (Time.realtimeSinceStartup - t0) * 1000f;
        Version++;

        MaskTexture.SetPixelData(_maskBytes, 0);
        MaskTexture.Apply(false);
        HasMask = true;

        // 每 4 帧（约 2 秒）打一条诊断
        if (++_runs % 4 == 0)
        {
            bool hasL = TryGetEdge(0.4f, 0f, 0.5f, true, out var l);
            bool hasR = TryGetEdge(0.4f, 0.5f, 1f, false, out var r);
            Debug.Log($"[Seg] {LastMs:F0}ms 覆盖 {Coverage:P0} 峰值 {MaxValue:F2} | 左缘 {(hasL ? l.x.ToString("F2") : "--")} 右缘 {(hasR ? r.x.ToString("F2") : "--")}");
        }
    }

    /// <summary>
    /// 在掩码上找"某高度带内、指定侧的轮廓边缘点"（归一化坐标，左上原点，与检测框一致）。
    /// 例：找脸部高度附近人体左侧轮廓 → 精灵从真实身体左缘探出。
    /// </summary>
    public bool TryGetEdge(float y01, float xMin01, float xMax01, bool leftSide, out Vector2 edge01)
        => TryGetEdgeStatic(_mask, W, H, y01, xMin01, xMax01, leftSide, edgeThreshold, out edge01);

    /// <summary>纯函数版（单元测试覆盖）：在 宽w×高h 的掩码数组上找边缘。</summary>
    public static bool TryGetEdgeStatic(float[] mask, int w, int h,
        float y01, float xMin01, float xMax01, bool leftSide, float threshold, out Vector2 edge01)
    {
        edge01 = default;

        int yc = Mathf.Clamp(Mathf.RoundToInt(y01 * h), 0, h - 1);
        int band = Mathf.Max(1, Mathf.RoundToInt(0.035f * h)); // 上下各扫若干行，取最外侧
        int x0 = Mathf.Clamp(Mathf.FloorToInt(xMin01 * w), 0, w - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(xMax01 * w), 0, w - 1);
        if (x1 < x0) return false;

        int bestX = -1, bestY = -1;
        for (int y = Mathf.Max(0, yc - band); y <= Mathf.Min(h - 1, yc + band); y++)
        {
            int rowBase = y * w;
            if (leftSide)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (mask[rowBase + x] >= threshold)
                    {
                        if (bestX < 0 || x < bestX) { bestX = x; bestY = y; }
                        break;
                    }
                }
            }
            else
            {
                for (int x = x1; x >= x0; x--)
                {
                    if (mask[rowBase + x] >= threshold)
                    {
                        if (bestX < 0 || x > bestX) { bestX = x; bestY = y; }
                        break;
                    }
                }
            }
        }
        if (bestX < 0) return false;

        edge01 = new Vector2((bestX + 0.5f) / w, (bestY + 0.5f) / h);
        return true;
    }

    void OnDestroy()
    {
        if (MaskTexture != null) Destroy(MaskTexture);
        if (_worker == null) return;
        _worker.PeekOutput(0)?.CompleteAllPendingOperations();
        _worker.Dispose();
        _input?.Dispose();
    }
}
