// Assets/Scripts/UI/IPWorldView.cs
// 真机世界空间 IP（小精灵）：把精灵"贴"到老师头部的真实 3D 位置。
// 驱动层（M3DebugDriver）保持平台无关——只推归一化坐标/透明度/缩放（与电脑版 IPDebugView 同接口）；
// 本组件用与 Detection3DOverlay 相同的反投影管道（帧同步位姿 + 焦距）换算世界位置。
// 关键机制——"世界锚定"：只有输入坐标/框高变化时才重算世界位置。
//   目标丢失时跟踪器输出冻结值（坐标不变）→ 精灵停在原地（世界坐标稳定），
//   不会随头部转动而漂移（符合设计文档 §4.3"人脸可丢，方向不丢"）。
// 占位美术：程序生成圆形贴图（与电脑版 IPDebugView 同款脸）。
// 组员正式素材到位后：替换 CreatePlaceholderSprite 的实现（或直接给 SpriteRenderer 换 sprite）。
using Meta.XR;
using UnityEngine;

public class IPWorldView : MonoBehaviour
{
    [Header("引用")]
    public PcaFrameSource frameSource;

    [Header("参数")]
    [Tooltip("估算用的人体身高（与检测框一致）")]
    public float assumedPersonHeight = 1.7f;
    [Tooltip("精灵基准世界尺寸（米）：≈人脸高度")]
    public float baseWorldSize = 0.20f;
    [Tooltip("世界偏移：微调到脸侧/脸前，避免与脸完全重叠")]
    public Vector3 worldOffset = new Vector3(0f, 0f, -0.03f);

    // ── 驱动层每帧写入（与 IPDebugView 保持一致的接口）──
    public bool visible;
    public float x01 = 0.5f, y01 = 0.5f; // 归一化坐标（左上原点，与检测框一致）
    public float height01;               // 目标框归一化高度（估距用）
    public float alpha = 1f;
    public float scale = 1f;
    public Color tint = Color.white;

    PassthroughCameraAccess _camera;
    SpriteRenderer _renderer;

    // 世界锚定：输入不变时保持上一帧的世界位置
    bool _hasWorldPos;
    Vector3 _lastWorldPos;
    float _prevX = float.NaN, _prevY = float.NaN, _prevH = float.NaN;

    void Awake()
    {
        _camera = GetComponent<PassthroughCameraAccess>();

        var go = new GameObject("IP_Sprite");
        go.transform.SetParent(transform, false);
        _renderer = go.AddComponent<SpriteRenderer>();
        _renderer.sprite = CreatePlaceholderSprite();
        _renderer.enabled = false;
    }

    void LateUpdate()
    {
        if (_renderer == null) return;

        if (!visible || alpha <= 0.01f)
        {
            _renderer.enabled = false;
            return;
        }

        CameraFrame frame = default;
        bool haveFrame = false;
        if (frameSource != null
            && frameSource.TryGetFrame(out frame)
            && frame.valid && frame.texture != null)
        {
            haveFrame = true;
        }
        if (!haveFrame || _camera == null)
        {
            _renderer.enabled = false;
            return;
        }

        // 输入变化才重算世界位置（不变 = 目标冻结/静止 → 世界锚定不漂移）
        bool inputChanged = !_hasWorldPos
                            || !Mathf.Approximately(x01, _prevX)
                            || !Mathf.Approximately(y01, _prevY)
                            || !Mathf.Approximately(height01, _prevH);

        if (inputChanged)
        {
            _prevX = x01; _prevY = y01; _prevH = height01;

            float texH = frame.texture.height;
            float hPx = Mathf.Max(height01, 0.02f) * texH; // 防除零；过小框也给个下限
            float dist = Mathf.Clamp(frame.focalLengthPx * assumedPersonHeight / hPx, 0.3f, 8f);

            var vp = new Vector2(x01, 1f - y01); // 左上原点 → 视口坐标（左下原点）
            var ray = _camera.ViewportPointToRay(vp, frame.cameraPose);

            Vector3 right = frame.cameraPose.rotation * Vector3.right;
            Vector3 up = frame.cameraPose.rotation * Vector3.up;
            Vector3 fwd = frame.cameraPose.rotation * Vector3.forward;
            _lastWorldPos = ray.origin + ray.direction * dist
                            + right * worldOffset.x + up * worldOffset.y + fwd * worldOffset.z;
            _hasWorldPos = true;
        }

        var t = _renderer.transform;
        t.position = _lastWorldPos;
        // 面向用户（用帧相机位置近似眼睛位置；精灵默认面朝 +Z）
        Vector3 toEye = t.position - frame.cameraPose.position;
        if (toEye.sqrMagnitude > 1e-6f)
            t.rotation = Quaternion.LookRotation(toEye.normalized, frame.cameraPose.rotation * Vector3.up);
        t.localScale = Vector3.one * (baseWorldSize * Mathf.Max(scale, 0.05f));

        _renderer.color = new Color(tint.r, tint.g, tint.b, alpha);
        _renderer.enabled = true;
    }

    /// <summary>程序生成占位精灵（64×64 紫色圆脸 + 眼睛 + 白圈），与电脑版同款。</summary>
    static Sprite CreatePlaceholderSprite()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color32[s * s];
        float r = s * 0.5f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = x - (r - 0.5f), dy = y - (r - 0.5f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            bool inside = d <= r - 2f;
            bool ring = d > r - 6f && d <= r - 1f;
            Color32 c;
            if (inside)
            {
                c = new Color32(170, 128, 255, 255);
                float ex = Mathf.Abs(dx) - r * 0.30f, ey = dy + r * 0.12f;
                if (ex * ex + ey * ey < (r * 0.10f) * (r * 0.10f))
                    c = new Color32(40, 30, 70, 255);
            }
            else if (ring) c = new Color32(255, 255, 255, 190);
            else c = new Color32(0, 0, 0, 0);
            px[y * s + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply();
        // PPU=纹理尺寸：缩放 1 时精灵世界尺寸恰为 1m（baseWorldSize 可直接按米理解）
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
    }
}
