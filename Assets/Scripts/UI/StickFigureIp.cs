// Assets/Scripts/UI/StickFigureIp.cs
// 火柴人小精灵（2026-10-11 用户定调）：组员正式 3D 模型到位前的"接口占位体"。
// 设计要点（对应用户验收标准：像 First Encounters 那样"位置稳定、移动逻辑合理"）：
//   · 姿态 = 关节角度目标 + SmoothDamp 平滑混合——状态切换不闪跳；
//   · 透明度 = 材质 alpha 平滑过渡——不靠 SetActive/缩放闪现；
//   · 根节点位置/朝向/缩放由 IPWorldView 统一驱动（与未来 FBX 模型完全同一接口）；
//   · 动作频率刻意放低（0.2~0.7Hz 起伏 + 偶尔招手），避免"小人动作太快"。
// 组员模型到位后：把 IPWorldView.model3D 指向 FBX 根节点即可；本类各状态的"姿态语言"
// 可作为 Animator 状态机的对照（参数名见 docs/specs/2026-10-10-ip-3d-model-requirements.md）。
using UnityEngine;
using UnityEngine.Rendering;

public class StickFigureIp : MonoBehaviour
{
    [Header("材质（场景装配器注入 URP/Unlit 资产，保证着色器进构建包）")]
    public Material bodyMaterial;
    public Material eyeMaterial;

    [Header("平滑参数")]
    [Tooltip("关节姿态平滑时间（秒）——越小反应越快，0.18 左右自然")]
    public float poseSmoothTime = 0.18f;
    [Tooltip("透明度平滑时间（秒）")]
    public float alphaSmoothTime = 0.12f;

    // ── 骨架（运行时程序化搭建，实际身高 ≈0.74m——IPWorldView 按 modelNativeHeight 归一缩放）──
    Transform _mid;                         // 中心对齐层：让"根原点=身体几何中心"（与需求文档一致）
    Transform _body;                        // 躯干+头+四肢的共同父级（局部起伏/倾斜加在它身上，不与世界定位打架）
    Transform _head;
    Transform _shoulderL, _shoulderR, _hipL, _hipR;

    Material _bodyMat, _eyeMat;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly Color BaseBodyColor = new Color(0.62f, 0.47f, 1f);
    static readonly Color BaseEyeColor = new Color(0.12f, 0.10f, 0.20f);

    // ── 关节通道（当前值+速度）：SmoothDamp 逐帧混合，杜绝姿态瞬变 ──
    float _armOutL, _armOutLV, _armOutR, _armOutRV; // 双臂侧举（度，正=抬起）
    float _armFwd, _armFwdV;      // 双臂前摆（度，正=向前）
    float _legBack, _legBackV;    // 双腿后摆（度，正=向后拖）
    float _leanX, _leanXV;        // 躯干前倾（度，正=前倾）
    float _leanZ, _leanZV;        // 躯干侧倾（度）
    float _headTilt, _headTiltV;  // 头部侧倾（度）

    // ── 挥手调度（仅陪伴态偶尔招手）──
    const float WaveDur = 1.4f;
    float _waveTimer = 3f;
    float _waveT = -1f;           // <0 = 未在挥手；否则 0..WaveDur
    float _waveSide = 1f;         // ±1 选一只手臂

    // ── 视觉输入（IPWorldView 每帧推送）──
    IPWorldView.IPAnimState _state = IPWorldView.IPAnimState.Hidden;
    float _alpha, _alphaTarget, _alphaVel;
    Color _tint = Color.white;

    /// <summary>IPWorldView 每帧推送：动画状态（已去抖）/透明度/色调。</summary>
    public void SetVisuals(IPWorldView.IPAnimState state, float alpha, Color tint)
    {
        _state = state;
        _alphaTarget = Mathf.Clamp01(alpha);
        _tint = tint;
    }

    void Awake() => Build();

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        var p = PoseFor(_state);

        // ── 挥手调度（陪伴态每 4.5~8s 招一次手；离开陪伴立即取消）──
        if (_state == IPWorldView.IPAnimState.Companion)
        {
            _waveTimer -= dt;
            if (_waveTimer <= 0f && _waveT < 0f)
            {
                _waveT = 0f;
                _waveTimer = Random.Range(4.5f, 8f);
                _waveSide = Random.value < 0.5f ? -1f : 1f;
            }
        }
        else _waveT = -1f;

        float waveL = 0f, waveR = 0f;
        if (_waveT >= 0f)
        {
            float env = WaveEnvelope(_waveT);
            float amt = 112f * env + Mathf.Sin(_waveT * 8.5f) * 9f * env; // 举起后小幅摆动
            if (_waveSide > 0f) waveR = amt; else waveL = amt;
            _waveT += dt;
            if (_waveT >= WaveDur) _waveT = -1f;
        }

        // ── 关节平滑混合（目标 = 状态姿态 + 挥手叠加）──
        _armOutL = Mathf.SmoothDamp(_armOutL, p.armOut + waveL, ref _armOutLV, poseSmoothTime, Mathf.Infinity, dt);
        _armOutR = Mathf.SmoothDamp(_armOutR, p.armOut + waveR, ref _armOutRV, poseSmoothTime, Mathf.Infinity, dt);
        _armFwd = Mathf.SmoothDamp(_armFwd, p.armFwd, ref _armFwdV, poseSmoothTime, Mathf.Infinity, dt);
        _legBack = Mathf.SmoothDamp(_legBack, p.legBack, ref _legBackV, poseSmoothTime, Mathf.Infinity, dt);
        _leanX = Mathf.SmoothDamp(_leanX, p.leanX, ref _leanXV, poseSmoothTime, Mathf.Infinity, dt);
        _leanZ = Mathf.SmoothDamp(_leanZ, p.leanZ, ref _leanZV, poseSmoothTime, Mathf.Infinity, dt);
        _headTilt = Mathf.SmoothDamp(_headTilt, p.headTilt, ref _headTiltV, poseSmoothTime, Mathf.Infinity, dt);

        // ── 套用到骨架 ──
        if (_shoulderL != null)
        {
            _shoulderL.localRotation = Quaternion.Euler(-_armFwd, 0f, -_armOutL);
            _shoulderR.localRotation = Quaternion.Euler(-_armFwd, 0f, _armOutR);
            _hipL.localRotation = Quaternion.Euler(_legBack, 0f, -3f);
            _hipR.localRotation = Quaternion.Euler(_legBack, 0f, 3f);
        }
        if (_body != null)
        {
            _body.localPosition = new Vector3(0f, Mathf.Sin(Time.time * p.bobSpeed) * p.bobAmp, 0f);
            _body.localRotation = Quaternion.Euler(
                _leanX, 0f, _leanZ + Mathf.Sin(Time.time * p.bobSpeed * 0.6f) * 1.5f);
        }
        if (_head != null)
            _head.localRotation = Quaternion.Euler(-_leanX * 0.35f, 0f, _headTilt);

        // ── 透明度平滑（材质 alpha——不靠开关闪现）──
        _alpha = Mathf.SmoothDamp(_alpha, _alphaTarget, ref _alphaVel, alphaSmoothTime, Mathf.Infinity, dt);
        if (_bodyMat != null)
        {
            var c = Color.Lerp(BaseBodyColor, _tint, 0.65f); // 反馈脉冲的暖色 tint 柔和叠加
            c.a = _alpha;
            _bodyMat.SetColor(BaseColorId, c);
            _bodyMat.SetColor(ColorId, c);
        }
        if (_eyeMat != null)
        {
            var e = BaseEyeColor; e.a = _alpha;
            _eyeMat.SetColor(BaseColorId, e);
            _eyeMat.SetColor(ColorId, e);
        }
    }

    static float WaveEnvelope(float t)
    {
        float rise = Mathf.Clamp01(t / 0.25f);
        float fall = Mathf.Clamp01((WaveDur - t) / 0.3f);
        rise = rise * rise * (3f - 2f * rise);
        fall = fall * fall * (3f - 2f * fall);
        return Mathf.Min(rise, fall);
    }

    // ── 各状态的目标姿态（"姿态语言"——组员 Animator 的对照）──
    struct Pose
    {
        public float armOut, armFwd, legBack, leanX, leanZ, headTilt;
        public float bobAmp, bobSpeed;
    }

    static Pose PoseFor(IPWorldView.IPAnimState s)
    {
        switch (s)
        {
            case IPWorldView.IPAnimState.Appearing: // 登场：双臂高举、微微后仰（惊喜感）
                return new Pose { armOut = 118f, armFwd = -12f, legBack = 10f, leanX = -6f, headTilt = 7f, bobAmp = 0.020f, bobSpeed = 3.0f };
            case IPWorldView.IPAnimState.Guiding:   // 引导：飞行姿态——前倾、手臂前伸、腿向后拖
                return new Pose { armOut = 18f, armFwd = 42f, legBack = 26f, leanX = 18f, headTilt = 0f, bobAmp = 0.010f, bobSpeed = 1.6f };
            case IPWorldView.IPAnimState.Companion: // 陪伴：放松悬浮 + 偶尔招手
                return new Pose { armOut = 10f, armFwd = 2f, legBack = 4f, leanX = 4f, headTilt = 5f, bobAmp = 0.012f, bobSpeed = 1.1f };
            case IPWorldView.IPAnimState.Attentive: // 注视：安静陪伴（无招手）
                return new Pose { armOut = 8f, armFwd = 2f, legBack = 4f, leanX = 4f, headTilt = 5f, bobAmp = 0.010f, bobSpeed = 1.0f };
            case IPWorldView.IPAnimState.Listening: // 倾听：轻微前倾/歪头（建模师 Listen 前的占位）
                return new Pose { armOut = 10f, armFwd = 8f, legBack = 4f, leanX = 5f, headTilt = 10f, bobAmp = 0.008f, bobSpeed = 0.9f };
            case IPWorldView.IPAnimState.Feedback:  // 正反馈：欢呼（双臂高举 + 轻跳）
                return new Pose { armOut = 150f, armFwd = 8f, legBack = 6f, leanX = -10f, headTilt = -6f, bobAmp = 0.028f, bobSpeed = 3.6f };
            case IPWorldView.IPAnimState.Peek:      // 探头：侧身探出（leanZ 侧倾）
                return new Pose { armOut = 30f, armFwd = 20f, legBack = 12f, leanX = 8f, leanZ = 14f, headTilt = 10f, bobAmp = 0.010f, bobSpeed = 2.0f };
            default:                                 // Hidden/未知：自然垂放
                return new Pose { armOut = 6f, headTilt = 0f, bobAmp = 0.006f, bobSpeed = 0.8f };
        }
    }

    // ── 骨架搭建（局部系：脚 0.23 ~ 头顶 0.97，身高 ≈0.74m；经 Mid 层平移后原点=身体几何中心）──
    void Build()
    {
        _bodyMat = MakeInstance(bodyMaterial, BaseBodyColor);
        _eyeMat = MakeInstance(eyeMaterial, BaseEyeColor);

        _mid = new GameObject("Mid").transform;
        _mid.SetParent(transform, false);
        _mid.localPosition = new Vector3(0f, -0.60f, 0f); // 骨架原系中身体中心 ≈0.60 → 平移到根原点

        _body = new GameObject("Body").transform;
        _body.SetParent(_mid, false);

        // 头 + 双眼（面向 +Z——与 IPWorldView 的朝向约定一致）
        _head = Prim(PrimitiveType.Sphere, _body, "Head", new Vector3(0f, 0.885f, 0f), _bodyMat).transform;
        _head.localScale = Vector3.one * 0.170f;
        var eyeL = Prim(PrimitiveType.Sphere, _body, "EyeL", new Vector3(-0.033f, 0.905f, 0.068f), _eyeMat).transform;
        var eyeR = Prim(PrimitiveType.Sphere, _body, "EyeR", new Vector3(0.033f, 0.905f, 0.068f), _eyeMat).transform;
        eyeL.localScale = Vector3.one * 0.030f;
        eyeR.localScale = Vector3.one * 0.030f;

        // 躯干（胶囊：直径 0.09、高 0.26）
        var torso = Prim(PrimitiveType.Capsule, _body, "Torso", new Vector3(0f, 0.63f, 0f), _bodyMat).transform;
        torso.localScale = new Vector3(0.090f, 0.130f, 0.090f);

        // 肩关节（枢轴）+ 手臂（直径 0.052、长 0.20）
        _shoulderL = Joint("ShoulderL", new Vector3(-0.062f, 0.72f, 0f));
        _shoulderR = Joint("ShoulderR", new Vector3(0.062f, 0.72f, 0f));
        Limb(_shoulderL, "ArmL", new Vector3(0.052f, 0.100f, 0.052f), -0.10f);
        Limb(_shoulderR, "ArmR", new Vector3(0.052f, 0.100f, 0.052f), -0.10f);

        // 髋关节 + 腿（直径 0.06、长 0.23——风格化单节）
        _hipL = Joint("HipL", new Vector3(-0.045f, 0.46f, 0f));
        _hipR = Joint("HipR", new Vector3(0.045f, 0.46f, 0f));
        Limb(_hipL, "LegL", new Vector3(0.060f, 0.115f, 0.060f), -0.11f);
        Limb(_hipR, "LegR", new Vector3(0.060f, 0.115f, 0.060f), -0.11f);
    }

    Transform Joint(string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_body, false);
        t.localPosition = localPos;
        return t;
    }

    void Limb(Transform pivot, string name, Vector3 scale, float yOffset)
    {
        var t = Prim(PrimitiveType.Capsule, pivot, name, new Vector3(0f, yOffset, 0f), _bodyMat).transform;
        t.localScale = scale;
    }

    GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // 纯视觉件，不需要物理
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>材质实例 + 运行时转 Alpha 混合（URP/Unlit 标准做法；只影响本组件实例）。</summary>
    static Material MakeInstance(Material src, Color baseColor)
    {
        Material m;
        if (src != null) m = new Material(src);
        else
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            m = new Material(sh);
        }
        m.SetColor(BaseColorId, baseColor);
        m.SetColor(ColorId, baseColor); // 兼容非 URP 回退着色器
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_Surface", 1f);   // URP：0=Opaque 1=Transparent
        m.SetFloat("_Blend", 0f);     // 0=Alpha 混合
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }
}
