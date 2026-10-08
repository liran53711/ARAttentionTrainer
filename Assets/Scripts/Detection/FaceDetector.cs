// Assets/Scripts/Detection/FaceDetector.cs
// Sentis 推理管线（CPU 后端）：把相机/视频帧里的"人"检测出来。
// 实现参考官方样例 oculus-samples/Unity-PassthroughCameraApiSamples（MIT 许可）的
// SentisInferenceRunManager —— 三个输出（框/类别/分数）、异步回读、启动预加载。
// 模型是 COCO 通用 80 类 YOLOv9，本类只保留 classId==0（person）。
// 坐标系：框为输入图像的像素坐标（左上角原点），归一化后存入 DetectionResult。
using System.Collections;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

public class FaceDetector : MonoBehaviour
{
    [Header("引用")]
    public FrameSourceBehaviour frameSource;
    public ExperienceSettings settings;

    /// <summary>最新一次检测结果（归一化框、左上角原点；仅含 person）。</summary>
    public List<DetectionResult> Latest { get; private set; } = new();

    /// <summary>最近一次"预处理+推理+回读"总耗时（毫秒），用于性能记录。</summary>
    public float LastInferenceMs { get; private set; }

    Worker _worker;
    Tensor<float> _input;
    Vector2Int _inputSize;
    const int PersonClassId = 0;
    const float IouThreshold = 0.6f; // 官方样例默认值

    void Awake()
    {
        if (settings == null || settings.modelAsset == null)
        {
            Debug.LogError("[FaceDetector] 缺少 ExperienceSettings 或模型资产，已禁用。");
            enabled = false;
            return;
        }

        var model = ModelLoader.Load(settings.modelAsset);
        var inputShape = model.inputs[0].shape;
        _inputSize = new Vector2Int(inputShape.Get(2), inputShape.Get(3));
        _worker = new Worker(model, settings.useGpuBackend ? BackendType.GPUCompute : BackendType.CPU);
        _input = new Tensor<float>(new TensorShape(1, 3, _inputSize.x, _inputSize.y));

        PreloadModel(settings.modelAsset); // 首次推理会阻塞数秒（算子编译），提前做完
        StartCoroutine(DetectionLoop());
    }

    /// <summary>官方范式：用 2x2 空纹理做一次 dummy 推理，避免首帧卡顿。</summary>
    static void PreloadModel(ModelAsset modelAsset)
    {
        var model = ModelLoader.Load(modelAsset);
        using var worker = new Worker(model, BackendType.CPU);
        var tempTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        // 注：Sentis 2.6 起 SetDimensions 已弃用——目标张量的尺寸会被自动采用
        var textureTransform = new TextureTransform();
        var inputShape = model.inputs[0].shape;
        using var input = new Tensor<float>(new TensorShape(1, 3, inputShape.Get(2), inputShape.Get(3)));
        TextureConverter.ToTensor(tempTexture, input, textureTransform);
        worker.Schedule(input);
        worker.PeekOutput(0).CompleteAllPendingOperations();
        worker.PeekOutput(1).CompleteAllPendingOperations();
        worker.PeekOutput(2).CompleteAllPendingOperations();
        Destroy(tempTexture);
    }

    IEnumerator DetectionLoop()
    {
        while (true)
        {
            // 节流：每 detectionStride 帧检测一次
            int stride = settings != null ? Mathf.Max(1, settings.detectionStride) : 1;
            for (int i = 0; i < stride; i++) yield return null;

            if (frameSource == null || !frameSource.IsReady) continue;
            if (!frameSource.TryGetFrame(out var frame) || !frame.valid || frame.texture == null) continue;
            yield return RunOnce(frame);
        }
    }

    IEnumerator RunOnce(CameraFrame frame)
    {
        float t0 = Time.realtimeSinceStartup;

        // GPU 侧预处理：源纹理 → 张量（Sentis 2.6：目标张量尺寸自动决定缩放）
        var textureTransform = new TextureTransform();
        TextureConverter.ToTensor(frame.texture, _input, textureTransform);
        _worker.Schedule(_input);

        // 异步回读三个输出（绝不用阻塞版 ReadbackAndClone）
        var boxesAwaiter = (_worker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
        while (!boxesAwaiter.IsCompleted) yield return null;
        using var boxes = boxesAwaiter.GetResult();
        if (boxes.shape[0] == 0) { Latest = new List<DetectionResult>(); yield break; }

        var classIdsAwaiter = (_worker.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync().GetAwaiter();
        while (!classIdsAwaiter.IsCompleted) yield return null;
        using var classIds = classIdsAwaiter.GetResult();

        var scoresAwaiter = (_worker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync().GetAwaiter();
        while (!scoresAwaiter.IsCompleted) yield return null;
        using var scores = scoresAwaiter.GetResult();

        // 解析 → 只留 person（classId 0）→ 置信度过滤 + NMS
        var raw = new List<DetectionResult>();
        int n = boxes.shape[0];
        for (int i = 0; i < n; i++)
        {
            if (classIds[i] != PersonClassId) continue;

            float x1 = boxes[i, 0], y1 = boxes[i, 1], x2 = boxes[i, 2], y2 = boxes[i, 3];
            raw.Add(new DetectionResult
            {
                normalizedBox = new Rect(
                    x1 / _inputSize.x, y1 / _inputSize.y,
                    (x2 - x1) / _inputSize.x, (y2 - y1) / _inputSize.y),
                confidence = scores[i]
            });
        }

        float confThreshold = settings != null ? settings.detectionConfThreshold : 0.35f;
        Latest = DetectionPostprocess.NMS(raw, confThreshold, IouThreshold);

        LastInferenceMs = (Time.realtimeSinceStartup - t0) * 1000f;
    }

    void OnDestroy()
    {
        if (_worker == null) return;
        _worker.PeekOutput(0)?.CompleteAllPendingOperations();
        _worker.PeekOutput(1)?.CompleteAllPendingOperations();
        _worker.PeekOutput(2)?.CompleteAllPendingOperations();
        _worker.Dispose();
    }
}
