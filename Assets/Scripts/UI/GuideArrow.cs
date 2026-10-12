// Assets/Scripts/UI/GuideArrow.cs
// 引导飘带箭头 v7（2026-10-12 第七轮）：
//   外观 = 设计贴图（ArrowRibbon.png，果冻紫飘带+V头+星光，头身一体不分离）
//   行为 = "有生命"的观察-反应（用户定调）：
//     · 视野/目标变化先被"观察"——变化持续 observeTime 才理会（还在变就继续看）；
//     · 理会后布局目标由 followSmoothTime 缓速追平——永远慢一拍、不慌不忙（与视野有分离感）；
//     · 变化过大（refreshDistance/refreshAngle）→ 整条重新铺设（重新渲染）；
//     · 不是冻结（会一直跟），也不是即时跟随（不随视野瞬动）。
//   附：路尽头柔光信标（缓慢脉动）+ 沿路流光（星光周期扫过）—— 都是世界空间的点缀实体。
using UnityEngine;
using UnityEngine.Rendering;

public class GuideArrow : MonoBehaviour
{
    [Header("贴图（设计资产 1024×256）")]
    public Texture2D ribbonTexture;
    public float uvBodyMin = 0.039f, uvBodyMax = 0.6035f;
    public float uvVMin = 0.26f, uvVMax = 0.76f;
    public float ribbonHalfWidth = 0.15f;

    [Header("V 形头（贴图右侧区域，与飘带同面绘制——一体不分离）")]
    public float uvHeadMin = 0.607f, uvHeadMax = 0.904f;
    public float uvHeadVMin = 0.0703f, uvHeadVMax = 0.9297f;
    public float headWorldWidth = 0.62f;
    [Range(0.05f, 0.4f)] public float headFadeSpan = 0.18f;
    public Transform eye;

    [Header("生命行为（观察-反应）")]
    [Tooltip("观察时间（秒）——目标变化持续这么久才理会")]
    public float observeTime = 0.32f;
    [Tooltip("布局追平时间常数（秒）——越大越慢、分离感越强")]
    public float followSmoothTime = 0.9f;
    [Tooltip("微小变化死区（米）——低于此不理会（防抖）")]
    public float microDeadzone = 0.035f;

    [Header("路径/铺设")]
    public float bow = 0.10f;
    public float minLength = 0.9f;
    public float maxLength = 2.8f;
    public float laySpeed = 2.6f;
    [Tooltip("布局变化超过该距离（米）→ 整条重新铺设")]
    public float refreshDistance = 0.55f;
    [Tooltip("布局方向变化超过该角度（度）→ 整条重新铺设")]
    public float refreshAngle = 32f;

    [Header("信标 + 流光（沿路点缀）")]
    [Tooltip("信标材质（柔光 glow_soft）")]
    public Material beaconMaterial;
    [Tooltip("流光材质（柔星 star_soft）")]
    public Material cometMaterial;
    [Tooltip("信标世界尺寸（米）")]
    public float beaconSize = 0.27f;
    [Tooltip("流光扫过周期（秒）")]
    public float cometPeriod = 2.6f;
    public float cometSize = 0.20f;

    [Header("外观")]
    public Material material;
    public Color color = Color.white;
    [Range(0f, 0.3f)] public float pulseAmount = 0.10f;
    public float pulseHz = 0.8f;

    const int Seg = 22;

    Mesh _mesh;
    MeshRenderer _rend;
    Material _mat;
    bool _want, _hasPath;
    Vector3 _tail, _tip;                 // 驱动推送的原始目标（每帧）
    float _visT;
    float _layLen;

    // 生命行为状态
    Vector3 _layoutTail, _layoutTip;     // 已"理会"的布局目标
    Vector3 _candTail, _candTip;         // 观察中的候选
    float _candT;
    Vector3 _curTail, _curTip;           // 缓速追平中的实际端点
    Vector3 _tailVel, _tipVel;
    bool _inited;

    Transform _beacon, _comet;
    MeshRenderer _beaconRend, _cometRend;

    void Awake() => EnsureInit();

    void EnsureInit()
    {
        if (_rend != null) return;
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _mesh = new Mesh { name = "GuideRibbonDesign" };
        _mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _rend = gameObject.AddComponent<MeshRenderer>();
        _mat = material != null ? new Material(material) : new Material(Shader.Find("ARAttention/GuideArrow"));
        if (ribbonTexture != null) _mat.SetTexture("_MainTex", ribbonTexture);
        _rend.sharedMaterial = _mat;
        _rend.shadowCastingMode = ShadowCastingMode.Off;
        _rend.receiveShadows = false;
        _rend.enabled = false;

        _beacon = MakeQuad("Beacon", beaconMaterial, out _beaconRend);
        _comet = MakeQuad("Comet", cometMaterial, out _cometRend);
    }

    Transform MakeQuad(string name, Material mat, out MeshRenderer rend)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
        rend = go.AddComponent<MeshRenderer>();
        var m = mat != null ? new Material(mat) : new Material(Shader.Find("ARAttention/GuideArrow"));
        rend.sharedMaterial = m;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;
        return go.transform;
    }

    static Mesh _quad;
    static Mesh QuadMesh()
    {
        if (_quad != null) return _quad;
        _quad = new Mesh { name = "FxQuad" };
        _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        _quad.RecalculateBounds();
        return _quad;
    }

    public void Show(bool on) => _want = on;

    public void SetPath(Vector3 tailWorld, Vector3 tipWorld)
    {
        _tail = tailWorld;
        _tip = tipWorld;
        _hasPath = true;
    }

    /// <summary>编辑器预览：直接摆到"铺完"状态（不经过生命行为）。</summary>
    public void EditorPreview(Vector3 tailWorld, Vector3 tipWorld, float visT = 1f, float layLen = -1f)
    {
        EnsureInit();
        SetPath(tailWorld, tipWorld);
        _want = true;
        _visT = visT;
        _inited = true;
        _layoutTail = _candTail = _curTail = tailWorld;
        _layoutTip = _candTip = _curTip = tipWorld;
        Vector3 d = tipWorld - tailWorld;
        float len = Mathf.Clamp(d.magnitude, minLength, maxLength);
        _layLen = layLen >= 0f ? Mathf.Min(layLen, len) : len;
        _SetFxEnabled(true);
        ManualVisual(0.9f);
        _rend.enabled = true;
    }

    /// <summary>按给定时间推进一次视觉（预览用；运行时由 LateUpdate 调）。</summary>
    public void ManualVisual(float t)
    {
        Vector3 d = _curTip - _curTail;
        float dist = d.magnitude;
        float len = Mathf.Clamp(dist, minLength, maxLength);
        Vector3 dir = dist > 0.05f ? d / dist : Vector3.forward;
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
        dir.Normalize();
        BuildWorldRibbon(_curTail, dir, len, _layLen);

        var c = color;
        float pulse = 1f + pulseAmount * Mathf.Sin(t * pulseHz * Mathf.PI * 2f);
        c.a = Mathf.Clamp01(_visT * pulse);
        _mat.SetColor("_Color", c);
        UpdateBeaconAndComet(t, _curTail, dir, len);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        float t = Time.time;
        bool show = _want && _hasPath;
        _visT = Mathf.MoveTowards(_visT, show ? 1f : 0f, dt * (show ? 5f : 8f));
        bool active = _visT > 0.01f;
        _rend.enabled = active;
        _SetFxEnabled(active);
        if (!active)
        {
            _layLen = 0f;
            _inited = false;
            return;
        }

        // ── 生命行为：观察 → 理会（提交布局）→ 缓速追平 ──
        if (!_inited)
        {
            _inited = true;
            _layoutTail = _candTail = _curTail = _tail;
            _layoutTip = _candTip = _curTip = _tip;
            _candT = 0f;
            _tailVel = _tipVel = Vector3.zero;
        }
        float delta = Vector3.Distance(_tail, _layoutTail) + Vector3.Distance(_tip, _layoutTip);
        if (delta > microDeadzone)
        {
            // 仍在持续变化（候选又新了）→ 重新计时：先"看一会儿"
            if (Vector3.Distance(_tail, _candTail) + Vector3.Distance(_tip, _candTip) > microDeadzone * 0.5f)
                _candT = 0f;
            _candTail = _tail;
            _candTip = _tip;
            _candT += dt;
            if (_candT >= observeTime)
            {
                // 理会：提交新布局；大变化 → 整条重新铺设
                Vector3 oldDir = _layoutTip - _layoutTail;
                Vector3 newDir = _candTip - _candTail;
                bool big = Vector3.Distance(_layoutTip, _candTip) > refreshDistance ||
                           (oldDir.sqrMagnitude > 1e-4f && newDir.sqrMagnitude > 1e-4f &&
                            Vector3.Angle(oldDir, newDir) > refreshAngle);
                _layoutTail = _candTail;
                _layoutTip = _candTip;
                if (big) _layLen = 0f;
                _candT = 0f;
            }
        }
        else
        {
            _candT = 0f;
            _candTail = _tail;
            _candTip = _tip;
        }

        _curTail = Vector3.SmoothDamp(_curTail, _layoutTail, ref _tailVel, followSmoothTime, 10f, dt);
        _curTip = Vector3.SmoothDamp(_curTip, _layoutTip, ref _tipVel, followSmoothTime, 10f, dt);

        Vector3 d = _curTip - _curTail;
        float dist = d.magnitude;
        float len = Mathf.Clamp(dist, minLength, maxLength);
        Vector3 dir = dist > 0.05f ? d / dist : Vector3.forward;
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
        dir.Normalize();

        _layLen = Mathf.MoveTowards(_layLen, show ? len : 0f, laySpeed * dt);

        BuildWorldRibbon(_curTail, dir, len, _layLen);

        var c = color;
        float pulse = 1f + pulseAmount * Mathf.Sin(t * pulseHz * Mathf.PI * 2f);
        c.a = Mathf.Clamp01(_visT * pulse);
        _mat.SetColor("_Color", c);
        UpdateBeaconAndComet(t, _curTail, dir, len);
    }

    void _SetFxEnabled(bool on)
    {
        if (_beaconRend != null) _beaconRend.enabled = on;
        if (_cometRend != null) _cometRend.enabled = on && _layLen > 0.4f;
    }

    /// <summary>信标（路尽头柔和光点，缓慢脉动）+ 流光（星光沿路周期扫过）。</summary>
    void UpdateBeaconAndComet(float t, Vector3 tail, Vector3 dir, float len)
    {
        Vector3 eyePos = eye != null ? eye.position
                       : (Camera.main != null ? Camera.main.transform.position : tail + Vector3.up);
        if (_beacon != null && _beaconRend.enabled)
        {
            Vector3 tipPt = tail + dir * len;
            _beacon.position = tipPt + Vector3.up * 0.06f;
            _beacon.rotation = Quaternion.LookRotation(_beacon.position - eyePos, Vector3.up);
            float bp = 1f + 0.18f * Mathf.Sin(t * 1.5f);
            _beacon.localScale = Vector3.one * (beaconSize * _visT * bp);
            var bm = _beaconRend.sharedMaterial;
            if (bm != null)
                bm.SetColor("_Color", new Color(1f, 1f, 1f, (0.35f + 0.14f * Mathf.Sin(t * 1.5f)) * _visT));
        }
        if (_comet != null && _cometRend.enabled && len > 0.4f)
        {
            float k = Mathf.Repeat(t / cometPeriod, 1f);
            Vector3 p0 = tail, p3 = tail + dir * len;
            Vector3 perp0 = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 c1 = p0 + dir * (len * 0.33f) + perp0 * bow;
            Vector3 c2 = p3 - dir * (len * 0.33f) + perp0 * bow;
            Vector3 p = Bez(p0, c1, c2, p3, k);
            _comet.position = p + Vector3.up * 0.05f;
            _comet.rotation = Quaternion.LookRotation(_comet.position - eyePos, Vector3.up);
            float fade = Mathf.Sin(k * Mathf.PI);
            _comet.localScale = Vector3.one * (cometSize * (0.7f + 0.3f * fade) * _visT);
            var cm = _cometRend.sharedMaterial;
            if (cm != null) cm.SetColor("_Color", new Color(1f, 1f, 1f, fade * 0.85f * _visT));
        }
    }

    // ────────────────────────── 世界曲线几何（贴图承载） ──────────────────────────

    void BuildWorldRibbon(Vector3 tail, Vector3 dir, float totalLen, float laid)
    {
        _mesh.Clear();
        if (laid < 0.03f) return;

        Vector3 p0 = tail;
        Vector3 p3 = tail + dir * totalLen;
        Vector3 perp0 = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 c1 = p0 + dir * (totalLen * 0.33f) + perp0 * bow;
        Vector3 c2 = p3 - dir * (totalLen * 0.33f) + perp0 * bow;

        float fApex = Mathf.Clamp01(laid / totalLen);
        float headBack = headWorldWidth * 0.42f;
        float fBody = Mathf.Clamp01((laid - headBack) / totalLen);

        var verts = new System.Collections.Generic.List<Vector3>(128);
        var uv0 = new System.Collections.Generic.List<Vector2>(128);
        var cols = new System.Collections.Generic.List<Color32>(128);
        var tris = new System.Collections.Generic.List<int>(192);

        // 飘带主体（贴图主体段）
        int b0 = verts.Count;
        for (int i = 0; i <= Seg; i++)
        {
            float k = i / (float)Seg;
            float tt = Mathf.Lerp(0f, fBody, k);
            Vector3 p = Bez(p0, c1, c2, p3, tt);
            Vector3 tan = BezTangent(p0, c1, c2, p3, tt);
            Vector3 side = Vector3.Cross(Vector3.up, tan).normalized;
            float u = Mathf.Lerp(uvBodyMin, uvBodyMax, tt);
            verts.Add(p - side * ribbonHalfWidth);
            verts.Add(p + side * ribbonHalfWidth);
            uv0.Add(new Vector2(u, uvVMin));
            uv0.Add(new Vector2(u, uvVMax));
            cols.Add(new Color32(255, 255, 255, 255));
            cols.Add(new Color32(255, 255, 255, 255));
            if (i < Seg)
            {
                int b = b0 + i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // V 形头：与飘带同一平面（水平面）铺开——一体不分离
        float headAlpha = Mathf.Clamp01((fApex - (1f - headFadeSpan)) / headFadeSpan);
        if (headAlpha > 0.02f)
        {
            Vector3 tipPt = Bez(p0, c1, c2, p3, fApex);
            Vector3 tanEnd = BezTangent(p0, c1, c2, p3, fApex);
            Vector3 fwd = new Vector3(tanEnd.x, 0f, tanEnd.z);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 a = fwd;                                     // 面内指向轴（水平）
            Vector3 b = Vector3.Cross(Vector3.up, a).normalized; // 面内横轴

            float hq = headWorldWidth * ((uvHeadVMax - uvHeadVMin) * 256f) / ((uvHeadMax - uvHeadMin) * 1024f);
            Vector3 ctr = tipPt - a * ((0.851f - 0.5f) * headWorldWidth) - b * ((0.573f - 0.5f) * hq);

            int h = verts.Count;
            AddQuadVerts(verts, uv0, cols, ctr, a, b, headWorldWidth * 0.5f, hq * 0.5f, headAlpha,
                uvHeadMin, uvHeadMax, uvHeadVMin, uvHeadVMax);
            tris.Add(h); tris.Add(h + 2); tris.Add(h + 1);
            tris.Add(h + 1); tris.Add(h + 2); tris.Add(h + 3);
        }

        _mesh.SetVertices(verts);
        _mesh.SetUVs(0, uv0);
        _mesh.SetColors(cols);
        _mesh.SetTriangles(tris, 0);
        _mesh.RecalculateBounds();
    }

    static void AddQuadVerts(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Vector2> uv,
                             System.Collections.Generic.List<Color32> c, Vector3 ctr, Vector3 a, Vector3 b,
                             float halfW, float halfH, float alpha, float uMin, float uMax, float vMin, float vMax)
    {
        byte al = (byte)(Mathf.Clamp01(alpha) * 255f);
        v.Add(ctr - a * halfW - b * halfH); uv.Add(new Vector2(uMin, vMin));
        v.Add(ctr + a * halfW - b * halfH); uv.Add(new Vector2(uMax, vMin));
        v.Add(ctr - a * halfW + b * halfH); uv.Add(new Vector2(uMin, vMax));
        v.Add(ctr + a * halfW + b * halfH); uv.Add(new Vector2(uMax, vMax));
        for (int i = 0; i < 4; i++) c.Add(new Color32(255, 255, 255, al));
    }

    static Vector3 Bez(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3;
    }

    static Vector3 BezTangent(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return (3f * u * u * (c1 - p0) + 6f * u * t * (c2 - c1) + 3f * t * t * (p3 - c2)).normalized;
    }
}
