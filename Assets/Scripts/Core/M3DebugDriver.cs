// Assets/Scripts/Core/M3DebugDriver.cs
// M3 总装胶水层：连接 帧源 → 检测 → 跟踪 → 注视 → 状态机 → IP动画/音效/日志。
// 键盘模拟输入（本工程为新 Input System）：K=模拟注视切换  L=模拟讲话切换  R=重置会话
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class M3DebugDriver : MonoBehaviour
{
    [Header("引用")]
    public FaceDetector detector;
    public ExperienceSettings settings;
    public IPDebugView ipView;          // 电脑端（OnGUI）
    public IPWorldView ipWorldView;     // 真机（世界空间）——两者二选一
    public GuideArrow guideArrow;       // 引导箭头（PPT：视线离开老师时指路拉回）
    public AudioCuePlayer audioPlayer;
    public VoiceCuePlayer voiceCue;     // 语音指导（真机；片段缺失时静默跳过）
    public MicVadService vadService;

    [Header("模拟模式（电脑端）")]
    [Tooltip("true=键盘K模拟注视（电脑调试）；真机阶段换真实头姿判定")]
    public bool simulateGazeWithKeyboard = true;

    [Tooltip("true=麦克风VAD判定讲话（推荐）；false=键盘L模拟")]
    public bool useMicVad = true;

    // 逻辑模块（纯 C#）
    TargetTrackerLogic _tracker;
    GazeLogic _gaze;
    InteractionStateMachine _sm;
    SessionLogger _logger;

    // 只读对外状态（真机数据面板 DeviceDataPanel 用）
    public float SmoothFps => _fpsSmooth;
    public InteractionState State => _sm != null ? _sm.State : InteractionState.Idle;
    public float GazeDwell => _gaze != null ? _gaze.Dwell : 0f;
    public string LevelName => settings != null ? settings.CurrentLevelName : "--";
    public int CurrentLevel => settings != null ? settings.interventionLevel : 1;
    float FallbackGazeSec => settings != null ? settings.micFallbackFeedbackGazeSeconds : 5f;

    // 模拟输入状态
    bool _speaking = true;        // 默认"老师在讲话"
    bool _prevSpeaking = true;
    bool _prevTargetValid;
    bool _simLooking;             // K 键：模拟"孩子在注视"
    bool _fallbackFired;          // 麦克风降级：本轮 Hidden 已用替代规则触发过反馈
    bool _micFallbackLogged;      // 降级事件每会话只记一次
    bool _everHadTarget;          // 本会话是否曾锁定过目标（开场吸引的结束条件）
    bool _startupAttractLogged;

    // IP 动画运行时
    // Companion（陪伴）= 用户定调 2026-10-10："精灵不允许消失，要和人脸互动"：
    // 注视达成后不再渐隐，而是落到脸旁轻轻起伏、每隔几秒换一边（与人脸"作伴"）
    enum IpAnim { Hidden, Appearing, GuidingMove, Companion, FeedbackPulse, Peek }
    IpAnim _ipAnim = IpAnim.Hidden;
    float _ipAnimT;
    Vector2 _ipFrom = new Vector2(0.5f, 0.5f);
    Vector2 _ipPos = new Vector2(0.5f, 0.5f);
    float _ipAlpha;
    float _ipScale = 1f;
    float _companionSide = 1f;       // 陪伴位置：左(-1)/右(+1)脸旁
    float _companionSwitchT;         // 下一次换边倒计时
    float _companionSideSmooth = 1f; // 平滑后的方位——换边"滑过去"而不是瞬移（2026-10-11 防闪现）
    float _companionSideVel;

    // 目标保持（2026-10-11 防闪现）：检测短暂丢失时保持最后位置，
    // 避免 TargetOrCenter 立刻回退屏幕中央造成"人一抖精灵就闪回中间再飞回来"
    Vector2 _heldTargetPos = new Vector2(0.5f, 0.5f);
    bool _hasHeldTarget;
    float _heldTargetTime;
    // 视口目标平滑追赶：吸收检测框逐帧抖动（世界层还有第二道平滑）
    Vector2 _smoothTarget = new Vector2(0.5f, 0.5f);
    Vector2 _smoothTargetVel;
    // 反馈/探头"飞入"起点终点（从当前位置平滑飞过去，不瞬移）
    Vector2 _ipDest;
    bool _everShown;
    float _lockStableT;              // 目标持续有效累计（锁定稳定性门：≥0.8s 才认"认识老师"，防虚空锁定）
    float _lostT;                    // 距上次目标有效的时长（"老师消失在视野后"计时）
    bool _arrowShown;                // 引导路当前是否显示（供视图推送）
    bool _prevArrowShown;            // 上升沿检测（方位提示出现的瞬间先喊一次）
    float _voiceRepeatT;             // 方向性语音重复节拍（立体声再定位）
    // 统一输出平滑（2026-10-12：消除状态切换的 alpha/scale 阶跃——用户实测"闪现"的主因之一：
    // 日志中 alpha=1.0/scale=1.0 ↔ alpha=0.55/scale=0.85 是瞬跳，模型瞬间缩 30%）
    float _ipAlphaSmooth, _ipScaleSmooth, _ipScaleVel;
    bool _ipSmoothInit;

    float _sampleTimer;
    float _fpsSmooth = 60f;
    float _peekTimer;          // 隐藏期微提示计时
    InteractionState _prevState = InteractionState.Idle;
    static readonly List<DetectionResult> EmptyDetections = new List<DetectionResult>();

    void Start()
    {
        if (settings != null) settings.ApplyLevel(settings.interventionLevel); // 档位表→阈值字段（须在构造之前）
        _sm = settings != null
            ? new InteractionStateMachine(settings.reGuideCooldownSeconds, settings.feedbackDurationSeconds)
            : new InteractionStateMachine();
        _tracker = new TargetTrackerLogic();
        _gaze = new GazeLogic(
            settings != null ? settings.gazeStartSeconds : 0.8f,
            settings != null ? settings.gazeSustainSeconds : 2f);

        CreateLogger();
        _logger.LogEvent("SessionStart");
        if (audioPlayer != null) audioPlayer.SetVolume(settings != null ? settings.cueVolumeScale : 0.6f);
        if (voiceCue != null) voiceCue.SetVolume(settings != null ? settings.cueVolumeScale : 0.9f);
        Debug.Log($"[M3] 会话数据目录：{_logger.SessionDir}");
        Debug.Log($"[M3] 干预档位：{LevelName}（gazeStart {settings?.gazeStartSeconds:0.0}s / 冷却 {settings?.reGuideCooldownSeconds:0.0}s / Peek {settings?.peekIntervalSeconds:0.0}s）");
        string speechMode = (useMicVad && vadService != null)
            ? "麦克风VAD（若麦克风不可用→自动降级为'始终引导'）"
            : "键盘L模拟";
        Debug.Log($"[M3] 讲话输入：{speechMode}");
        Debug.Log("[M3] K=模拟注视 | L=模拟讲话(仅无麦时) | 1/2/3=切档位 | R=重置 | 真机：手柄A键循环切档");
    }

    void CreateLogger()
        => _logger = new SessionLogger(Application.persistentDataPath, "", "",
            settings != null ? settings.interventionLevel : 1,
            settings != null ? settings.SnapshotJson() : null);

    /// <summary>切换干预档位（真机手柄 A/X 键循环；电脑键盘 1/2/3）。参数即时生效并记录事件。</summary>
    void SwitchLevel(int level)
    {
        if (settings == null) return;
        settings.ApplyLevel(level);
        if (_gaze != null) _gaze.StartSeconds = settings.gazeStartSeconds;
        if (_sm != null) _sm.ReGuideCooldownSeconds = settings.reGuideCooldownSeconds;
        if (audioPlayer != null) audioPlayer.SetVolume(settings.cueVolumeScale);
        if (voiceCue != null) voiceCue.SetVolume(settings.cueVolumeScale);
        _logger?.LogEvent("LevelChange", settings.CurrentLevelName);
        Debug.Log($"[M3] 干预档位 → {settings.CurrentLevelName}" +
                  $"（gazeStart {settings.gazeStartSeconds:0.0}s / 冷却 {settings.reGuideCooldownSeconds:0.0}s / Peek {settings.peekIntervalSeconds:0.0}s）");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 1f / 60f;
        _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / dt, 0.05f);

        // ── 输入：注视（键盘K模拟）+ 讲话（麦克风VAD优先 → 降级模式 → 键盘L）──
        bool vadActive = useMicVad && vadService != null && vadService.MicRunning;
        bool micFallback = useMicVad && vadService != null && !vadService.MicRunning && !simulateGazeWithKeyboard;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.kKey.wasPressedThisFrame) _simLooking = !_simLooking;
            if (kb.lKey.wasPressedThisFrame && !vadActive) _speaking = !_speaking;
            if (kb.digit1Key.wasPressedThisFrame) SwitchLevel(0);
            if (kb.digit2Key.wasPressedThisFrame) SwitchLevel(1);
            if (kb.digit3Key.wasPressedThisFrame) SwitchLevel(2);
            if (kb.rKey.wasPressedThisFrame) ResetSession();
        }
        if (!simulateGazeWithKeyboard && OVRInput.GetDown(OVRInput.Button.One))
            SwitchLevel((CurrentLevel + 1) % 3); // 真机：手柄 A/X 键循环切档

        bool speechEnded;     // 喂状态机的"讲话刚结束"边沿
        bool realSpeechEnd = false; // 真实 VAD 边沿（日志用；降级合成的不算）
        if (vadActive)
        {
            speechEnded = vadService.Logic.SpeechEnded;
            realSpeechEnd = speechEnded;
            _speaking = vadService.Logic.Speaking;
        }
        else if (micFallback)
        {
            // 降级模式（设计 §4.4）：麦克风不可用 → "始终引导" + 注视累计达标直接给反馈
            _speaking = true;
            if (!_micFallbackLogged)
            {
                _micFallbackLogged = true;
                _logger.LogEvent("MicFallback", $"gaze{settings?.micFallbackFeedbackGazeSeconds ?? 5f}s");
                Debug.Log($"[M3] 麦克风不可用 → 降级模式（始终引导；注视满 {FallbackGazeSec}s 直接反馈）");
            }
            bool due = _gaze.Dwell >= FallbackGazeSec;
            speechEnded = due && !_fallbackFired;  // 合成一次边沿
            _fallbackFired |= due;
        }
        else
        {
            speechEnded = _prevSpeaking && !_speaking;
        }
        _prevSpeaking = _speaking;

        // ── 跟踪（选目标 + 头部点 + 滤波）──
        var detections = detector != null ? detector.Latest : EmptyDetections;
        var snapshot = _tracker.Tick(detections, dt);
        if (snapshot.valid) _everHadTarget = true;

        // ── 开场吸引（用户定调）：启动即引导，不等讲话；直到首次锁到目标或超时 ──
        bool startupAttraction = !_everHadTarget
                                 && settings != null
                                 && settings.startupAttractionSeconds > 0f
                                 && _logger.NowSeconds < settings.startupAttractionSeconds;
        if (startupAttraction && !_startupAttractLogged)
        {
            _startupAttractLogged = true;
            Debug.Log("[M3] 开场吸引：启动即引导（无需讲话）——带孩子找老师中…");
        }

        if (_prevTargetValid && !snapshot.valid) _logger.LogEvent("TargetLost");
        if (!_prevTargetValid && snapshot.valid) _logger.LogEvent("TargetAcquired");
        _prevTargetValid = snapshot.valid;

        // ── 注视判定 ──
        // 电脑端：视频里"人是否在画面中心"与"孩子是否在看"无关（相机是固定的），
        // 必须用键盘模拟；真机阶段相机会随头转动，才启用几何代理。
        bool looking;
        if (simulateGazeWithKeyboard)
        {
            looking = _simLooking;
        }
        else if (snapshot.valid)
        {
            // 几何代理：目标在视野中心附近 且 检测是新鲜的（脸不在画面里 = 没在看）
            float fresh = settings != null ? settings.gazeFreshnessSeconds : 0.4f;
            looking = snapshot.lastSeenSeconds < fresh &&
                      GazeLogic.IsLookingAt(snapshot.headPoint01,
                          settings != null ? settings.gazeViewportRadius : 0.28f);
        }
        else looking = false;
        _gaze.Tick(looking, dt);

        // ── 状态机 ──
        _sm.Tick(new InteractionInputs
        {
            speaking = _speaking || startupAttraction,
            speechJustEnded = speechEnded,
            looking = looking,
            gazeStarted = _gaze.GazeStarted,
            gazeSustained = _gaze.GazeSustained,
            targetValid = snapshot.valid,
            dt = dt,
        });

        // 状态切换 → 日志+控制台（让"为什么精灵出现/消失"一目了然）
        if (_sm.State != _prevState)
        {
            Debug.Log($"[M3] 状态切换: {_prevState} → {_sm.State}");
            _logger.LogEvent("StateChange", $"{_prevState}->{_sm.State}");
            _prevState = _sm.State;
        }
        // 降级模式的合成边沿按 Hidden 期为一轮（离开 Hidden 即解锁，下一轮可再触发）
        if (_sm.State != InteractionState.Hidden) _fallbackFired = false;

        // ── 事件 → 音效 / IP 动画 / 日志 ──
        if (_sm.GuideTriggered)
        {
            _logger.LogEvent("GuideStart", _simLooking ? "sim" : "auto");
            audioPlayer?.PlayCue(AudioCue.GuideAppear);
            // 吸引模式（还没找到人）说"看看老师在哪儿呀"，正常引导说"看这里呀"
            voiceCue?.Play(snapshot.valid ? VoiceCue.GuideAppear : VoiceCue.Find, IpVoicePos());
            StartIpAppear();
            Debug.Log("[M3] 引导出现 → IP 登场");
        }
        if (_gaze.GazeStarted) _logger.LogEvent("GazeStart");
        if (_gaze.GazeSustained) _logger.LogEvent("GazeSustained");
        if (_sm.FeedbackTriggered)
        {
            _logger.LogEvent("Feedback");
            audioPlayer?.PlayCue(AudioCue.Feedback);
            voiceCue?.Play(VoiceCue.Feedback, IpVoicePos());
            StartIpFeedback(snapshot);
            Debug.Log("[M3] 正反馈！IP 重现");
        }
        if (realSpeechEnd) _logger.LogEvent("SpeechEnd");

        // ── 周期采样 ──
        _sampleTimer += dt;
        float every = settings != null ? settings.logSampleEverySeconds : 2f;
        if (_sampleTimer >= every)
        {
            _sampleTimer = 0f;
            _logger.LogSample(looking, _speaking, snapshot.valid, _fpsSmooth,
                detector != null ? detector.LastInferenceMs : 0f);
        }

        // ── 隐藏期微提示（Peek）：长讲话中隔一会儿探头，维持注意力 ──
        if (_sm.State == InteractionState.Hidden && _speaking)
        {
            _peekTimer += dt;
            float interval = settings != null ? settings.peekIntervalSeconds : 10f;
            if (_peekTimer >= interval && _ipAnim == IpAnim.Hidden)
            {
                StartIpPeek(snapshot);
                _peekTimer = 0f;
            }
        }
        else if (_sm.State != InteractionState.Hidden)
        {
            _peekTimer = 0f;
        }

        // ── IP 动画推进 + 喂给视图 ──
        UpdateIpAnim(dt, snapshot);

        // ── 方位提示（2026-10-12 第四版，用户定调："箭头是低效信息 → 立体声重置注意力"）──
        //   老师消失在视野中：① IP 飞向"老师相对方位"（视野内缘的该方向位置）；
        //   ② 从老师所在方位播放方向性语音（立体声定位，孩子自然转头找声源）；周期重复帮助再定位；
        //   ③ IP 做"找老师"动作（指向+侧身）。箭头方案已停用（GuideArrow 保留备用）。
        if (ipWorldView != null)
        {
            float lockBefore = _lockStableT;
            _lockStableT = snapshot.valid ? _lockStableT + dt : Mathf.Max(0f, _lockStableT - dt * 0.5f);
            if (lockBefore < 0.8f && _lockStableT >= 0.8f)
                ipWorldView.FxPulse("Lock"); // 稳定锁定老师 → 一圈柔光"找到啦"
            _lostT = snapshot.valid ? 0f : _lostT + dt;
            bool hintOn = !snapshot.valid && _lostT > 0.4f && _lostT < 12f && _lockStableT >= 0.8f;
            if (hintOn)
            {
                // ① IP 飞向老师相对方位（不慌不忙，缓速滑过去）
                Vector2 hint = ipWorldView.TeacherViewportHint();
                _ipPos = Vector2.Lerp(_ipPos, hint, Mathf.Clamp01(dt * 1.6f));
                // ② 方向性语音从老师方位传来（立体声定位；出现时立刻喊，之后每 ~3.6s 重复）
                if (!_prevArrowShown) _voiceRepeatT = 3.6f;
                _voiceRepeatT -= dt;
                if (_voiceRepeatT <= 0f)
                {
                    _voiceRepeatT = 3.6f;
                    voiceCue?.Play(VoiceCue.Find, ipWorldView.CurrentTeacherPos); // 声源=老师方位
                }
            }
            _prevArrowShown = hintOn;
            _arrowShown = hintOn;
        }
        if (ipView != null)
        {
            ipView.visible = _ipAnim != IpAnim.Hidden && _ipAlphaSmooth > 0.01f;
            ipView.x01 = _ipPos.x; ipView.y01 = _ipPos.y;
            ipView.alpha = _ipAlphaSmooth; ipView.scale = _ipScaleSmooth * (settings != null ? settings.ipSizeFactor : 1f);
            ipView.tint = _ipAnim == IpAnim.FeedbackPulse ? new Color(1f, 0.95f, 0.6f) : Color.white;

            // 状态栏：解释当前行为 + 反馈还差什么
            string hint = "";
            if (_sm.State == InteractionState.Hidden)
            {
                float need = (settings != null ? settings.gazeSustainSeconds : 2f) - _gaze.Dwell;
                if (need > 0f)
                    hint = $"　→ 再看 {need:0.0}s 达成注视";
                else if (_speaking)
                {
                    float interval = settings != null ? settings.peekIntervalSeconds : 10f;
                    hint = $"　→ 注视已达成，等老师讲完（下次探头 {Mathf.Max(0f, interval - _peekTimer):0}s）";
                }
                else hint = "　→ 注视已达成+讲完了！";
            }
            string speechInfo = vadActive
                ? $"{(_speaking ? "开" : "关")}[VAD {vadService.LastEnergyDb:0}dB/底{vadService.Logic.NoiseFloorDb:0}]"
                : $"{(_speaking ? "开" : "关")}[键盘]";
            ipView.statusLine =
                $"状态:{_sm.State} | 模拟注视:{(_simLooking ? "开" : "关")} 累计:{_gaze.Dwell:0.0}s" +
                $" | 讲话:{speechInfo} | 目标:{(snapshot.valid ? "有效" : "无")}{hint}";
        }
        else if (ipWorldView != null)
        {
            ipWorldView.visible = _ipAnim != IpAnim.Hidden && _ipAlphaSmooth > 0.01f;
            ipWorldView.x01 = _ipPos.x; ipWorldView.y01 = _ipPos.y;
            if (snapshot.valid) ipWorldView.height01 = snapshot.boxHeight01;
            ipWorldView.alpha = _ipAlphaSmooth; ipWorldView.scale = _ipScaleSmooth * (settings != null ? settings.ipSizeFactor : 1f);
            ipWorldView.tint = _ipAnim == IpAnim.FeedbackPulse ? new Color(1f, 0.95f, 0.6f) : Color.white;
            ipWorldView.companionMode = _ipAnim == IpAnim.Companion; // 陪伴 → 贴真实轮廓
            ipWorldView.sideHint = _companionSide;
            ipWorldView.targetValid = snapshot.valid; // 锚定规则：有效=跟随；丢失=冻结防漂移
            ipWorldView.targetX01 = _heldTargetPos.x; // 老师独立锚点（最后已知视口坐标；路标模式精灵飞走后老师方位单独算）
            ipWorldView.targetY01 = _heldTargetPos.y;
            ipWorldView.signpostMode = _arrowShown;   // 路标模式：老师消失 → IP 飞来当路标（目标丢失也重构锚点）
            ipWorldView.pointing = _arrowShown;       // "找老师"动作：周期性指向手势 + 身体朝老师侧转
            ipWorldView.faceTeacherBlend = _arrowShown ? 0.5f : 0f;
            ipWorldView.animState = (IPWorldView.IPAnimState)(int)_ipAnim; // C：动画状态转发（枚举顺序一一对应）
        }
    }

    void StartIpAppear()
    {
        // 首次登场（或重新引导）：从当前位置飞过去（不再瞬移回屏幕中央）
        _ipFrom = _everShown ? _ipPos : new Vector2(0.5f, 0.5f);
        _ipAnim = IpAnim.Appearing;
        _ipAnimT = 0f;
        _ipScale = 0.7f;
    }

    void StartIpFeedback(TargetSnapshot snapshot)
    {
        // 从目标附近的随机方位庆祝：从当前位置"飞过去"（0.35s 飞入在 FeedbackPulse 里完成，不瞬移）
        var basePos = TargetOrCenter(snapshot);
        float ang = Random.Range(0f, Mathf.PI * 2f);
        _ipFrom = _ipPos;
        _ipDest = basePos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.12f;
        _ipAnim = IpAnim.FeedbackPulse;
        _ipAnimT = 0f;
        _ipScale = 0.8f;
    }

    void StartIpPeek(TargetSnapshot snapshot)
    {
        // 从老师头旁随机位置探头（PPT 设计：左/右/头顶，每次不同）——从当前位置飞过去
        var basePos = TargetOrCenter(snapshot);
        int pick = Random.Range(0, 3);
        if (pick == 0) _ipDest = basePos + new Vector2(-0.10f, Random.Range(-0.05f, 0.02f));      // 左侧
        else if (pick == 1) _ipDest = basePos + new Vector2(0.10f, Random.Range(-0.05f, 0.02f));  // 右侧
        else _ipDest = basePos + new Vector2(Random.Range(-0.04f, 0.04f), -0.10f);               // 头顶（y01 减小=更上方）
        _ipFrom = _ipPos;
        _ipAnim = IpAnim.Peek;
        _ipAnimT = 0f;
        _ipScale = 0.85f;
        voiceCue?.Play(VoiceCue.Peek, IpVoicePos());
    }

    Vector3 IpVoicePos() => ipWorldView != null ? ipWorldView.CurrentWorldPos : transform.position;

    /// <summary>目标点：有效→刷新保持值；短暂丢失（&lt;2.5s）→保持最后位置（防闪回中央）；彻底无目标→视野中央。</summary>
    Vector2 TargetOrCenter(TargetSnapshot s)
    {
        if (s.valid)
        {
            _heldTargetPos = s.headPoint01;
            _hasHeldTarget = true;
            _heldTargetTime = Time.time;
            return _heldTargetPos;
        }
        if (_hasHeldTarget && Time.time - _heldTargetTime < 2.5f) return _heldTargetPos;
        return new Vector2(0.5f, 0.5f);
    }

    /// <summary>平滑追赶后的目标（所有动画状态统一用它，吸收检测抖动）。大跨度（重锁）直接吸附防长距离飞行。</summary>
    Vector2 SmoothTarget(TargetSnapshot s)
    {
        var raw = TargetOrCenter(s);
        if (Vector2.Distance(_smoothTarget, raw) > 0.4f)
        {
            _smoothTarget = raw;
            _smoothTargetVel = Vector2.zero;
        }
        else
        {
            _smoothTarget = Vector2.SmoothDamp(_smoothTarget, raw, ref _smoothTargetVel, 0.15f, 20f, Time.deltaTime);
        }
        return _smoothTarget;
    }

    void UpdateIpAnim(float dt, TargetSnapshot snapshot)
    {
        _ipAnimT += dt;
        if (_ipAnim != IpAnim.Hidden) _everShown = true;
        Vector2 tgt = SmoothTarget(snapshot); // 每帧刷新（先于各状态使用）
        // 陪伴换边：方位平滑滑过去（不再左右瞬移）
        _companionSideSmooth = Mathf.SmoothDamp(_companionSideSmooth, _companionSide, ref _companionSideVel, 0.45f, 20f, dt);
        switch (_ipAnim)
        {
            case IpAnim.Appearing: // 0.5s 渐入，同时向目标移动（共 1.2s）
            {
                float k = Mathf.Clamp01(_ipAnimT / 0.5f);
                _ipAlpha = Mathf.MoveTowards(_ipAlpha, 1f, dt * 4f); // 平滑渐入（不瞬变）
                _ipScale = Mathf.Lerp(0.65f, 1f, k * k * (3f - 2f * k)); // smoothstep 缓入缓出
                _ipPos = Vector2.Lerp(_ipFrom, tgt + new Vector2(0f, -0.06f),
                    Mathf.Clamp01(_ipAnimT / 1.2f));
                if (_ipAnimT >= 1.2f) { _ipAnim = IpAnim.GuidingMove; _ipAnimT = 0f; }
                break;
            }
            case IpAnim.GuidingMove: // 悬浮在目标上方，轻微起伏
                _ipPos = tgt + new Vector2(0f, -0.06f + Mathf.Sin(Time.time * 1.9f) * 0.010f);
                _ipAlpha = 1f;
                _ipScale = 1f;
                if (_sm.State != InteractionState.Guiding) // 离开引导 → 进入"陪伴模式"（不消失）
                {
                    _ipAnim = IpAnim.Companion;
                    _ipAnimT = 0f;
                    _companionSide = Random.value < 0.5f ? -1f : 1f;
                    _companionSwitchT = Random.Range(3f, 6f);
                    voiceCue?.Play(VoiceCue.Peek, IpVoicePos()); // “我在这儿”～落到脸旁
                }
                break;
            case IpAnim.Companion: // 陪伴：趴在脸旁轻轻起伏，每隔几秒换一边（滑过去，不瞬移）
            {
                _companionSwitchT -= dt;
                if (_companionSwitchT <= 0f)
                {
                    _companionSwitchT = Random.Range(4f, 8f);
                    _companionSide = -_companionSide;
                }
                _ipPos = tgt + new Vector2(
                    _companionSideSmooth * 0.115f,
                    -0.035f + Mathf.Sin(Time.time * 1.35f) * 0.009f);
                _ipScale = 0.85f + 0.05f * Mathf.Sin(Time.time * 1.7f);
                if (_sm.State == InteractionState.Idle)
                {
                    // 待机期：有目标（含短暂丢失保持期）半透明待着；长期无目标也【不消失】——
                    // ★2026-10-13 修复（用户实测："长时间没看到人脸，IP 自己消失了"）：
                    // 设计原则"小精灵不允许消失"——无目标时降到 0.35 半透明守候，绝不淡出为 0。
                    bool targetAlive = snapshot.valid
                        || (_hasHeldTarget && Time.time - _heldTargetTime < 2.5f);
                    _ipAlpha = targetAlive
                        ? Mathf.MoveTowards(_ipAlpha, 0.55f, dt * 0.8f)
                        : Mathf.MoveTowards(_ipAlpha, 0.35f, dt * 0.8f);
                }
                else
                {
                    _ipAlpha = 1f;
                }
                if (_sm.State == InteractionState.Guiding) _ipAnim = IpAnim.GuidingMove; // 重新引导 → 回到引导移动
                break;
            }
            case IpAnim.FeedbackPulse: // 从当前位置"飞过去"庆祝（0.35s smoothstep 飞入 + 柔和脉冲；不闪现）
            {
                float flyT = Mathf.Clamp01(_ipAnimT / 0.35f);
                float e = flyT * flyT * (3f - 2f * flyT);
                _ipPos = Vector2.Lerp(_ipFrom, _ipDest, e);
                _ipAlpha = Mathf.MoveTowards(_ipAlpha, 1f, dt * 6f);
                _ipScale = Mathf.Lerp(0.8f, 1.05f, e) + 0.04f * Mathf.Sin(_ipAnimT * 4.5f) * e;
                float dur = settings != null ? settings.feedbackDurationSeconds : 2f;
                if (_ipAnimT >= dur)
                {
                    // 反馈结束 → 回到陪伴（不消失）
                    _ipAnim = IpAnim.Companion;
                    _ipAnimT = 0f;
                    _companionSwitchT = Random.Range(3f, 6f);
                }
                break;
            }
            case IpAnim.Peek: // 短暂探头：从当前位置飞过去（0.3s），保持，柔出（不瞬间闪现）
            {
                float flyT = Mathf.Clamp01(_ipAnimT / 0.3f);
                float e = flyT * flyT * (3f - 2f * flyT);
                _ipPos = Vector2.Lerp(_ipFrom, _ipDest, e);
                float dur = settings != null ? settings.peekDurationSeconds : 0.8f;
                float t = _ipAnimT;
                float targetAlpha = t < dur - 0.25f ? 1f : 0f;
                _ipAlpha = Mathf.MoveTowards(_ipAlpha, targetAlpha, dt * 4.5f);
                _ipScale = Mathf.Lerp(0.85f, 1f, e);
                if (t >= dur)
                {
                    // 探头结束 → 回到陪伴（不消失）
                    _ipAnim = IpAnim.Companion;
                    _ipAnimT = 0f;
                    _companionSwitchT = Random.Range(3f, 6f);
                }
                break;
            }
            case IpAnim.Hidden:
                _ipAlpha = 0f;
                break;
        }

        // 精灵保持在视野舒适区（用户反馈"精灵待在视野边缘"）：不贴边、不被裁、始终看得清
        _ipPos = new Vector2(Mathf.Clamp(_ipPos.x, 0.13f, 0.87f), Mathf.Clamp(_ipPos.y, 0.12f, 0.88f));

        // ── 统一输出平滑（所有状态切变都变坡道；首次直接对齐防开场爬升）──
        if (!_ipSmoothInit)
        {
            _ipSmoothInit = true;
            _ipAlphaSmooth = _ipAlpha;
            _ipScaleSmooth = _ipScale;
            _ipScaleVel = 0f;
        }
        else
        {
            _ipAlphaSmooth = Mathf.MoveTowards(_ipAlphaSmooth, _ipAlpha, dt * 2.2f);
            _ipScaleSmooth = Mathf.SmoothDamp(_ipScaleSmooth, _ipScale, ref _ipScaleVel, 0.30f, 20f, dt);
        }
    }

    void ResetSession()
    {
        _logger?.Close();
        CreateLogger();
        _logger.LogEvent("SessionStart");
        _tracker.Reset();
        _gaze.Reset();
        _sm.Reset();
        _ipAnim = IpAnim.Hidden;
        _ipAlpha = 0f;
        _prevTargetValid = false;
        _everHadTarget = false;
        _startupAttractLogged = false;
        _hasHeldTarget = false;
        _smoothTarget = new Vector2(0.5f, 0.5f);
        _smoothTargetVel = Vector2.zero;
        _companionSideSmooth = _companionSide;
        _companionSideVel = 0f;
        _everShown = false;
        _ipSmoothInit = false;
        _lockStableT = 0f;
        _lostT = 999f;
        _arrowShown = false;
        _prevArrowShown = false;
        Debug.Log($"[M3] 会话已重置：{_logger.SessionDir}");
    }

    void OnDestroy()
    {
        _logger?.Close();
    }
}
