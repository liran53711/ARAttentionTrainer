// Assets/Scripts/UI/Detection3DOverlay.cs
// 真机 3D 检测可视化：把检测框"贴"到真实世界里的人身上（透视画面中直接看到追踪框）。
// 原理：
//   · 用 PCA 的 ViewportPointToRay（带"帧同步"位姿）把框中心反投影成世界射线
//     —— 不能用实时头姿：异步推理约 200ms，期间头在转，画面与框会错位
//   · 距离估算：距离 = 焦距(px) × 人的真实身高(≈1.7m) / 框的像素高
//     （身高是假设值，M3.5 之后可用 MRUK 环境深度射线替换，精度更高）
// 坐标系：DetectionResult 是归一化框、左上原点；Unity 视口是左下原点 → 转换时 y 翻转。
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

public class Detection3DOverlay : MonoBehaviour
{
    [Header("引用")]
    public FaceDetector detector;
    public PcaFrameSource frameSource;

    [Header("外观（材质由场景生成器创建并接线，保证着色器进包不被裁剪）")]
    public Material targetMaterial;   // 最大的人 = 引导目标
    public Material otherMaterial;

    [Header("参数")]
    [Tooltip("估算用的人体身高（米）")]
    public float assumedPersonHeight = 1.7f;
    public float lineWidth = 0.006f;
    public int maxRendered = 5;

    PassthroughCameraAccess _camera;
    readonly List<LineRenderer> _lines = new List<LineRenderer>();

    void Awake()
    {
        // 与 PcaFrameSource 挂在同一个物体上
        _camera = GetComponent<PassthroughCameraAccess>();
    }

    void LateUpdate()
    {
        CameraFrame frame = default;
        bool haveFrame = false;
        if (frameSource != null
            && frameSource.TryGetFrame(out frame)
            && frame.valid && frame.texture != null)
        {
            haveFrame = true;
        }
        var detections = detector != null ? detector.Latest : null;
        bool haveBoxes = haveFrame && _camera != null && detections != null && detections.Count > 0;

        if (!haveBoxes)
        {
            foreach (var l in _lines) l.enabled = false;
            return;
        }

        int texW = frame.texture.width, texH = frame.texture.height;

        // 最大框 = 目标（与 TargetTrackerLogic 的"取最大"策略一致）
        int targetIdx = 0;
        float best = -1f;
        for (int i = 0; i < detections.Count; i++)
        {
            var nb = detections[i].normalizedBox;
            float a = nb.width * nb.height;
            if (a > best) { best = a; targetIdx = i; }
        }

        int shown = 0;
        for (int i = 0; i < detections.Count && shown < maxRendered; i++)
        {
            var b = detections[i].normalizedBox;
            float hPx = b.height * texH;
            if (hPx < 4f) continue;

            // 归一化框(左上原点) → 视口坐标（★实测修正 2026-10-10：
            // PCA 的 ViewportPointToRay 采用"左上原点"视口约定，与检测框一致，直接传，勿翻转！
            // 之前做了 Unity 式 y 翻转 → 射线上下颠倒 → 框/精灵画到人脚下）
            var center01 = new Vector2(b.x + b.width * 0.5f, b.y + b.height * 0.5f);

            // 帧同步位姿反投影
            var ray = _camera.ViewportPointToRay(center01, frame.cameraPose);
            float dist = Mathf.Clamp(frame.focalLengthPx * assumedPersonHeight / hPx, 0.3f, 8f);

            Vector3 center = ray.origin + ray.direction * dist;
            Vector3 right = frame.cameraPose.rotation * Vector3.right;
            Vector3 up = frame.cameraPose.rotation * Vector3.up;
            float wWorld = dist * (b.width * texW) / frame.focalLengthPx;
            float hWorld = dist * hPx / frame.focalLengthPx;

            var lr = GetLine(shown++);
            DrawRect(lr, center, right, up, wWorld, hWorld,
                i == targetIdx ? targetMaterial : otherMaterial);
        }
        for (int i = shown; i < _lines.Count; i++) _lines[i].enabled = false;
    }

    LineRenderer GetLine(int index)
    {
        while (_lines.Count <= index)
        {
            var go = new GameObject($"Box{_lines.Count}");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 4;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            _lines.Add(lr);
        }
        return _lines[index];
    }

    void DrawRect(LineRenderer lr, Vector3 c, Vector3 right, Vector3 up, float w, float h, Material mat)
    {
        lr.enabled = true;
        lr.sharedMaterial = mat;
        float hw = w * 0.5f, hh = h * 0.5f;
        lr.SetPosition(0, c - right * hw - up * hh);
        lr.SetPosition(1, c + right * hw - up * hh);
        lr.SetPosition(2, c + right * hw + up * hh);
        lr.SetPosition(3, c - right * hw + up * hh);
    }
}
