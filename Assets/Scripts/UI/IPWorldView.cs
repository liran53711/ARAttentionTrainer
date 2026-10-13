// Assets/Scripts/UI/IPWorldView.cs
// 真机世界空间 IP（小精灵/火柴人）：把精灵"贴"到老师头部的真实 3D 位置。
// 驱动层（M3DebugDriver）保持平台无关——只推归一化坐标/透明度/缩放（与电脑版 IPDebugView 同接口）；
// 本组件用与 Detection3DOverlay 相同的反投影管道（帧同步位姿 + 焦距）换算世界位置。
//
// 2026-10-11 重做（用户定调：参考 First Encounters 的"位置稳定 + 移动合理"，消灭抖动/闪现）：
//   ① 锚定规则：目标有效→每检测批次（~5Hz）重锚；目标丢失→冻结（不随头转漂移）；
//      从未锁过目标（开场吸引）→跟随驱动动画；陪伴模式→分割掩码更新时贴轮廓。
//   ② 连续追赶：渲染位置每帧用 SmoothDamp 平滑趋近锚点（不再 5Hz 瞬跳），
//      超大跨度（重生/重锁跨房间）直接吸附，不发生长距离飞行。
//   ③ 朝向用 Slerp 指数平滑；透明度交给材质的 alpha（StickFigureIp），不用 SetActive 闪现。
//   ④ 空间围栏：有 MRUK 房间数据时把锚点约束在房间内（不穿墙/地板）——"空间定位"感。
// 占位美术：StickFigureIp 火柴人（为组员正式 3D 模型预留的接口节点 model3D）。
using Meta.XR;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class IPWorldView : MonoBehaviour
{
    /// <summary>C 项：驱动层推送的动画状态（与 M3DebugDriver.IpAnim 顺序一一对应）。</summary>
    public enum IPAnimState { Hidden = 0, Appearing = 1, Guiding = 2, Companion = 3, Feedback = 4, Peek = 5 }
    public IPAnimState animState;
    [Header("引用")]
    public PcaFrameSource frameSource;
    public FaceDetector detector;   // 检测批次版本（~5Hz 重锚节拍）
    public SelfieSegRunner seg;     // 人体轮廓分割（陪伴模式：精灵贴真实轮廓边缘）
    public EnvironmentRaycastManager envRaycast; // B：环境射线实测距离（有深度/房间数据时优先）
    public GameObject model3D;      // C：3D 模型（火柴人占位/组员正式模型；为空则用精灵贴图）
    public Animator animator;       // C：正式模型动画驱动（Animator 缺失时走 StickFigureIp 程序化姿态）
    public MrukBootstrap mruk;      // 空间锚定：房间边界围栏

    [Header("轮廓贴合")]
    [Tooltip("真机驱动推送：当前是否处于陪伴模式（贴轮廓）")]
    public bool companionMode;
    [Tooltip("驱动推送：陪伴方位（-1=脸左，+1=脸右）")]
    public float sideHint = 1f;
    [Tooltip("驱动推送：目标是否有效——有效=跟随锚定；丢失=冻结防漂移（设计 §4.3 脸可丢方向不丢）")]
    public bool targetValid;
    [Tooltip("驱动推送：老师的视口坐标（最后已知；路标模式下精灵离开了老师，老师锚点单独算）")]
    public float targetX01 = 0.5f, targetY01 = 0.5f;
    [Tooltip("驱动推送：路标模式（引导路激活，精灵飞到孩子面前）——此模式下即使目标丢失也重构精灵锚点")]
    public bool signpostMode;
    [Tooltip("驱动推送：找老师动作——周期触发 Point 指向手势（引导路激活时）")]
    public bool pointing;
    [Tooltip("驱动推送：身体朝老师方向侧转比例（0=完全面向孩子，0.5=一半朝向老师）")]
    public float faceTeacherBlend;
    float _nextPoint;

    [Header("移动平滑（First Encounters 手感）")]
    [Tooltip("渲染位置追赶锚点的时间常数（秒）——抹平 5Hz 重锚步进（2026-10-12 二调 0.26→0.32）")]
    public float positionSmoothTime = 0.32f;
    [Tooltip("最大移动速度（米/秒）——超速自动限幅（防甩飞；2026-10-12 二调 1.7→1.2：锁定瞬间不再横飞）")]
    public float maxSpeed = 1.2f;
    [Tooltip("超过该距离视为瞬移（重生/跨房间重锁）：直接吸附（2026-10-12 1.2→2.0：检测抖动不易触发瞬移）")]
    public float snapDistance = 2.0f;
    [Tooltip("朝向平滑时间（秒）")]
    public float rotationSmoothTime = 0.2f;
    [Tooltip("飞行倾斜（bank）强度：随移动速度前倾/侧倾，增强飞行动态感")]
    public float bankStrength = 1f;
    [Tooltip("飞行倾斜最大角（度）")]
    public float bankMaxDeg = 18f;

    [Header("空间围栏（MRUK 房间边界）")]
    [Tooltip("与人墙/边界保持的边距（米）")]
    public float roomMargin = 0.35f;
    [Tooltip("最低高度=地板+该值（米）")]
    public float floorMinY = 0.45f;
    [Tooltip("最高高度=天花板-该值（米）")]
    public float ceilClearance = 0.35f;

    [Header("模型接口（组员正式模型约定）")]
    [Tooltip("模型原生身高（米）——按 baseWorldSize/modelNativeHeight 归一缩放：FBX 按真实米制建模即可，不用在源文件里缩放")]
    public float modelNativeHeight = 1f;
    [Tooltip("模型\"脚下\"距中心的偏移（米）——引导路的起点锚定用（≈世界身高的 35%）")]
    public float modelFeetOffset = 0.16f;

    [Header("参数")]
    [Tooltip("估算用的人体身高（与检测框一致）")]
    public float assumedPersonHeight = 1.7f;
    [Tooltip("精灵基准世界尺寸（米）：略大于人脸，保证远距离也醒目（2026-10-12 0.34→0.46：用户反馈偏小）")]
    public float baseWorldSize = 0.46f;
    [Tooltip("世界偏移：微调到脸侧/脸前，避免与脸完全重叠")]
    public Vector3 worldOffset = new Vector3(0f, 0f, -0.03f);
    [Tooltip("吸引模式（尚无目标框）时精灵显示的默认距离（米）")]
    public float attractDefaultDistance = 1.5f;

    // ── 驱动层每帧写入（与 IPDebugView 保持一致的接口）──
    public bool visible;
    public float x01 = 0.5f, y01 = 0.5f; // 归一化坐标（左上原点，与检测框一致）
    public float height01;               // 目标框归一化高度（估距用）
    public float alpha = 1f;
    public float scale = 1f;
    public Color tint = Color.white;

    PassthroughCameraAccess _camera;
    SpriteRenderer _renderer;
    StickFigureIp _stick;
    IpEffects _fx;                      // 精灵特效系统（柔光/粒子/微动画）
    SpriteExpressionController _expr;   // CH_Sprite 表情适配器（有则驱动）
    string _exprCurrent = "";
    bool _stickChecked;

    /// <summary>精灵当前的世界位置（平滑后的渲染位置；语音空间定位用）。</summary>
    public Vector3 CurrentWorldPos => _hasRenderPos ? _renderWorldPos : transform.position;

    /// <summary>老师相对视野的方位提示点（视口坐标）：老师可见时=其投影附近；不可见时=该方向的视野内缘。
    /// 用于"IP 飞向老师相对方位"（2026-10-12 用户定调：立体声引导方案）。</summary>
    public Vector2 TeacherViewportHint()
    {
        if (!_hasTeacherPos) return new Vector2(0.5f, 0.6f);
        Vector3 camPos;
        Quaternion camRot;
        CameraFrame frame = default;
        if (frameSource != null && frameSource.TryGetFrame(out frame) && frame.valid)
        {
            camPos = frame.cameraPose.position;
            camRot = frame.cameraPose.rotation;
        }
        else
        {
            camPos = transform.position;
            camRot = transform.rotation;
        }
        Vector3 dLocal = Quaternion.Inverse(camRot) * (_teacherPos - camPos);
        var dir = new Vector2(dLocal.x, -dLocal.y); // 视口 y 向下
        if (dir.sqrMagnitude < 1e-4f) return new Vector2(0.5f, 0.6f);
        dir.Normalize();
        var hint = new Vector2(0.5f + dir.x * 0.34f, 0.6f + dir.y * 0.24f);
        return new Vector2(Mathf.Clamp(hint.x, 0.15f, 0.85f), Mathf.Clamp(hint.y, 0.2f, 0.85f));
    }

    /// <summary>驱动层转发一次性特效脉冲（Lock/Achieve/Feedback/ReGuide/Celebrate）。</summary>
    public void FxPulse(string kind) => _fx?.Pulse(kind);

    // ── 跳舞庆典（2026-10-13 用户定调：人脸居中保持 20s → 精灵跳舞奖励）──
    float _celebrateStart = -999f, _celebrateUntil = -999f, _nextCelebFx;
    /// <summary>庆典是否进行中（驱动层/测试探针用）。</summary>
    public bool IsCelebrating => Time.time < _celebrateUntil;

    /// <summary>触发跳舞庆典：立即进入"开心"情绪 + 欢呼泡泡 + 金色星光，持续 duration 秒。</summary>
    public void Celebrate(float duration)
    {
        _celebrateStart = Time.time;
        _celebrateUntil = Time.time + duration;
        _nextCelebFx = Time.time + 0.45f; // 开场大脉冲后 0.45s 再开始节拍
        EnsureStick();                    // 非 LateUpdate 时机调用时保证 _fx/_expr 就绪
        if (_fx != null)
        {
            _fx.SetMood(IPAnimState.Feedback); // 开心弹跳 + 亮光晕
            _fx.Pulse("Celebrate");            // 大爆发：金色星尘 + 欢呼泡泡
        }
        if (animator != null) animator.SetTrigger("Feedback"); // 开心/庆祝动作
        if (_expr != null)
        {
            _expr.ClearExpressions();
            _expr.SetExpression("Encourage", 1f);
        }
    }

    /// <summary>老师的世界位置（独立锚点；丢失时=最后已知方位——"人脸可丢，方向不丢"，引导路终点用）。</summary>
    public Vector3 CurrentTeacherPos => _hasTeacherPos ? _teacherPos : CurrentWorldPos;

    // 老师独立锚点：路标模式下精灵飞走了，老师的方位要单独记住
    Vector3 _teacherPos;
    bool _hasTeacherPos;

    // 锚点（离散重建）与渲染位置（连续追赶）
    bool _hasWorldPos;
    Vector3 _lastWorldPos;      // 锚点
    Vector3 _renderWorldPos;    // 渲染位置（每帧追赶锚点）
    Vector3 _posVel;
    bool _hasRenderPos;
    Quaternion _renderRot;
    bool _hasRenderRot;

    Vector2 _prevAnimPos = new Vector2(0.5f, 0.5f);
    int _lastVersion = -1;
    int _lastSegVersion = -1;
    bool _everHadValidTarget;
    float _invalidDuration;     // 连续无目标时长（>10s 回到"从未锁过"的跟随语义）
    float _lastDiagTime;
    IPAnimState _visualAnim = IPAnimState.Hidden;
    IPAnimState _pendingAnim = IPAnimState.Hidden;
    float _pendingAnimT;

    void Awake()
    {
        _camera = GetComponent<PassthroughCameraAccess>();

        var go = new GameObject("IP_Sprite");
        go.transform.SetParent(transform, false);
        _renderer = go.AddComponent<SpriteRenderer>();
        _renderer.sprite = CreatePlaceholderSprite();
        _renderer.enabled = false;
    }

    /// <summary>把世界坐标约束在房间内（纯函数，供单测）：x/z 留边距、y ∈ [地板+floorMinY, 天花板−ceilClearance]。
    /// 尺寸过小/无效的房间不做约束（返回原值）。</summary>
    public static Vector3 ClampToRoom(Vector3 p, Bounds room, float margin, float floorMinY, float ceilClearance)
    {
        if (room.size.x > 2f * margin + 0.1f)
            p.x = Mathf.Clamp(p.x, room.min.x + margin, room.max.x - margin);
        if (room.size.z > 2f * margin + 0.1f)
            p.z = Mathf.Clamp(p.z, room.min.z + margin, room.max.z - margin);
        float yMin = room.min.y + floorMinY;
        float yMax = room.max.y - ceilClearance;
        if (yMax > yMin) p.y = Mathf.Clamp(p.y, yMin, yMax);
        return p;
    }

    void LateUpdate()
    {
        if (_renderer == null) return;
        EnsureStick();

        // 不可见：火柴人走"材质淡出"（平滑，不闪现）；无火柴人的模型才用 SetActive
        if (!visible || alpha <= 0.01f)
        {
            _renderer.enabled = false;
            if (model3D != null)
            {
                if (_stick != null) _stick.SetVisuals(IPAnimState.Hidden, 0f, tint);
                else model3D.SetActive(false);
            }
            return;
        }

        CameraFrame frame = default;
        if (frameSource == null || !frameSource.TryGetFrame(out frame) || !frame.valid || frame.texture == null || _camera == null)
        {
            _renderer.enabled = false;
            if (model3D != null)
            {
                if (_stick != null) _stick.SetVisuals(IPAnimState.Hidden, 0f, tint);
                else model3D.SetActive(false);
            }
            return;
        }

        // ── 锚定规则（2026-10-11 重做）──
        bool versionChanged = detector != null && detector.Version != _lastVersion;
        if (versionChanged) _lastVersion = detector.Version;
        bool segChanged = companionMode && seg != null && seg.Version != _lastSegVersion;
        if (segChanged) _lastSegVersion = seg.Version;
        bool animMoved = Vector2.Distance(new Vector2(x01, y01), _prevAnimPos) > 0.02f;

        if (targetValid)
        {
            _everHadValidTarget = true;
            _invalidDuration = 0f;
        }
        else
        {
            _invalidDuration += Time.deltaTime;
            if (_invalidDuration > 10f) _everHadValidTarget = false; // 长期无目标 → 回到吸引模式的跟随语义
        }
        bool anchorable = targetValid || signpostMode || segChanged || !_everHadValidTarget;
        bool rebuild = !_hasWorldPos || (anchorable && (versionChanged || animMoved || segChanged));

        if (rebuild)
        {
            _prevAnimPos = new Vector2(x01, y01);

            float texH = frame.texture.height;
            float hPx = height01 * texH;
            float dist;
            if (hPx > 8f)
                dist = Mathf.Clamp(frame.focalLengthPx * assumedPersonHeight / hPx, 0.3f, 8f);
            else
                dist = attractDefaultDistance; // 吸引模式：还没有人脸框 → 固定距离出现在视野中央

            // 陪伴模式 + 有轮廓掩码：位置改为"贴住真实轮廓边缘"（用户定调：与人脸贴合互动）
            var target01 = new Vector2(x01, y01);
            if (companionMode && seg != null && seg.Version > 0)
            {
                bool leftSide = sideHint < 0f;
                float xMin = leftSide ? 0.02f : Mathf.Clamp01(x01 - 0.04f);
                float xMax = leftSide ? Mathf.Clamp01(x01 + 0.04f) : 0.98f;
                if (seg.TryGetEdge(y01, xMin, xMax, leftSide, out var edge))
                    target01 = new Vector2(Mathf.Clamp01(edge.x + sideHint * 0.015f), edge.y); // 探出轮廓一点点
            }

            // ★实测修正（2026-10-10）：PCA 的 ViewportPointToRay 采用"左上原点"视口约定，
            // 与 input 的归一化坐标一致——直接传，不要做 Unity 式 y 翻转（翻转会把精灵画到脚下）
            var ray = _camera.ViewportPointToRay(new Vector2(target01.x, target01.y), frame.cameraPose);

            // B（MRUK 环境射线实测距离）：有深度/房间数据时优先；失败回退身高公式
            // （预声明命中结构：C# 对"短路链中 out var"的明确赋值分析会误报）
            EnvironmentRaycastHit hit = default;
            if (envRaycast != null &&
                envRaycast.Raycast(ray, out hit, 8f) &&
                hit.status == EnvironmentRaycastHitStatus.Hit)
            {
                float dHit = Vector3.Distance(ray.origin, hit.point);
                if (dHit > 0.3f && dHit < 8f) dist = dHit;
            }

            Vector3 right = frame.cameraPose.rotation * Vector3.right;
            Vector3 up = frame.cameraPose.rotation * Vector3.up;
            Vector3 fwd = frame.cameraPose.rotation * Vector3.forward;
            Vector3 anchorNew = ray.origin + ray.direction * dist
                            + right * worldOffset.x + up * worldOffset.y + fwd * worldOffset.z;

            // 空间围栏：房间数据就绪时，把锚点约束在房间内（不穿墙/不插地板）
            if (mruk != null && mruk.TryGetRoomBounds(out var room))
                anchorNew = ClampToRoom(anchorNew, room, roomMargin, floorMinY, ceilClearance);

            // ★锚点低通滤波（2026-10-12 二调，日志实锤：锁定瞬间锚点跳 1.5-2m → "看到老师瞬间闪现"）：
            //   5Hz 重建逐次混合，平滑检测噪声；刚重锁（丢失>0.5s）时更缓（0.18）防首帧乱跳。
            float k = _invalidDuration > 0.5f ? 0.18f : 0.45f;
            _lastWorldPos = _hasWorldPos ? Vector3.Lerp(_lastWorldPos, anchorNew, k) : anchorNew;

            // 老师独立锚点（引导路终点）：目标有效时按老师视口坐标算；丢失时保持冻结（同样低通）
            if (targetValid)
            {
                var rayT = _camera.ViewportPointToRay(new Vector2(targetX01, targetY01), frame.cameraPose);
                Vector3 teacherNew = rayT.origin + rayT.direction * dist
                              + right * worldOffset.x + up * worldOffset.y + fwd * worldOffset.z;
                if (mruk != null && mruk.TryGetRoomBounds(out var roomT))
                    teacherNew = ClampToRoom(teacherNew, roomT, roomMargin, floorMinY, ceilClearance);
                _teacherPos = _hasTeacherPos ? Vector3.Lerp(_teacherPos, teacherNew, k) : teacherNew;
                _hasTeacherPos = true;
            }

            _hasWorldPos = true;
        }

        // ── 连续追赶（核心修复）：渲染位置逐帧平滑趋近锚点，5Hz 重锚不再瞬跳 ──
        if (!_hasRenderPos || Vector3.Distance(_renderWorldPos, _lastWorldPos) > snapDistance)
        {
            _renderWorldPos = _lastWorldPos; // 首帧/大跨度（重生）→ 吸附
            _posVel = Vector3.zero;
            _hasRenderPos = true;
        }
        else
        {
            _renderWorldPos = Vector3.SmoothDamp(_renderWorldPos, _lastWorldPos, ref _posVel,
                                                 positionSmoothTime, maxSpeed, Time.deltaTime);
        }

        // ── 精灵贴图路径（无 3D 模型时的回退）──
        var t = _renderer.transform;
        t.position = _renderWorldPos;
        Vector3 toEye = t.position - frame.cameraPose.position;
        if (toEye.sqrMagnitude > 1e-6f)
            t.rotation = Quaternion.LookRotation(toEye.normalized, frame.cameraPose.rotation * Vector3.up);
        t.localScale = Vector3.one * (baseWorldSize * Mathf.Max(scale, 0.05f));
        _renderer.color = new Color(tint.r, tint.g, tint.b, alpha);
        _renderer.enabled = model3D == null; // 有 3D 模型 → 精灵贴图让位

        // ── 3D 模型路径（火柴人占位 / 组员正式模型）──
        var visual = DebounceAnim(animState); // 引导↔陪伴抖动去抖，姿势不闪
        if (model3D != null)
        {
            if (_stick == null) model3D.SetActive(true);
            var mt = model3D.transform;
            // 环绕漂移（2026-10-12 用户"独立生命感"）：绕锚点的小幅慢速环绕——世界系，不随头转
            float ot = Time.time * 0.35f;
            Vector3 orbit = Vector3.right * (Mathf.Cos(ot) * 0.018f)
                          + Vector3.up * (Mathf.Sin(ot * 0.8f) * 0.014f);
            // 跳舞庆典叠加（2026-10-13）：蹦跳 + 旋转两圈 + 伸缩脉冲 + 星光节拍
            Vector3 dance = Vector3.zero;
            float danceScale = 1f;
            if (IsCelebrating)
            {
                float ct = Time.time - _celebrateStart;
                float danceFade = Mathf.Clamp01((_celebrateUntil - Time.time) / 0.6f); // 收势柔和淡出
                dance = Vector3.up * (Mathf.Abs(Mathf.Sin(ct * 6.5f)) * 0.07f * danceFade); // 蹦跳
                if (_fx != null && Time.time >= _nextCelebFx)
                {
                    _nextCelebFx = Time.time + 0.7f;
                    _fx.Pulse("Celebrate"); // 周期金色星光（大爆发在 Celebrate() 开场已放）
                }
            }
            mt.position = _renderWorldPos + orbit + dance + (_fx != null ? _fx.ModelMicroOffset : Vector3.zero);

            // 朝向：指数平滑 Slerp（吸收头姿微抖）；"找老师"时按比例侧转向老师
            Vector3 toEyeFlat = frame.cameraPose.position - _renderWorldPos;
            toEyeFlat.y = 0f;
            if (toEyeFlat.sqrMagnitude > 1e-4f)
            {
                var want = Quaternion.LookRotation(toEyeFlat.normalized, Vector3.up);
                if (faceTeacherBlend > 0.01f && _hasTeacherPos)
                {
                    Vector3 toTeacherFlat = _teacherPos - _renderWorldPos;
                    toTeacherFlat.y = 0f;
                    if (toTeacherFlat.sqrMagnitude > 1e-4f)
                        want = Quaternion.Slerp(want,
                            Quaternion.LookRotation(toTeacherFlat.normalized, Vector3.up),
                            Mathf.Clamp01(faceTeacherBlend));
                }
                _renderRot = _hasRenderRot && rotationSmoothTime > 0.01f
                    ? Quaternion.Slerp(_renderRot, want, Mathf.Clamp01(Time.deltaTime / rotationSmoothTime))
                    : want;
                _hasRenderRot = true;
                mt.rotation = _renderRot;
            }

            // 飞行倾斜（bank，2026-10-12 用户反馈"增加平滑的飞行动作"）：
            // 随移动速度前倾/侧倾——速度来自 SmoothDamp 本身（已平滑），再叠姿态更"在飞"
            if (bankStrength > 0.01f && _posVel.sqrMagnitude > 0.0004f)
            {
                Vector3 localVel = Quaternion.Inverse(_renderRot) * _posVel; // 局部系：+Z 前、+X 右
                float bankPitch = Mathf.Clamp(-localVel.z * 9f * bankStrength, -bankMaxDeg, bankMaxDeg); // 前飞→前倾
                float bankRoll = Mathf.Clamp(-localVel.x * 11f * bankStrength, -bankMaxDeg, bankMaxDeg); // 侧移→侧倾
                mt.rotation = _renderRot * Quaternion.Euler(-bankPitch, 0f, bankRoll);
            }
            else mt.rotation = _renderRot;

            if (IsCelebrating)
            {
                float ct = Time.time - _celebrateStart;
                float spinFade = Mathf.Clamp01((_celebrateUntil - Time.time) / 0.6f);
                // 前 2.6s 原地跳转两整圈（720°≡0°，结束不回跳）；后段蹦跳继续
                float spin = Mathf.Min(ct / 2.6f, 1f) * 2f * 360f;
                mt.rotation = Quaternion.AngleAxis(spin, Vector3.up) * mt.rotation;
                danceScale = 1f + 0.07f * Mathf.Sin(ct * 9f) * spinFade; // 伸缩脉冲（像开心晃）
            }

            // 尺寸：按原生身高归一 + 出场随 alpha 柔和放大
            // ★2026-10-12：alpha*1.5 → alpha*3——陪伴态 alpha≈0.55 时保持满尺寸，
            //   否则 引导(1.0)→陪伴(0.55) 切换瞬间模型缩 30%（用户实测"闪现"的另一来源）
            float fade = Mathf.Clamp01(alpha * 3f);
            float modelScale = baseWorldSize * scale / Mathf.Max(0.01f, modelNativeHeight) * fade * danceScale;
            mt.localScale = Vector3.one * Mathf.Max(modelScale, 0.01f);

            if (_stick != null) _stick.SetVisuals(visual, alpha, tint); // 火柴人：姿态+透明度全平滑
            DriveAnimator(visual);

            // "找老师"指向手势：周期触发 Point（找老师期间每次约 2.8s 一次）
            if (animator != null && pointing && Time.time >= _nextPoint)
            {
                animator.SetTrigger("Point");
                _nextPoint = Time.time + 2.8f;
            }
        }

        // 诊断日志（1Hz 节流）：锚点 vs 渲染位置 vs 目标——真机排查"抖/闪"用
        if (Time.unscaledTime - _lastDiagTime > 1f)
        {
            _lastDiagTime = Time.unscaledTime;
            string roomInfo = mruk != null && mruk.TryGetRoomBounds(out var rb)
                ? $"房间[{rb.min.ToString("F1")}~{rb.max.ToString("F1")}]" : "无房间数据";
            Debug.Log($"[IP] vis={visible} alpha={alpha:F2} scale={scale:F2} 锚={_lastWorldPos.ToString("F2")} " +
                      $"渲染={_renderWorldPos.ToString("F2")} tgt有效={targetValid} 重建距={Vector3.Distance(_lastWorldPos, _renderWorldPos):F3}m " +
                      $"camPos={frame.cameraPose.position.ToString("F2")} height01={height01:F3} {roomInfo}");
        }
    }

    void EnsureStick()
    {
        if (_stickChecked) return;
        _stickChecked = true;
        if (model3D == null) return;
        _stick = model3D.GetComponentInChildren<StickFigureIp>(true);
        _expr = model3D.GetComponentInChildren<SpriteExpressionController>(true);
        _fx = model3D.GetComponentInChildren<IpEffects>(true);
    }

    /// <summary>视觉状态去抖：引导↔陪伴需持续 0.18s 才切换（注视抽动不致姿势乱闪）；瞬时态（登场/反馈/探头/隐藏）放行。</summary>
    IPAnimState DebounceAnim(IPAnimState want)
    {
        if (want == _visualAnim) { _pendingAnimT = 0f; return _visualAnim; }
        bool transient = want == IPAnimState.Hidden || _visualAnim == IPAnimState.Hidden
                      || want == IPAnimState.Appearing || _visualAnim == IPAnimState.Appearing
                      || want == IPAnimState.Feedback || _visualAnim == IPAnimState.Feedback
                      || want == IPAnimState.Peek || _visualAnim == IPAnimState.Peek;
        if (transient) { _pendingAnimT = 0f; _visualAnim = want; return _visualAnim; }
        if (want != _pendingAnim) { _pendingAnim = want; _pendingAnimT = 0f; }
        _pendingAnimT += Time.deltaTime;
        if (_pendingAnimT >= 0.18f) { _visualAnim = want; _pendingAnimT = 0f; }
        return _visualAnim;
    }

    IPAnimState _prevVisual = IPAnimState.Hidden;

    /// <summary>C：把动画状态翻译成 Animator 参数（CH_Sprite.controller：Guiding/Companion 布尔 + Appear/Feedback/Peek 触发）。</summary>
    void DriveAnimator(IPAnimState visual)
    {
        if (_prevVisual == visual) return;
        var prev = _prevVisual;
        _prevVisual = visual;
        // 特效联动（2026-10-12）：情绪切换 + 关键节点脉冲
        if (_fx != null)
        {
            _fx.SetMood(visual);
            if (visual == IPAnimState.Appearing) _fx.Pulse("Appear");
            if (visual == IPAnimState.Feedback) _fx.Pulse("Feedback");
            if (visual == IPAnimState.Companion && prev == IPAnimState.Guiding) _fx.Pulse("Achieve"); // 注视达成
        }
        if (animator != null)
        {
            animator.SetBool("Guiding", visual == IPAnimState.Guiding);
            animator.SetBool("Companion", visual == IPAnimState.Companion);
            switch (visual)
            {
                case IPAnimState.Appearing: animator.SetTrigger("Appear"); break;
                case IPAnimState.Feedback: animator.SetTrigger("Feedback"); break;
                case IPAnimState.Peek: animator.SetTrigger("Peek"); break;
            }
        }
        DriveExpression(visual);
    }

    /// <summary>表情（模型挂 SpriteExpressionController 时）：按交付契约的 suggested_expression 映射状态。</summary>
    void DriveExpression(IPAnimState visual)
    {
        if (_expr == null) return;
        string want;
        switch (visual)
        {
            case IPAnimState.Appearing: want = "Surprised"; break;
            case IPAnimState.Guiding:   want = "Curious"; break;   // 引导=好奇张望
            case IPAnimState.Feedback:  want = "Encourage"; break; // 正反馈=鼓励笑
            case IPAnimState.Peek:      want = "Curious"; break;   // 探头=好奇
            default:                    want = "Smile"; break;     // 陪伴/待机
        }
        if (want == _exprCurrent) return;
        _exprCurrent = want;
        _expr.ClearExpressions();   // 单情绪切换前先清（交付说明要求）
        _expr.SetExpression(want, 1f);
    }

    /// <summary>程序生成占位精灵（64×64 紫色圆脸 + 眼睛 + 白圈），无 3D 模型时的回退。</summary>
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
