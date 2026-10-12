// Assets/Scripts/UI/IpEmoteBubble.cs
// 情绪泡泡（2026-10-12 第八轮，用户定调"对话泡泡 + 表情小动画"）：
//   · 找到老师（Lock）→ 欢呼泡泡（Cheer）：弹性弹出 + 挤压弹跳 + 摇摆 + 周期星光迸发
//   · 陪伴环绕（Companion 持续）→ 哼歌泡泡（Hum）：缓慢摇曳 + 音符 ♪ 飘出消散
//   · 探头（Peek）→ 好奇泡泡（Curious）：歪头摇摆 + 眨眼
//   · 生命周期：PopIn(弹性) → Loop(按情绪动画) → FloatOut(上飘淡出)
//   一切动画程序化（弹性/挤压/摇曳/音符弧线），素材为设计贴图（泡泡/表情/音符）。
//   Tick(now, dt) 架构——运行时 Update；预览脚本可驱动。
using UnityEngine;
using UnityEngine.Rendering;

public class IpEmoteBubble : MonoBehaviour
{
    [Header("材质（场景/预览注入）")]
    public Material bubbleMat;
    public Material cheerMat;
    public Material humMat;
    public Material noteMat;
    public Material starMat;
    public Transform eye;

    [Header("摆放")]
    [Tooltip("相对精灵的锚点（右上——泡泡尾巴指向精灵）")]
    public Vector3 anchorLocal = new Vector3(0.21f, 0.17f, 0.03f);
    public float bubbleSize = 0.30f;
    public float faceSize = 0.22f;

    // 状态
    enum Phase { Hidden, PopIn, Loop, FloatOut }
    Phase _phase = Phase.Hidden;
    string _kind = "Cheer";
    float _phaseT;
    float _loopT;          // Loop 阶段累计（动画相位）
    float _holdT;          // Loop 持续到多久后 FloatOut（<=0 = 不自动结束）
    float _nextStar;       // Cheer 星光节拍
    float _nextNote;       // Hum 音符节拍
    float _nextBlink;      // Curious 眨眼节拍
    float _blinkT = 99f;

    Transform _root, _face;
    MeshRenderer _bubbleR, _faceR;
    readonly Transform[] _notes = new Transform[2];
    readonly MeshRenderer[] _noteR = new MeshRenderer[2];
    readonly Transform[] _stars = new Transform[2];
    readonly MeshRenderer[] _starR = new MeshRenderer[2];
    float[] _noteT = { 99f, 99f };
    float[] _starT = { 99f, 99f };

    // 注意：不作 Awake 自动初始化——本组件由 IpEffects.EnsureBubble() 运行时 AddComponent
    // 后才注入材质，Awake 时字段还是 null 会建出"无贴图白板"（2026-10-12 实测"泡泡一片空白"的根因）。
    void Update() => Tick(Time.time, Time.deltaTime);

    Material _cheerInst, _humInst;

    void EnsureInit()
    {
        if (_bubbleR != null) return;
        var rootGO = new GameObject("BubbleRoot");   // 容器（位置=精灵锚点；不影响精灵本体）
        rootGO.transform.SetParent(transform, false);
        _root = rootGO.transform;

        MakeQuad("Bubble", bubbleMat, _root, out _bubbleR);
        MakeQuad("Face", cheerMat, _root, out _faceR);
        _cheerInst = _faceR.sharedMaterial;
        _humInst = humMat != null ? new Material(humMat) : _cheerInst;
        for (int i = 0; i < 2; i++)
        {
            MakeQuad("Note" + i, noteMat, _root, out _noteR[i]);
            _notes[i] = _noteR[i].transform;
            MakeQuad("Spark" + i, starMat, _root, out _starR[i]);
            _stars[i] = _starR[i].transform;
        }
        SetAlpha(0f);
    }

    void MakeQuad(string name, Material mat, Transform parent, out MeshRenderer rend)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
        rend = go.AddComponent<MeshRenderer>();
        var m = mat != null ? new Material(mat) : new Material(Shader.Find("ARAttention/GuideArrow"));
        rend.sharedMaterial = m;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;
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

    /// <summary>播放一个情绪泡泡（Cheer/Hum/Curious）。holdSec≤0 表示一直循环直到被替换/隐藏。</summary>
    public void Play(string kind, float holdSec = 3.2f)
    {
        EnsureInit();
        _kind = kind;
        _phase = Phase.PopIn;
        _phaseT = 0f;
        _loopT = 0f;
        _holdT = holdSec;
        _nextStar = 0.35f;
        _nextNote = 0.25f;
        _nextBlink = 2.5f;
        _blinkT = 99f;
        // 表情贴图切换（实例已缓存，不重复分配）
        if (_kind == "Hum" && _humInst != null) _faceR.sharedMaterial = _humInst;
        else if (_cheerInst != null) _faceR.sharedMaterial = _cheerInst;
    }

    public void Hide()
    {
        _phase = Phase.Hidden;
        SetAlpha(0f);
    }

    void SetAlpha(float a)
    {
        SetQuadAlpha(_bubbleR, a);
        SetQuadAlpha(_faceR, a);
    }

    static void SetQuadAlpha(MeshRenderer r, float a)
    {
        if (r == null) return;
        r.enabled = a > 0.01f;
        var m = r.sharedMaterial;
        if (m != null) m.SetColor("_Color", new Color(1f, 1f, 1f, a));
    }

    static void SetQuadAlpha(MeshRenderer r, Color c)
    {
        if (r == null) return;
        r.enabled = c.a > 0.01f;
        var m = r.sharedMaterial;
        if (m != null) m.SetColor("_Color", c);
    }

    /// <summary>核心推进（运行时每帧 / 预览可推进）。</summary>
    public void Tick(float now, float dt)
    {
        EnsureInit();
        if (_phase == Phase.Hidden)
        {
            SetAlpha(0f);
            foreach (var r in _noteR) if (r != null) r.enabled = false;
            foreach (var r in _starR) if (r != null) r.enabled = false;
            return;
        }

        _phaseT += dt;

        // ── 根摆放：精灵锚点 + 朝向观察者 ──
        Vector3 eyePos = eye != null ? eye.position : (Camera.main != null ? Camera.main.transform.position : transform.position + Vector3.up);
        Vector3 anchor = transform.position + anchorLocal;
        _root.position = anchor;
        _root.rotation = Quaternion.LookRotation(_root.position - eyePos, Vector3.up);

        // ── 生命周期 ──
        float alpha;
        float scale = 1f;
        float squashY = 1f, tilt = 0f;
        switch (_phase)
        {
            case Phase.PopIn:
            {
                float k = Mathf.Clamp01(_phaseT / 0.38f);
                float back = 1f + 2.70158f * Mathf.Pow(k - 1f, 3f) + 1.70158f * Mathf.Pow(k - 1f, 2f); // easeOutBack
                scale = back;
                alpha = Mathf.Clamp01(_phaseT / 0.18f);
                if (k >= 1f) { _phase = Phase.Loop; _phaseT = 0f; }
                break;
            }
            case Phase.Loop:
            {
                _loopT += dt;
                alpha = 1f;
                switch (_kind)
                {
                    case "Hum": // 哼歌：缓慢摇曳 + 呼吸
                        tilt = Mathf.Sin(_loopT * 1.6f) * 5f;
                        squashY = 1f + Mathf.Sin(_loopT * 3.2f) * 0.03f;
                        break;
                    case "Curious": // 好奇：歪头 + 偶发眨眼
                        tilt = Mathf.Sin(_loopT * 0.9f) * 8f;
                        break;
                    default: // Cheer：Q 弹欢跳 + 摇摆
                        tilt = Mathf.Sin(_loopT * 4f) * 4f;
                        squashY = 1f + Mathf.Abs(Mathf.Sin(_loopT * 4f * Mathf.PI)) * 0.06f;
                        break;
                }
                if (_holdT > 0f && _phaseT >= _holdT) { _phase = Phase.FloatOut; _phaseT = 0f; }
                break;
            }
            default: // FloatOut
            {
                float k = Mathf.Clamp01(_phaseT / 1.0f);
                alpha = 1f - k;
                _root.position = anchor + Vector3.up * (0.22f * k);
                scale = 1f + 0.08f * k;
                tilt = Mathf.Sin(_loopT) * 3f * (1f - k);
                if (k >= 1f) { _phase = Phase.Hidden; return; }
                break;
            }
        }

        // ── 泡泡 + 表情 ──
        _root.localScale = new Vector3(scale, scale * squashY, 1f);
        _root.rotation = _root.rotation * Quaternion.Euler(0f, 0f, tilt);
        _bubbleR.transform.localPosition = Vector3.zero;
        _bubbleR.transform.localScale = Vector3.one * bubbleSize;
        _faceR.transform.localPosition = new Vector3(0f, -0.005f, -0.01f);
        _faceR.transform.localScale = Vector3.one * faceSize;
        // 眨眼（Curious/通用）
        if (_kind == "Curious" && _phase == Phase.Loop)
        {
            _nextBlink -= dt;
            if (_nextBlink <= 0f) { _blinkT = 0f; _nextBlink = Random.Range(2.5f, 4.5f); }
        }
        if (_blinkT < 0.14f)
        {
            _blinkT += dt;
            float bk = 1f - Mathf.Sin(Mathf.Clamp01(_blinkT / 0.14f) * Mathf.PI) * 0.82f;
            _faceR.transform.localScale = new Vector3(faceSize, faceSize * bk, 1f);
        }
        SetQuadAlpha(_bubbleR, alpha);
        SetQuadAlpha(_faceR, alpha);

        // ── 星光迸发（Cheer）──
        if (_kind != "Hum" && _phase == Phase.Loop)
        {
            _nextStar -= dt;
            if (_nextStar <= 0f)
            {
                _nextStar = 0.55f;
                int slot = _starT[0] > _starT[1] ? 0 : 1;
                _starT[slot] = 0f;
            }
        }
        for (int i = 0; i < 2; i++)
        {
            if (_starT[i] > 0.45f || _starR[i] == null) { if (_starR[i] != null) _starR[i].enabled = false; continue; }
            _starT[i] += dt;
            float k = Mathf.Clamp01(_starT[i] / 0.45f);
            float corner = i == 0 ? -1f : 1f;
            _stars[i].localPosition = new Vector3(corner * bubbleSize * 0.52f, bubbleSize * 0.42f, -0.01f);
            _stars[i].localScale = Vector3.one * (0.09f * Mathf.Sin(k * Mathf.PI));
            SetQuadAlpha(_starR[i], new Color(1f, 1f, 1f, Mathf.Sin(k * Mathf.PI) * alpha));
        }

        // ── 音符飘出（Hum）：从泡泡里飘出、上扬、消散 ──
        if (_kind == "Hum" && _phase == Phase.Loop)
        {
            _nextNote -= dt;
            if (_nextNote <= 0f)
            {
                _nextNote = 1.15f;
                int slot = _noteT[0] > _noteT[1] ? 0 : 1;
                _noteT[slot] = 0f;
                if (_noteR[slot] != null) _noteR[slot].sharedMaterial.SetColor(
                    "_Color", slot == 0 ? new Color(1f, 1f, 1f, 1f) : new Color(0.75f, 1f, 0.95f, 1f)); // 第二枚偏青
            }
        }
        for (int i = 0; i < 2; i++)
        {
            if (_noteT[i] > 1.4f || _noteR[i] == null) { if (_noteR[i] != null) _noteR[i].enabled = false; continue; }
            _noteT[i] += dt;
            float k = Mathf.Clamp01(_noteT[i] / 1.4f);
            float side = i == 0 ? 1f : -1f;
            Vector3 p = new Vector3(
                side * (bubbleSize * 0.38f + k * bubbleSize * 0.7f),
                bubbleSize * 0.36f + k * bubbleSize * 0.95f,
                -0.01f);
            _notes[i].localPosition = p;
            _notes[i].localRotation = Quaternion.Euler(0f, 0f, side * 14f * Mathf.Sin(k * Mathf.PI));
            _notes[i].localScale = Vector3.one * (0.075f * (0.7f + 0.3f * Mathf.Sin(k * Mathf.PI)));
            SetQuadAlpha(_noteR[i], new Color(
                _noteR[i].sharedMaterial.GetColor("_Color").r,
                _noteR[i].sharedMaterial.GetColor("_Color").g,
                _noteR[i].sharedMaterial.GetColor("_Color").b,
                (1f - k) * 0.9f * alpha));
        }
    }
}
