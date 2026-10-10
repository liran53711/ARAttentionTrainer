// Assets/Scripts/UI/MaskOccluder.cs
// A 项（用户定调）：屏幕空间"人物擦除"——精灵/画线被真实人体轮廓遮挡（"躲到人身后"）。
// 原理：挂在中心眼相机前的全屏四边形上，每帧计算
//   屏幕UV(眼相机) → 世界方向 → PCA 相机像素坐标 的 3×3 单应矩阵，
//   在着色器里取人体掩码，用 Blend Zero OneMinusSrcAlpha 把"绘制在其下"的精灵擦除。
// 关键点：两次投影都是针孔模型 → 映射是纯投影变换（单应），可解析计算（见 ComputeRows，已被单测覆盖）。
using Meta.XR;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class MaskOccluder : MonoBehaviour
{
    [Header("引用")]
    public SelfieSegRunner seg;          // 人体掩码来源（其 MaskTexture 每帧更新）
    public PcaFrameSource frameSource;   // 相机帧（位姿 + 焦距）
    public PassthroughCameraAccess pca;  // ViewportPointToRay（基向量推导，保证与全链路一致）
    public Camera eyeCamera;             // 中心眼相机（本组件的父级）

    [Header("参数")]
    [Tooltip("四边形距眼睛的放置距离（米）")]
    public float quadDistance = 0.2f;
    [Tooltip("尺寸冗余系数（防边缘露角）")]
    public float sizeMargin = 1.6f;

    Material _mat;
    readonly Vector3[] _rows = new Vector3[3];
    float _lastDiag;
    static readonly int Row0 = Shader.PropertyToID("_Row0");
    static readonly int Row1 = Shader.PropertyToID("_Row1");
    static readonly int Row2 = Shader.PropertyToID("_Row2");
    static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");

    void Awake()
    {
        _mat = GetComponent<MeshRenderer>().material; // 场景里已挂 MaskErase 材质（实例化）
        ResizeQuad();
    }

    void ResizeQuad()
    {
        if (eyeCamera == null) return;
        // XR 相机的 fieldOfView 不可信（投影来自 XR 矩阵）→ 直接给"远超视野"的固定尺寸。
        // 超出掩码范围的部分擦除量为 (1-0)=1 天然无操作，放大无害。
        float size = Mathf.Max(2f, 2f * quadDistance * sizeMargin);
        transform.localPosition = new Vector3(0f, 0f, quadDistance);
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(size, size, 1f);
    }

    void LateUpdate()
    {
        if (_mat == null || seg == null || seg.MaskTexture == null || eyeCamera == null) return;
        if (!seg.HasMask) return; // 尚无掩码 → 不擦除（掩码初值全 0，天然无操作）

        CameraFrame frame = default;
        if (frameSource == null || !frameSource.TryGetFrame(out frame) || !frame.valid || frame.texture == null)
            return;

        // ── PCA 相机基（用 API 自身反推，与精灵/检测框的坐标链完全一致）──
        Vector3 pF = pca.ViewportPointToRay(new Vector2(0.5f, 0.5f), frame.cameraPose).direction;
        Vector3 pR = Vector3.ProjectOnPlane(pca.ViewportPointToRay(new Vector2(1f, 0.5f), frame.cameraPose).direction, pF).normalized;
        Vector3 pU = Vector3.ProjectOnPlane(pca.ViewportPointToRay(new Vector2(0.5f, 0f), frame.cameraPose).direction, pF).normalized; // v=0 为图像顶部

        // ── 眼相机基（含 tanX/tanY，兼容非对称投影）──
        Vector3 eF = eyeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f)).direction;
        Vector3 eR = eyeCamera.ViewportPointToRay(new Vector3(1f, 0.5f)).direction;
        Vector3 eT = eyeCamera.ViewportPointToRay(new Vector3(0.5f, 1f)).direction;
        Vector3 eRv = Vector3.ProjectOnPlane(eR, eF).normalized;
        Vector3 eUv = Vector3.ProjectOnPlane(eT, eF).normalized;
        float tanX = Vector3.Project(eR, eRv).magnitude / Mathf.Max(1e-5f, Vector3.Project(eR, eF).magnitude);
        float tanY = Vector3.Project(eT, eUv).magnitude / Mathf.Max(1e-5f, Vector3.Project(eT, eF).magnitude);

        ComputeRows(pF, pR, pU, frame.focalLengthPx, seg.MaskWidth, seg.MaskHeight,
                    eF, eRv, eUv, tanX, tanY, _rows);

        _mat.SetVector(Row0, _rows[0]);
        _mat.SetVector(Row1, _rows[1]);
        _mat.SetVector(Row2, _rows[2]);
        _mat.SetTexture(MaskTexId, seg.MaskTexture);

        // 诊断（5 秒一条）：单应行 + 中间像素应≈中心掩码值——映射错了立即看得出来
        if (Time.unscaledTime - _lastDiag > 5f)
        {
            _lastDiag = Time.unscaledTime;
            Debug.Log($"[Occ] 行0={_rows[0].ToString("F3")} 行2={_rows[2].ToString("F3")} " +
                      $"掩码峰值={seg.MaxValue:F2} 覆盖={seg.Coverage:P0}");
        }
    }

    /// <summary>
    /// 解析计算 屏幕UV(眼相机, 左下原点) → 掩码UV(左上原点) 的单应矩阵三行（纯函数，供单测）。
    /// 推导：sw=(u,v,1) →（E）→眼相机坐标(−1..1,tX/tY) →（A，两次基变换）→PCA相机坐标(x,y,z)
    ///       →（P）→ (u_m,v_m,1)*z 齐次 → 取行。
    /// </summary>
    public static void ComputeRows(
        Vector3 pcaFwd, Vector3 pcaRight, Vector3 pcaUp, float focalPx, float maskW, float maskH,
        Vector3 eyeFwd, Vector3 eyeRight, Vector3 eyeUp, float tanX, float tanY,
        Vector3[] rows)
    {
        // A：[-1..1] 眼相机坐标 → PCA 相机坐标
        float r00 = Vector3.Dot(eyeRight, pcaRight) * tanX, r01 = Vector3.Dot(eyeUp, pcaRight) * tanY, r02 = Vector3.Dot(eyeFwd, pcaRight);
        float r10 = Vector3.Dot(eyeRight, pcaUp) * tanX, r11 = Vector3.Dot(eyeUp, pcaUp) * tanY, r12 = Vector3.Dot(eyeFwd, pcaUp);
        float r20 = Vector3.Dot(eyeRight, pcaFwd) * tanX, r21 = Vector3.Dot(eyeUp, pcaFwd) * tanY, r22 = Vector3.Dot(eyeFwd, pcaFwd);

        // A*E（输入 sw 为"左上原点"屏幕UV；E: (u,v,1) → (2u-1, 1-2v, 1)）
        float a00 = 2f * r00, a01 = -2f * r01, a02 = r02 - r00 + r01;
        float a10 = 2f * r10, a11 = -2f * r11, a12 = r12 - r10 + r11;
        float a20 = 2f * r20, a21 = -2f * r21, a22 = r22 - r20 + r21;

        // P：u_m = 0.5 + (x/z)·(f/W)；v_m = 0.5 − (y/z)·(f/H)（v 向下=左上原点）
        float sx = focalPx / Mathf.Max(1f, maskW);
        float sy = focalPx / Mathf.Max(1f, maskH);
        rows[0] = new Vector3(0.5f * a20 + sx * a00, 0.5f * a21 + sx * a01, 0.5f * a22 + sx * a02);
        rows[1] = new Vector3(0.5f * a20 - sy * a10, 0.5f * a21 - sy * a11, 0.5f * a22 - sy * a12);
        rows[2] = new Vector3(a20, a21, a22);
    }
}
