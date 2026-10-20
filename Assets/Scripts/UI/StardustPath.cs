// Assets/Scripts/UI/StardustPath.cs
// 星尘路径 v6（2026-10-20 用户实测"效果一模一样"后重做）：
//   根因①：占位贴图 star_soft.png 的内容只在左上 128×128 象限 → 每颗星偏在面片左上角、只有 1/4 大小。
//          现改用 star_dot.png（居中裁切）。
//   根因②：旧路径是"空中细弧线"，没有地面透视，读不出"一条路"。
//          现改为：视野前下方（可见）起步 → 落到地面 → 贴地绕过孩子 → 铺到老师脚下；
//          120 颗物理尺寸一致的星点（近大远小）形成真实 3D 透视的"星路"，两端淡出。
//   方向：终点 = 老师独立空间锚点（最后一次已知世界位置）的地面投影 + 身侧偏移；
//         老师位于背后时，控制点绕到孩子侧方（保证不穿过身体）。
// 调试：开启时每 2s 打一条 [Stardust] 日志（起点/终点/老师锚点/方位角/星数）。
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;

public class StardustPath : MonoBehaviour
{
    [Header("素材/引用")]
    public Material starMat;          // 星点材质（居中星图，GuideArrow shader）
    public Transform eye;             // 观察者（中心眼）
    public MrukBootstrap mruk;        // 可选：房间地板高度（没有则用 eyeHeight 估算）

    [Header("几何")]
    [Tooltip("起点：视野前方多少米（水平距离）")]
    public float startForward = 0.90f;
    [Tooltip("起点比眼睛低多少米（保证在垂直视野内看得见路头）")]
    public float startDrop = 0.65f;
    [Tooltip("绕开孩子所需的最小侧向距离（米）")]
    public float detour = 0.95f;
    [Tooltip("终点相对老师身体的侧向偏移（米）")]
    public float endSideOffset = 0.30f;
    [Tooltip("无房间数据时用于估算地板高度的眼高（米）")]
    public float eyeHeight = 1.5f;
    [Tooltip("铺路星点数量（密集铺满）")]
    public int starDots = 150;
    [Tooltip("星点基准尺寸（米）")]
    public float starDotSize = 0.10f;
    [Tooltip("星点偏离路径中心的横向散布半宽（米）——路面宽度")]
    public float roadHalfWidth = 0.50f;

    [Header("节奏")]
    [Tooltip("端点/控制点重算间隔（秒）——孩子走动/老师移动时保持正确")]
    public float updateInterval = 0.1f;
    [Tooltip("控制点平滑时间（秒）")]
    public float smoothTime = 0.18f;
    [Tooltip("星点沿路向老师方向缓慢流动的速度（归一化/秒）")]
    public float flowSpeed = 0.06f;

    const int MaxDots = 192;      // 120 路径星 + 6 终点星，留余量
    const int BeaconDots = 6;     // 终点星星柱（老师身侧）

    public bool IsOn { get; private set; }

    // ── 路径控制点（三次贝塞尔）──
    struct Path
    {
        public Vector3 p0, c1, c2, p3; // 起点/控制1/控制2/终点
    }

    Path _raw, _smooth;
    readonly Vector3[] _vel = new Vector3[4];
    bool _hasSmooth, _built;
    float _nextCalc, _flow, _diagT;

    Transform[] _dots;
    MeshRenderer[] _dotR;
    MaterialPropertyBlock _mpb;

    // 每颗星的确定性伪随机参数（构建时生成，运行时不变）
    float[] _jSide, _jUp, _sMul, _phase, _pick;

    // 最近一次的几何（诊断用）
    Vector3 _diagStart, _diagEnd, _diagTeacher;

    void Awake()
    {
        EnsureBuilt();
        gameObject.SetActive(false);
    }

    /// <summary>构建星点（预览工具也会显式调用；幂等）。</summary>
    public void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        _dots = new Transform[MaxDots];
        _dotR = new MeshRenderer[MaxDots];
        _mpb = new MaterialPropertyBlock();
        _jSide = new float[MaxDots];
        _jUp = new float[MaxDots];
        _sMul = new float[MaxDots];
        _phase = new float[MaxDots];
        _pick = new float[MaxDots];

        var shared = starMat != null ? starMat
                   : new Material(Shader.Find("ARAttention/GuideArrow"));
        for (int i = 0; i < MaxDots; i++)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Star" + i;
            quad.transform.SetParent(transform, false);
            Destroy(quad.GetComponent<Collider>());
            var qr = quad.GetComponent<MeshRenderer>();
            qr.sharedMaterial = shared;                 // 共享材质 + MPB 逐星染色
            qr.shadowCastingMode = ShadowCastingMode.Off;
            qr.receiveShadows = false;
            qr.enabled = false;
            _dots[i] = quad.transform;
            _dotR[i] = qr;
            // 确定性伪随机（不依赖 UnityEngine.Random，测试/构建稳定）
            _jSide[i] = Hash01(i * 12.9898f + 3.7f) * 2f - 1f;
            _jUp[i] = Hash01(i * 78.233f + 9.1f);
            _sMul[i] = 0.62f + 0.75f * Hash01(i * 39.425f + 1.3f);
            _phase[i] = Hash01(i * 93.989f + 7.7f) * Mathf.PI * 2f;
            _pick[i] = Hash01(i * 53.717f + 5.5f);
        }
    }

    /// <summary>开/关星路（驱动层在"引导激活且老师不可见"时打开）。</summary>
    public void SetOn(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        gameObject.SetActive(on);
        if (on)
        {
            _hasSmooth = false;      // 重新出现：直接对齐，不做跨屏滑移
            _nextCalc = 0f;
        }
        else
        {
            for (int i = 0; i < MaxDots; i++)
                if (_dotR[i] != null) _dotR[i].enabled = false;
        }
    }

    /// <summary>驱动层每帧调用。teacherPos=老师世界位置（独立锚点，可丢失后保持）。</summary>
    public void Tick(float now, float dt, Vector3 teacherPos)
    {
        if (!IsOn || eye == null) return;
        EnsureBuilt();
        Vector3 eyePos = eye.position;
        Vector3 fwd = eye.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        // ── 地板高度：优先 MRUK 房间数据，否则按眼高估算 ──
        float floorY = eyePos.y - eyeHeight;
        if (mruk != null && mruk.TryGetRoomBounds(out var room))
            floorY = room.min.y;

        // ── 端点/控制点：节流重算 + 平滑 ──
        if (now >= _nextCalc || !_hasSmooth)
        {
            _nextCalc = now + Mathf.Max(0.02f, updateInterval);
            ComputePath(eyePos, fwd, floorY, teacherPos,
                        startForward, startDrop, detour, endSideOffset,
                        out _raw.p0, out _raw.c1, out _raw.c2, out _raw.p3);
            _diagStart = _raw.p0;
            _diagEnd = _raw.p3;
            _diagTeacher = teacherPos;

            if (!_hasSmooth)
            {
                _smooth = _raw; _hasSmooth = true;
                System.Array.Clear(_vel, 0, _vel.Length);
            }
            else
            {
                _smooth.p0 = Vector3.SmoothDamp(_smooth.p0, _raw.p0, ref _vel[0], smoothTime);
                _smooth.c1 = Vector3.SmoothDamp(_smooth.c1, _raw.c1, ref _vel[1], smoothTime);
                _smooth.c2 = Vector3.SmoothDamp(_smooth.c2, _raw.c2, ref _vel[2], smoothTime);
                _smooth.p3 = Vector3.SmoothDamp(_smooth.p3, _raw.p3, ref _vel[3], smoothTime);
            }
        }

        // ── 星点沿路铺满 + 缓慢流动 + 闪烁 ──
        _flow = Mathf.Repeat(_flow + flowSpeed * dt, 1f);
        int n = Mathf.Clamp(starDots, 0, MaxDots - BeaconDots);
        // 路宽随路长收缩：短路不再是一团 1m 宽的星星
        float pathLen = Vector3.Distance(_smooth.p0, _smooth.p3);
        float halfWidth = RoadHalfWidthFor(pathLen, roadHalfWidth);
        for (int i = 0; i < n; i++)
        {
            float t = Mathf.Repeat((i + 0.5f) / Mathf.Max(1, n) + _flow, 1f);
            Vector3 p = CubicBezier(_smooth.p0, _smooth.c1, _smooth.c2, _smooth.p3, t);
            Vector3 tangent = CubicBezier(_smooth.p0, _smooth.c1, _smooth.c2, _smooth.p3,
                                          Mathf.Min(1f, t + 0.012f))
                            - CubicBezier(_smooth.p0, _smooth.c1, _smooth.c2, _smooth.p3,
                                          Mathf.Max(0f, t - 0.012f));
            Vector3 side = Vector3.Cross(tangent, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(fwd, Vector3.up);
            side.Normalize();

            // 路面横向散布 + 微小高度起伏（星星不是一条线，而是一条有宽度的路）
            float widthProfile = halfWidth * (0.82f + 0.18f * Mathf.Sin(t * Mathf.PI));
            p += side * (_jSide[i] * widthProfile);
            p += Vector3.up * (0.03f + _jUp[i] * 0.10f);

            // 两端淡出（路头/路尾自然消失在空气里）
            float fade = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((1f - t) / 0.08f);
            float tw = 0.62f + 0.38f * Mathf.Sin(now * (2.2f + 2.4f * _phase[i]) + _phase[i]);
            float size = starDotSize * _sMul[i] * (0.85f + 0.15f * tw) * fade;

            PlaceDot(i, p, size, fade * (0.72f + 0.28f * tw), ColorFor(_pick[i]));
        }

        // ── 终点星星柱：老师身侧 0.15~1.0m 高度，明确"路通向这里" ──
        Vector3 endSide = Vector3.Cross(Vector3.up, (_smooth.p3 - _smooth.p0)).normalized;
        for (int k = 0; k < BeaconDots; k++)
        {
            int i = n + k;
            float h = 0.18f + k * 0.15f;
            Vector3 p = _smooth.p3 + endSide * 0.10f + Vector3.up * h;
            float tw = 0.65f + 0.35f * Mathf.Sin(now * 2.7f + k * 1.4f);
            float size = starDotSize * (1.25f - 0.06f * k) * (0.9f + 0.1f * tw);
            PlaceDot(i, p, size, 0.85f * tw, ColorFor(0.15f + 0.1f * k));
        }

        // 其余的星隐藏（数量下调时）
        for (int i = n + BeaconDots; i < MaxDots; i++)
            if (_dotR[i] != null) _dotR[i].enabled = false;

        // ── 诊断（2s 一条）──
        _diagT += dt;
        if (_diagT >= 2f)
        {
            _diagT = 0f;
            Vector3 dirT = _diagTeacher - eyePos; dirT.y = 0f;
            float ang = dirT.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(fwd, dirT.normalized, Vector3.up) : 0f;
            Debug.Log($"[Stardust] 起点={_diagStart.ToString("F2")} 终点={_diagEnd.ToString("F2")} " +
                      $"路长={Vector3.Distance(_diagStart, _diagEnd):F2}m 半宽={halfWidth:F2}m " +
                      $"老师锚={_diagTeacher.ToString("F2")} 眼={eyePos.ToString("F2")} " +
                      $"老师方位角={ang:F0}° 地板Y={floorY:F2} 星点={n}+{BeaconDots}");
        }
    }

    void PlaceDot(int i, Vector3 pos, float size, float alpha, Color col)
    {
        if (_dots[i] == null) return;
        bool on = alpha > 0.02f && size > 0.002f;
        _dotR[i].enabled = on;
        if (!on) return;
        _dots[i].position = pos;
        _dots[i].rotation = Quaternion.LookRotation(pos - eye.position, Vector3.up);
        _dots[i].localScale = Vector3.one * size;
        col.a = alpha;
        _mpb.SetColor("_Color", col);
        _dotR[i].SetPropertyBlock(_mpb);
    }

    static Color ColorFor(float pick)
    {
        if (pick < 0.62f) return new Color(1f, 0.93f, 0.68f, 1f);   // 暖金
        if (pick < 0.88f) return new Color(0.86f, 0.80f, 1f, 1f);   // 淡紫
        return new Color(0.92f, 0.98f, 1f, 1f);                      // 冷白
    }

    static float Hash01(float x)
    {
        float v = Mathf.Sin(x) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    /// <summary>路面半宽随路长收缩（纯函数，供单测）：短路若保持 1m 宽会变成"一团星星"。</summary>
    public static float RoadHalfWidthFor(float pathLength, float baseHalfWidth)
    {
        // 3m 以上保持设定路宽；越短越窄（0.4m 的路 → 约 0.07m 半宽），下限 6cm
        float k = Mathf.Clamp01(pathLength / 3f);
        return Mathf.Clamp(baseHalfWidth * k, 0.06f, baseHalfWidth);
    }

    /// <summary>三次贝塞尔采样（公开为静态，供 EditMode 几何测试/预览使用）。</summary>
    public static Vector3 CubicBezier(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0
             + 3f * u * u * t * c1
             + 3f * u * t * t * c2
             + t * t * t * p3;
    }

    /// <summary>计算路径控制点（纯函数，供单测）：
    /// 起点=眼前下方（视野内）→ 落到地面 → 绕过孩子 → 老师脚下身侧。
    /// 老师位于身后时，第二控制点绕到孩子侧方（保证路面不穿过身体）。</summary>
    public static void ComputePath(Vector3 eyePos, Vector3 eyeForward, float floorY, Vector3 teacherPos,
                                   float startForward, float startDrop, float detour, float endSideOffset,
                                   out Vector3 p0, out Vector3 c1, out Vector3 c2, out Vector3 p3)
    {
        Vector3 fwd = new Vector3(eyeForward.x, 0f, eyeForward.z);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        // 路头：视野前下方（在垂直 FOV 内；从"眼前一段距离"开始）
        p0 = eyePos + fwd * startForward + Vector3.down * startDrop;

        // 地板落点：路头前方 0.6m 落到地面
        Vector3 land = new Vector3(p0.x, floorY + 0.05f, p0.z) + fwd * 0.6f;

        // 路尾：老师脚下 + 身侧偏移（"老师身旁"）
        Vector3 dirT = new Vector3(teacherPos.x - eyePos.x, 0f, teacherPos.z - eyePos.z);
        if (dirT.sqrMagnitude < 1e-4f) dirT = fwd;
        dirT.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized; // 孩子右手方向
        Vector3 endSide = Vector3.Cross(Vector3.up, dirT).normalized;
        // 站在老师哪一侧：取离孩子更远的一侧，避免路尾贴着孩子
        if (Vector3.Dot(endSide, eyePos - teacherPos) > 0f) endSide = -endSide;
        p3 = new Vector3(teacherPos.x, floorY + 0.05f, teacherPos.z) + endSide * endSideOffset;

        bool behind = Vector3.Dot(fwd, dirT) < 0.15f; // 老师侧后方/正后方
        if (behind)
        {
            // 绕行点：孩子侧方（取老师所在一侧），保证贝塞尔中段远离孩子身体
            float sideSign = Vector3.Dot(right, dirT);
            if (Mathf.Abs(sideSign) < 0.05f) sideSign = 1f;
            sideSign = Mathf.Sign(sideSign);
            float lateral = Mathf.Max(2.2f, detour * 2.4f);
            c2 = new Vector3(eyePos.x, floorY + 0.30f, eyePos.z)
               + right * (sideSign * lateral) + fwd * 0.35f;
        }
        else
        {
            // 老师在身前：地面段正常绕开孩子（不会穿过身体）
            Vector3 childFloor = new Vector3(eyePos.x, floorY, eyePos.z);
            Vector3 ctrl = MeteorLogic.PathControl(childFloor, land, p3, detour);
            c2 = new Vector3(ctrl.x, floorY + 0.30f, ctrl.z);
        }

        // 第一控制点：把路头快速压向地面（形成"从眼前垂下 → 贴地"的 3D 过渡）
        c1 = Vector3.Lerp(p0, land, 0.55f) + Vector3.down * 0.45f;
        c1.y = Mathf.Max(floorY + 0.12f, c1.y);
    }
}
