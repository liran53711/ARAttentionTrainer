// Assets/Scripts/UI/IpEffects.cs
// 精灵特效系统（2026-10-12 第七轮）：温和半透明 + 有动画的柔光/粒子/微动画（无 2D 贴图质感）。
// 12 个创意中精灵侧的实装（对应状态由 IPWorldView 驱动）：
//   1 出场：上方柔光尘埃向下聚拢 + 出场小跳（微动画）
//   2 稳定锁定：一圈柔光环扩散（Pulse "Lock"）
//   3 引导飞行：缓慢上升的星尘痕迹（世界空间粒子，低频）
//   4 注视达成：柔环 + 一小簇星尘（Pulse "Achieve"）
//   5 陪伴期：2-3 只"萤火虫"柔光点绕精灵缓飞 + 呼吸光晕
//   6 正反馈：暖金柔环 + 星尘爆发（Pulse "Feedback"）+ 弹跳微动画
//   7 探头：轻微侧摆（微动画）+ 星尘细尾
//   10 等待注视：极淡呼吸光晕（存在感、不打扰）
//   12 待机：缓慢漂浮 + 偶尔"困困"下沉再弹起
// 架构：所有动画逻辑集中在 Tick(now, dt)——运行时 Update 调用；预览脚本可直接驱动（批量渲染）。
using UnityEngine;
using UnityEngine.Rendering;

public class IpEffects : MonoBehaviour
{
    [Header("材质（场景/预览注入：柔光/柔星/柔环）")]
    public Material glowMat;
    public Material starMat;
    public Material ringMat;
    [Tooltip("观察者（中心眼相机）")]
    public Transform eye;
    [Header("情绪泡泡素材（场景/预览注入）")]
    public Material bubbleMat, cheerMat, humMat, noteMat;

    [Header("尺寸/节奏")]
    public float haloSize = 0.85f;
    public float ringStartSize = 0.3f, ringEndSize = 1.15f, ringDuration = 0.95f;
    public float fireflySize = 0.10f, fireflyRadius = 0.26f, fireflySpeed = 0.55f;
    public float dustSize = 0.075f;

    // 运行时
    IPWorldView.IPAnimState _mood = IPWorldView.IPAnimState.Hidden;
    float _moodT;
    float _ringT = 99f;
    int _ringKind;                      // 0紫 1金
    float _haloAlpha;
    float _flyAlpha;

    Transform _halo, _ring;
    MeshRenderer _haloR, _ringR;
    readonly Transform[] _fly = new Transform[3];
    readonly MeshRenderer[] _flyR = new MeshRenderer[3];
    ParticleSystem _dust;

    /// <summary>供 IPWorldView 叠加的"表情小动画"位移（弹跳/困困下沉等）。</summary>
    public Vector3 ModelMicroOffset { get; private set; }

    void Awake() => EnsureInit();
    void Update() => Tick(Time.time, Time.deltaTime);

    void EnsureInit()
    {
        if (_haloR != null) return;
        _halo = MakeQuad("Halo", glowMat, out _haloR);
        _ring = MakeQuad("Ring", ringMat, out _ringR);
        for (int i = 0; i < 3; i++)
            _fly[i] = MakeQuad("Firefly" + i, starMat != null ? starMat : glowMat, out _flyR[i]);
        BuildDust();
    }

    IpEmoteBubble _bubble;
    float _compT;              // 陪伴持续时长（哼歌节拍）
    float _nextHum = 10f;

    /// <summary>情绪泡泡组件（预览脚本用）。</summary>
    public IpEmoteBubble Bubble => _bubble;

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

    void EnsureBubble()
    {
        if (_bubble != null) return;
        _bubble = gameObject.GetComponent<IpEmoteBubble>();
        if (_bubble == null) _bubble = gameObject.AddComponent<IpEmoteBubble>();
        _bubble.bubbleMat = bubbleMat;
        _bubble.cheerMat = cheerMat;
        _bubble.humMat = humMat;
        _bubble.noteMat = noteMat;
        _bubble.starMat = starMat;
        _bubble.eye = eye;
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

    void BuildDust()
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        _dust = go.AddComponent<ParticleSystem>();
        _dust.Stop();
        var main = _dust.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(dustSize * 0.5f, dustSize * 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.88f, 0.78f, 1f, 0.5f), new Color(1f, 1f, 1f, 0.3f));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // 拖尾留在旅途上
        var em = _dust.emission; em.rateOverTime = 0f;
        var sh = _dust.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
        var col = _dust.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.65f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
        var sol = _dust.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 0.15f));
        var vel = _dust.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); // 缓慢上浮
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = starMat != null ? starMat : glowMat;
        rend.renderMode = ParticleSystemRenderMode.Billboard;
    }

    /// <summary>状态切换（由 IPWorldView 驱动）。</summary>
    public void SetMood(IPWorldView.IPAnimState s)
    {
        if (_mood == s) return;
        var prev = _mood;
        _mood = s;
        _moodT = 0f;
        if (prev == IPWorldView.IPAnimState.Companion) _compT = 0f;
        if (s == IPWorldView.IPAnimState.Peek)   // 探头 → 好奇泡泡
        {
            EnsureBubble();
            _bubble.Play("Curious", 2.2f);
        }
    }

    /// <summary>一次性脉冲：Lock（锁定）/ Achieve（注视达成）/ Feedback（正反馈，金色）/ ReGuide。</summary>
    public void Pulse(string kind)
    {
        _ringT = 0f;
        _ringKind = kind == "Feedback" ? 1 : 0;
        if (_dust == null) return;
        switch (kind)
        {
            case "Appear":   EmitDust(18, -0.6f, new Color(0.9f, 0.82f, 1f, 0.5f)); break; // 聚拢：向下
            case "Lock":
                EmitDust(10, 0.25f, new Color(0.9f, 0.85f, 1f, 0.5f));
                EnsureBubble();
                _bubble.Play("Cheer", 3.4f);   // "找到老师！"→ 欢呼泡泡（用户定调）
                break;
            case "Achieve":  EmitDust(14, 0.35f, new Color(0.92f, 0.86f, 1f, 0.55f)); break;
            case "Feedback": EmitDust(30, 0.55f, new Color(1f, 0.92f, 0.6f, 0.6f)); break; // 金色星尘
            case "ReGuide":  EmitDust(10, 0.3f, new Color(0.9f, 0.85f, 1f, 0.5f)); break;
        }
    }

    void EmitDust(int n, float upSpeed, Color c)
    {
        if (_dust == null) return;
        var ep = new ParticleSystem.EmitParams
        {
            startColor = c,
            velocity = new Vector3(Random.Range(-0.1f, 0.1f), upSpeed, Random.Range(-0.1f, 0.1f)),
        };
        for (int i = 0; i < n; i++) _dust.Emit(ep, 1);
    }

    /// <summary>预览助手：一次性爆发（批量渲染用；运行时不需要）。</summary>
    public void PreviewBurst(int n, Color c, float advance = 0.45f)
    {
        EnsureInit();
        var em = _dust.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)n) });
        var main = _dust.main;
        main.startColor = new ParticleSystem.MinMaxGradient(c);
        var vel = _dust.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(-0.25f, 0.35f);
        _dust.Simulate(advance, true, true);
    }

    /// <summary>预览助手：持续发射（拖尾类，批量渲染用）。</summary>
    public void PreviewEmit(float rate, float advance)
    {
        EnsureInit();
        var em = _dust.emission;
        em.rateOverTime = rate;
        _dust.Simulate(advance, true, true);
    }

    /// <summary>核心推进（运行时每帧；预览脚本可多次调用以推进时间）。</summary>
    public void Tick(float now, float dt)
    {
        EnsureInit();
        _moodT += dt;

        // ── 光晕（呼吸；强度按状态）──
        float haloTo;
        switch (_mood)
        {
            case IPWorldView.IPAnimState.Guiding:   haloTo = 0.34f + 0.10f * Mathf.Sin(now * 1.6f); break;
            case IPWorldView.IPAnimState.Companion: haloTo = 0.24f + 0.08f * Mathf.Sin(now * 1.1f); break;
            case IPWorldView.IPAnimState.Feedback:  haloTo = 0.6f + 0.15f * Mathf.Sin(now * 6f); break;
            case IPWorldView.IPAnimState.Appearing: haloTo = 0.5f; break;
            case IPWorldView.IPAnimState.Peek:      haloTo = 0.3f; break;
            default:                                haloTo = 0.10f + 0.04f * Mathf.Sin(now * 0.7f); break; // Hidden/待机微光
        }
        float haloHidden = 0.07f + 0.03f * Mathf.Sin(now * 0.8f); // 等待注视：极淡呼吸光（存在感、不打扰）
        _haloAlpha = Mathf.MoveTowards(_haloAlpha, (_mood == IPWorldView.IPAnimState.Hidden) ? haloHidden : haloTo, dt * 2f);
        if (_haloR != null)
        {
            _haloR.enabled = _haloAlpha > 0.01f;
            if (_haloR.enabled)
            {
                Vector3 eyePos = EyePos();
                _halo.position = transform.position + (transform.position - eyePos).normalized * 0.06f;
                _halo.rotation = Quaternion.LookRotation(_halo.position - eyePos, Vector3.up);
                float hs = haloSize * (1f + 0.05f * Mathf.Sin(now * 1.6f));
                _halo.localScale = Vector3.one * hs;
                var m = _haloR.sharedMaterial;
                if (m != null) m.SetColor("_Color", new Color(0.85f, 0.78f, 1f, _haloAlpha));
            }
        }

        // ── 萤火虫（陪伴/待机时 2-3 只绕飞）──
        float flyTo = _mood == IPWorldView.IPAnimState.Companion ? 1f : 0f;
        _flyAlpha = Mathf.MoveTowards(_flyAlpha, flyTo, dt * 1.5f);
        for (int i = 0; i < 3; i++)
        {
            if (_flyR[i] == null) continue;
            bool on = _flyAlpha > 0.01f && i < (_mood == IPWorldView.IPAnimState.Companion ? 3 : 2);
            _flyR[i].enabled = on;
            if (!on) continue;
            float ang = now * fireflySpeed * (1f + i * 0.23f) + i * 2.1f;
            float r = fireflyRadius * (0.85f + 0.15f * Mathf.Sin(now * 0.7f + i));
            Vector3 p = transform.position + new Vector3(
                Mathf.Cos(ang) * r,
                Mathf.Sin(now * 0.9f + i * 1.7f) * 0.09f + 0.05f,
                Mathf.Sin(ang) * r);
            _fly[i].position = p;
            _fly[i].rotation = Quaternion.LookRotation(p - EyePos(), Vector3.up);
            float tw = 0.55f + 0.4f * Mathf.Sin(now * 2.3f + i * 1.9f); // 闪烁
            _fly[i].localScale = Vector3.one * (fireflySize * tw);
            var m = _flyR[i].sharedMaterial;
            if (m != null) m.SetColor("_Color", new Color(1f, 0.98f, 0.85f, _flyAlpha * tw * 0.9f));
        }

        // ── 脉冲环 ──
        if (_ringT < ringDuration && _ringR != null)
        {
            _ringT += dt;
            float k = Mathf.Clamp01(_ringT / ringDuration);
            float ease = 1f - (1f - k) * (1f - k); // easeOut
            _ringR.enabled = k < 1f;
            if (_ringR.enabled)
            {
                Vector3 eyePos = EyePos();
                _ring.position = transform.position;
                _ring.rotation = Quaternion.LookRotation(_ring.position - eyePos, Vector3.up);
                _ring.localScale = Vector3.one * Mathf.Lerp(ringStartSize, ringEndSize, ease);
                var m = _ringR.sharedMaterial;
                Color rc = _ringKind == 1 ? new Color(1f, 0.9f, 0.62f, (1f - k) * 0.85f)
                                          : new Color(0.85f, 0.78f, 1f, (1f - k) * 0.7f);
                if (m != null) m.SetColor("_Color", rc);
            }
        }
        else if (_ringR != null) _ringR.enabled = false;

        // ── 陪伴环绕 → 哼歌泡泡（持续陪伴 8s 后开始，周期 14~22s）──
        _compT = _mood == IPWorldView.IPAnimState.Companion ? _compT + dt : 0f;
        if (_mood == IPWorldView.IPAnimState.Companion && _compT > 8f && now >= _nextHum)
        {
            _nextHum = now + Random.Range(14f, 22f);
            EnsureBubble();
            _bubble.Play("Hum", 4.5f);   // 愉快哼歌（用户定调）
        }

        // ── 星尘持续发射（飞行拖尾 / 待机轻尘）──
        if (_dust != null)
        {
            var em = _dust.emission;
            em.rateOverTime = _mood == IPWorldView.IPAnimState.Guiding ? 7f
                            : _mood == IPWorldView.IPAnimState.Appearing ? 10f
                            : _mood == IPWorldView.IPAnimState.Peek ? 5f
                            : 0f;
        }

        // ── 表情小动画（微位移，供 IPWorldView 叠加）──
        float my = 0f;
        switch (_mood)
        {
            case IPWorldView.IPAnimState.Feedback: // 开心弹跳
                my = Mathf.Abs(Mathf.Sin(_moodT * 9f)) * 0.035f;
                break;
            case IPWorldView.IPAnimState.Appearing: // 出场小跳
                my = Mathf.Sin(Mathf.Min(_moodT, 0.6f) * 14f) * 0.02f * Mathf.Max(0f, 1f - _moodT / 0.6f);
                break;
            case IPWorldView.IPAnimState.Peek: // 探头轻摆（由 IPWorldView 的朝向处理，这里只给一点上下）
                my = Mathf.Sin(now * 3f) * 0.008f;
                break;
            case IPWorldView.IPAnimState.Companion: // 漂浮呼吸 + 偶尔"困困"下沉再弹起
            {
                float p = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(now * Mathf.PI / 7f)), 8f);
                my = Mathf.Sin(now * 1.1f) * 0.008f - 0.018f * p;
                break;
            }
            case IPWorldView.IPAnimState.Guiding:
                my = Mathf.Sin(now * 1.6f) * 0.010f;
                break;
        }
        ModelMicroOffset = new Vector3(0f, my, 0f);
    }

    Vector3 EyePos()
    {
        if (eye != null) return eye.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return transform.position + Vector3.up * 2f + Vector3.forward;
    }
}
