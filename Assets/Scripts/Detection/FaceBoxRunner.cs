// Assets/Scripts/Detection/FaceBoxRunner.cs
// BlazeFace 人脸检测（PaddleDetection blazeface_1000e → ONNX, 871KB）。
// 输入 1×3×640×640（RGB 0..1，归一化已烘焙进图内，C# 端零预处理）。
// 输出三个：reg [1,22400,4]（回归量）、logits [1,22400,2]（脸/背景）、anchors [22400,4]（常量，读一次缓存）。
// 解码规格（与 Python 参考 ref_decode.py 完全一致，已在真实人脸照片上验证）：
//   softmax(logits)[脸] > 阈值 → SSDBox 解码（方差 0.1,0.1,0.2,0.2）→ 归一化 xyxy（左上原点）。
// 用途（用户定调 2026-10-13）：老师人脸位于视野中心保持 20s → 精灵跳舞庆典。
// 节流 ~3Hz（与人体检测独立）；只产数据，消费在 M3DebugDriver。
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

public class FaceBoxRunner : MonoBehaviour
{
    [Header("引用")]
    public FrameSourceBehaviour frameSource;
    public ExperienceSettings settings;

    /// <summary>最新一次人脸框（归一化 xyxy 转 Rect，左上原点；按分数降序，最多 16 个）。</summary>
    public List<DetectionResult> LatestFaces { get; private set; } = new();

    /// <summary>结果版本号：每完成一次推理+解码自增。</summary>
    public int Version { get; private set; }

    /// <summary>最近一次推理+解码耗时（毫秒）。</summary>
    public float LastMs { get; private set; }

    /// <summary>是否已产出过至少一帧结果。</summary>
    public bool HasRun { get; private set; }

    Worker _worker;
    Tensor<float> _input;
    Vector2Int _inputSize;
    float[] _anchors;              // [22400×4] 常量锚点（初始化时读一次）
    const int AnchorCount = 22400;
    const int MaxFaces = 16;
    int _runs;                     // 推理次数（心跳节拍）

    // SSDBox 解码常量（PaddleDetection blazeface_1000e 训练同款）
    const float V0 = 0.1f, V1 = 0.1f, V2 = 0.2f, V3 = 0.2f;

    void Awake()
    {
        if (frameSource == null || settings == null || settings.faceModelAsset == null)
        {
            Debug.LogWarning("[FaceBox] 缺少帧源/设置/模型，已禁用（不影响其他功能）");
            enabled = false;
            return;
        }

        var model = ModelLoader.Load(settings.faceModelAsset);
        var inputShape = model.inputs[0].shape;
        _inputSize = new Vector2Int(inputShape.Get(2), inputShape.Get(3));
        _worker = new Worker(model, BackendType.CPU);
        _input = new Tensor<float>(new TensorShape(1, 3, _inputSize.x, _inputSize.y));

        // 空跑一次：编译算子 + 读回常量锚点（缓存，之后每帧不再读）
        _worker.Schedule(_input);
        var anchorTensor = _worker.PeekOutput(2) as Tensor<float>;
        if (anchorTensor != null)
        {
            _anchors = anchorTensor.DownloadToArray();
            Debug.Log($"[FaceBox] 锚点已缓存 {_anchors.Length / 4} 个");
        }
        else
        {
            Debug.LogError("[FaceBox] 读不到锚点输出（模型输出顺序应为 reg/cls/anchors），已禁用");
            enabled = false;
            return;
        }
        // 清零张量，避免残留脏数据参与首次真实推理
        for (int i = 0; i < _input.count; i++) _input[i] = 0f;

        Debug.Log($"[FaceBox] BlazeFace 就绪（输入 {_inputSize.x}x{_inputSize.y}，CPU，节流 {settings.faceDetectionInterval:F2}s）");
        StartCoroutine(Loop());
    }

    IEnumerator Loop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, settings != null ? settings.faceDetectionInterval : 0.33f));
            if (frameSource == null || !frameSource.IsReady) continue;
            if (!frameSource.TryGetFrame(out var frame) || !frame.valid || frame.texture == null) continue;
            yield return RunOnce(frame);
        }
    }

    IEnumerator RunOnce(CameraFrame frame)
    {
        float t0 = Time.realtimeSinceStartup;

        // 纹理 → 张量（RGB 0..1；归一化在图内完成——与 Python 验证流程一致）
        var textureTransform = new TextureTransform();
        TextureConverter.ToTensor(frame.texture, _input, textureTransform);
        _worker.Schedule(_input);

        var regAwaiter = (_worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
        var clsAwaiter = (_worker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
        while (!regAwaiter.IsCompleted || !clsAwaiter.IsCompleted) yield return null;

        using var reg = regAwaiter.GetResult();
        using var cls = clsAwaiter.GetResult();

        float thr = settings != null ? settings.faceConfThreshold : 0.6f;
        LatestFaces = Decode(reg.DownloadToArray(), cls.DownloadToArray(), _anchors,
                             AnchorCount, thr, settings != null ? settings.faceMinBoxWidth : 0.03f);
        Version++;
        HasRun = true;
        LastMs = (Time.realtimeSinceStartup - t0) * 1000f;

        // 运行期心跳（约 3.3s 一条；2026-10-13：真机排查需要——"没检出脸"与"组件没跑"必须可区分）
        if (++_runs % 10 == 0)
            Debug.Log($"[FaceBox] {LastMs:F0}ms 检出 {LatestFaces.Count} 张脸" +
                      (LatestFaces.Count > 0
                          ? $" 最高分 {LatestFaces[0].confidence:F2} @({LatestFaces[0].normalizedBox.x + LatestFaces[0].normalizedBox.width * 0.5f:F2},{LatestFaces[0].normalizedBox.y + LatestFaces[0].normalizedBox.height * 0.5f:F2})"
                          : ""));
    }

    /// <summary>
    /// 纯函数解码（EditMode 测试覆盖）：logits 过 softmax → 阈值 → SSDBox 解码 → 分数降序截断。
    /// </summary>
    public static List<DetectionResult> Decode(float[] reg, float[] cls, float[] anchors,
                                               int count, float threshold, float minBoxWidth)
    {
        var result = new List<DetectionResult>();
        count = Mathf.Min(count, Mathf.Min(reg.Length / 4, Mathf.Min(cls.Length / 2, anchors.Length / 4)));
        for (int i = 0; i < count; i++)
        {
            int ci = i * 2;
            float l0 = cls[ci], l1 = cls[ci + 1];
            float m = Mathf.Max(l0, l1);
            float e0 = Mathf.Exp(l0 - m), e1 = Mathf.Exp(l1 - m);
            float p1 = e1 / (e0 + e1);                 // 脸类 softmax 概率
            if (p1 < threshold) continue;

            int ri = i * 4, ai = i * 4;
            float a0 = anchors[ai], a1 = anchors[ai + 1], a2 = anchors[ai + 2], a3 = anchors[ai + 3];
            float pbW = a2 - a0, pbH = a3 - a1;
            float pbX = a0 + pbW * 0.5f, pbY = a1 + pbH * 0.5f;
            float ox = pbX + reg[ri] * pbW * V0;
            float oy = pbY + reg[ri + 1] * pbH * V1;
            float ow = Mathf.Exp(reg[ri + 2] * V2) * pbW;
            float oh = Mathf.Exp(reg[ri + 3] * V3) * pbH;

            float x1 = ox - ow * 0.5f, y1 = oy - oh * 0.5f;
            float w = ow, h = oh;
            if (w < minBoxWidth || h < minBoxWidth) continue;
            result.Add(new DetectionResult
            {
                normalizedBox = new Rect(x1, y1, w, h),
                confidence = p1,
            });
        }
        result.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        if (result.Count > MaxFaces) result.RemoveRange(MaxFaces, result.Count - MaxFaces);
        return result;
    }

    void OnDestroy()
    {
        if (_worker == null) return;
        _worker.PeekOutput(0)?.CompleteAllPendingOperations();
        _worker.PeekOutput(1)?.CompleteAllPendingOperations();
        _worker.Dispose();
        _input?.Dispose();
    }
}
