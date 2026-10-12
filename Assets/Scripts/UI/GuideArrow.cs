// Assets/Scripts/UI/GuideArrow.cs
// 引导箭头 v2（2026-10-12 第二轮用户定调）：
//   "像路一样蜿蜒的箭头，大一点、长一点，直接从视野中心铺到老师方向，半透明"
//   → 程序生成的"渐宽蜿蜒飘带"：尾端在视野中心（细、淡），向老师方向延伸并逐渐变宽变亮，
//     末端一个箭头三角；中段做 S 形蜿蜒；整体半透明（顶点色渐变 × 材质 _Color.a 脉动）。
// 挂中心眼相机下；由 M3DebugDriver 驱动：Show(bool) + SetTarget(worldPos)。
// 形状/材质（长度、弯曲、宽窄、浓淡）全部是 Inspector 字段——"材质后续来调"直接改这些。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GuideArrow : MonoBehaviour
{
    [Header("摆放")]
    [Tooltip("距眼睛的深度（米）")]
    public float distance = 0.55f;

    [Header("形状（蜿蜒飘带）")]
    [Tooltip("总长度（米@depth 平面）——大一点长一点")]
    public float length = 0.34f;
    [Tooltip("S 形蜿蜒幅度（米）")]
    public float curve = 0.045f;
    [Tooltip("尾宽（视野中心端）")]
    public float widthTail = 0.010f;
    [Tooltip("头宽（指向端，箭头前）")]
    public float widthHead = 0.042f;
    [Tooltip("箭头三角长度")]
    public float headLength = 0.075f;
    [Tooltip("箭头三角半宽")]
    public float headHalfWidth = 0.062f;

    [Header("浓淡（顶点色渐变）")]
    [Range(0f, 1f)] public float alphaTail = 0.08f;
    [Range(0f, 1f)] public float alphaHead = 0.85f;

    [Header("整体")]
    [Tooltip("材质（场景注入 ARAttention/GuideArrow 资产）")]
    public Material material;
    public Color color = new Color(1f, 0.84f, 0.32f, 0.85f);
    [Tooltip("整体透明度脉动幅度（0=不闪）")]
    [Range(0f, 0.5f)] public float pulseAmount = 0.15f;
    [Tooltip("脉动频率（次/秒）")]
    public float pulseHz = 0.9f;

    MeshRenderer _rend;
    Material _mat;
    Transform _cam;
    bool _want;
    bool _hasTarget;
    Vector3 _targetWorld;
    float _visT; // 0..1 出现/收起渐进

    void Awake()
    {
        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildRibbonMesh();
        _rend = gameObject.AddComponent<MeshRenderer>();
        _mat = material != null ? new Material(material) : new Material(Shader.Find("ARAttention/GuideArrow"));
        _rend.sharedMaterial = _mat;
        _rend.shadowCastingMode = ShadowCastingMode.Off;
        _rend.receiveShadows = false;
        _rend.enabled = false;

        _cam = GetComponentInParent<Camera>() != null
            ? GetComponentInParent<Camera>().transform
            : (Camera.main != null ? Camera.main.transform : null);
    }

    /// <summary>驱动层：是否满足"需要拉回视线"条件。</summary>
    public void Show(bool on) => _want = on;

    /// <summary>驱动层：目标世界位置（老师/精灵所在）。</summary>
    public void SetTarget(Vector3 worldPos)
    {
        _targetWorld = worldPos;
        _hasTarget = true;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        bool show = _want && _hasTarget && _cam != null;
        _visT = Mathf.MoveTowards(_visT, show ? 1f : 0f, dt * (show ? 4f : 6f));
        if (_visT <= 0.001f)
        {
            if (_rend.enabled) _rend.enabled = false;
            return;
        }
        _rend.enabled = true;

        // 尾端锚在视野中心：整体绕 Z 转到"老师方向"
        Vector3 local = _cam.InverseTransformPoint(_targetWorld);
        var dir = new Vector2(local.x, local.y);
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up; // 目标在正前方（不该显示）兜底
        dir.Normalize();
        transform.localPosition = new Vector3(0f, 0f, distance);
        transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

        // 出现渐进：从中心"长出去"（缩放 X）+ 整体透明度脉动
        float grow = 0.35f + 0.65f * _visT;
        transform.localScale = new Vector3(grow, Mathf.Min(1f, (0.4f + 0.6f * _visT)), 1f);
        float pulse = 1f + pulseAmount * Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f);
        var c = color;
        c.a = color.a * _visT * pulse;
        _mat.SetColor("_Color", c);
    }

    /// <summary>
    /// 程序生成"蜿蜒飘带 + 箭头" mesh（沿 +X，宽窄/浓淡渐变，S 形弯曲）。
    /// 顶点色只写 alpha（形状浓淡），颜色交给材质 _Color。
    /// </summary>
    Mesh BuildRibbonMesh()
    {
        const int seg = 26;
        var verts = new List<Vector3>((seg + 1) * 2 + 3);
        var cols = new List<Color32>((seg + 1) * 2 + 3);
        var tris = new List<int>(seg * 6 + 3);

        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg;
            float x = t * length;
            float y = Mathf.Sin(t * Mathf.PI * 2f) * curve;              // S 形蜿蜒
            float w = Mathf.Lerp(widthTail, widthHead, t * t * (3f - 2f * t)); // 尾细头宽（smoothstep）
            byte a = (byte)(Mathf.Lerp(alphaTail, alphaHead, t) * 255f); // 尾淡头浓
            verts.Add(new Vector3(x, y + w, 0f));
            verts.Add(new Vector3(x, y - w, 0f));
            cols.Add(new Color32(255, 255, 255, a));
            cols.Add(new Color32(255, 255, 255, a));
            if (i < seg)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // 箭头三角：接在飘带头
        float endT = Mathf.Sin(Mathf.PI * 2f) * curve; // = 0
        float endX = length, endY = endT;
        int ib = verts.Count;
        verts.Add(new Vector3(endX + headLength, endY, 0f));                 // 尖
        verts.Add(new Vector3(endX, endY + headHalfWidth, 0f));
        verts.Add(new Vector3(endX, endY - headHalfWidth, 0f));
        byte ha = (byte)(alphaHead * 255f);
        cols.Add(new Color32(255, 255, 255, ha));
        cols.Add(new Color32(255, 255, 255, ha));
        cols.Add(new Color32(255, 255, 255, ha));
        tris.Add(ib); tris.Add(ib + 1); tris.Add(ib + 2);

        var mesh = new Mesh { name = "GuideArrowRibbon" };
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
