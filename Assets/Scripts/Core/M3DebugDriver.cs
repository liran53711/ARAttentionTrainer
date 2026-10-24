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
    public FaceBoxRunner faceRunner;   // BlazeFace 人脸检测（人脸居中奖励；可为空=功能关闭）
    public ExperienceSettings settings;
    public IPDebugView ipView;          // 电脑端（OnGUI）
    public IPWorldView ipWorldView;     // 真机（世界空间）——两者二选一
    public MeteorSystem meteorView;     // 流星/星环/流星雨（真机视图；可为空）
    public StardustPath stardustPath;   // 星尘路径（真机视图；可为空）
    public GuideArrow guideArrow;       // 引导箭头——已停用（方案改为流星引导），保留备用
    public AudioCuePlayer audioPlayer;
    public VoiceCuePlayer voiceCue;     // 语音指导（真机；片段缺失时静默跳过）
    public MicVadService vadService;
    [Tooltip("中心眼/头位变换（真机=OVRCameraRig.centerEyeAnchor；模拟为空）——首寻 3D 漫游起点")]
    public Transform eye;

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
    GuideDirectorLogic _guide;      // 流星引导·三段式导演（2026-10-20）
    StarProgressLogic _stars;       // 星星进度（2026-10-20）
    ConversationGateLogic _conversationGate; // v9：外界讲话/自音频门控
    bool _prevExternalSpeech;       // 上一帧有效外界讲话（边沿用）
    VoiceCue _pendingVoice;         // 外界讲话期间挂起的一条（Critical/Guide）
    VoicePriority _pendingPriority;
    float _pendingAt;
    bool _hasPendingVoice;
    bool _prevGateListening;        // 门控 Listening 边沿（会话日志 VoiceGate）
    int _prevListenLogged = -1;     // ListenState 边沿：-1 未记录 / 0 非倾听态 / 1 Attentive / 2 Listening

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
    bool _everHadTarget;          // 本会话是否曾锁定过目标（首寻结束条件）

    // 首寻 3D 漫游（2026-10-20）：第一次找到老师前，绕孩子 360° 真实漫游 + 空间语音
    FirstSearchLogic _firstSearch;
    bool _searchActive;
    float _searchDiagT;
    static readonly VoiceCue[] SearchVoiceOrder =
    {
        VoiceCue.SearchStart, VoiceCue.SearchLook, VoiceCue.SearchHere,
        VoiceCue.SearchAgain, VoiceCue.SearchWhere
    };

    // IP 动画运行时
    // Companion（陪伴）= 用户定调 2026-10-10："精灵不允许消失，要和人脸互动"：
    // 注视达成后不再渐隐，而是落到脸旁轻轻起伏、每隔几秒换一边（与人脸"作伴"）
    enum IpAnim { Hidden, Appearing, GuidingMove, Companion, FeedbackPulse, Peek, Search, Attentive, Listening }
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
    float _lockStableT;              // 目标持续有效累计（锁定稳定性门：≥0.35s 才认"认识老师"，防虚空锁定）
    float _lostT;                    // 距上次目标有效的时长（"老师消失在视野后"计时）
    float _now;                      // 会话时钟（Tick 驱动；仿真可加速运行）
    float _lockFxCooldown;           // 重获反馈冷却（每次回来都给"找到啦"，6s 内不重复）
    float _pendingLockFxAt = -1f;    // 重获后延迟发庆祝的时刻（等待稳定性确认）
    bool _targetLostEdge;            // 本帧目标丢失边沿
    bool _targetReacquiredEdge;      // 本帧目标重获边沿

    // ── 测试探针/信号（模拟测试机 M3SimHarness 用；2026-10-13）──
    public IM3SimInputs sim;         // 非 null → 全部输入来自模拟（测试机模式，可无设备跑 20 分钟场景）
    public bool simMode => sim != null;
    public float IpAlphaProbe => _ipAlphaSmooth;
    public int IpAnimProbe => (int)_ipAnim;
    public float NowProbe => _now;
    public int StarProbe => _stars != null ? _stars.LitStars : 0;          // 当前已点亮星星数
    public int GuidePhaseProbe => _guide != null ? (int)_guide.Phase : 0;  // 引导阶段
    public int GuideRoundProbe => _guide != null ? _guide.Round : 1;       // 强化轮次
    public float GuideCooldownProbe => _guide != null ? _guide.CooldownRemaining : 0f; // 成功后冷却
    public bool LookingProbe => _lookingNow;                                // 当前注视判定（harness 的眼睛丢失 SLA）
    public bool SpeakingProbe => _speaking;                                 // 当前有效外界讲话（门控后）
    public bool FaceCelebrateProbe => _celebrating;
    public Vector2 IpPosProbe => _ipPos;   // 精灵视口位置（harness 统计"跳变"诊断用）
    public event System.Action<string, string> OnSignal;
    void Signal(string kind, string detail) => OnSignal?.Invoke(kind, detail);

    // ── 人脸居中奖励 / 流星引导（2026-10-20 交互方案 v2）──
    float _faceDiagT;                // 人脸诊断日志节拍（2s）
    float _faceDataT = -99f;         // 上次拿到新人脸检测批次的时刻（新鲜度判定）
    int _faceSeenVer = -1;           // 已见过的人脸检测版本号
    float _faceCenteredDist = 99f;   // 当前主脸中心到视野中心的距离（探针/日志用）
    bool _celebrating;               // 流星雨庆典进行中（探针）
    float _celebrateUntil;           // 庆典结束时刻
    float _celebrateLookAwayT;       // 庆典期间连续移开视线的时长（≥0.5s 柔和收尾）
    float _guideRepeatT;             // 带领阶段语音重复节拍
    float _guideDiagT;               // 引导诊断日志节拍（2s）
    float _pathLogT;                 // 星路诊断事件节拍（2s）
    bool _guideActive;               // 引导流程激活（Confused/Found/Leading）
    bool _attending;                 // 孩子正在看老师（targetValid && looking）——引导的触发/成功口径
    bool _listening;                 // 外界有人讲话且孩子在看老师（对话门控，Task 6 提供）
    bool _lookingNow;                // 本帧注视判定（探针/模拟机 SLA）
    List<DetectionResult> _simFaces; // 模拟人脸框（测试机注入）
    // 统一输出平滑（2026-10-12：消除状态切换的 alpha/scale 阶跃——用户实测"闪现"的主因之一：
    // 日志中 alpha=1.0/scale=1.0 ↔ alpha=0.55/scale=0.85 是瞬跳，模型瞬间缩 30%）
    float _ipAlphaSmooth, _ipScaleSmooth, _ipScaleVel;
    bool _ipSmoothInit;

    float _sampleTimer;
    float _fpsSmooth = 60f;
    float _peekTimer;          // 隐藏期微提示计时
    InteractionState _prevState = InteractionState.Idle;
    static readonly List<DetectionResult> EmptyDetections = new List<DetectionResult>();

    void Start() => InitCore();

    /// <summary>核心初始化（2026-10-13：从 Start 拆出——编辑器测试机需显式调用，Start 在编辑器不自动跑）。</summary>
    public void InitCore()
    {
        if (_sm != null) return; // 幂等
        if (settings != null) settings.ApplyLevel(settings.interventionLevel); // 档位表→阈值字段（须在构造之前）
        _sm = settings != null
            ? new InteractionStateMachine(settings.reGuideCooldownSeconds, settings.feedbackDurationSeconds)
            : new InteractionStateMachine();
        _tracker = new TargetTrackerLogic();
        _gaze = new GazeLogic(
            settings != null ? settings.gazeStartSeconds : 0.8f,
            settings != null ? settings.gazeSustainSeconds : 2f);
        _guide = settings != null
            ? new GuideDirectorLogic(settings.guideConfusedDelay, settings.guideConfusedSeconds, settings.guideFoundSeconds,
                                     settings.guideLeadSeconds, settings.guideLeadSecondsRound3, settings.guideMaxRounds,
                                     1f, 1.5f, settings.guideSilentSeconds, settings.guideRetriggerCooldownSeconds)
            : new GuideDirectorLogic();
        _stars = settings != null
            ? new StarProgressLogic(settings.meteorStarSeconds, settings.meteorStarsPerRing,
                                    settings.meteorShowerCooldown, settings.meteorRingClearSeconds)
            : new StarProgressLogic();
        _conversationGate = settings != null
            ? new ConversationGateLogic(settings.conversationResumeSeconds, settings.selfAudioTailSeconds)
            : new ConversationGateLogic();
        _prevExternalSpeech = false;

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

    void Update() => Tick(Time.deltaTime);

    /// <summary>单帧推进（2026-10-13 重构：从 Update 抽出，测试机可直接以任意 dt 驱动）。</summary>
    public void Tick(float dtInput)
    {
        float dt = dtInput;
        if (dt <= 0f) dt = 1f / 60f;
        _now += dt;
        _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / dt, 0.05f);

        // ── 输入：模拟（测试机）优先 → 键盘/麦克风/VAD ──
        bool vadActive = false, micFallback = false;
        bool simLooking = false, simSpeaking = false, simSpeechEnded = false, simReset = false;
        List<DetectionResult> simDetections = null;
        if (simMode)
        {
            sim.Tick(dt, out simDetections, out _simFaces, out simLooking, out simSpeaking, out simSpeechEnded, out simReset);
            if (simReset) ResetSession();
        }
        else
        {
            vadActive = useMicVad && vadService != null && vadService.MicRunning;
            micFallback = useMicVad && vadService != null && !vadService.MicRunning && !simulateGazeWithKeyboard;
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
        }

        bool speechEnded;     // 喂状态机的"讲话刚结束"边沿
        bool realSpeechEnd = false; // 有效外界讲话结束边沿（日志用）
        bool rawSpeaking = false;
        bool rawSpeechEnded = false;
        bool bypassGate = false; // 键盘模拟/麦克风降级：没有可靠的外部语音检测，不做门控

        if (simMode)
        {
            rawSpeaking = simSpeaking;
            rawSpeechEnded = simSpeechEnded;
        }
        else if (vadActive)
        {
            rawSpeaking = vadService.Logic.Speaking;
            rawSpeechEnded = vadService.Logic.SpeechEnded;
        }
        else if (micFallback)
        {
            // 降级模式（设计 §4.4）：麦克风不可用 → "始终引导" + 注视累计达标直接给反馈
            rawSpeaking = true;
            bypassGate = true;
            if (!_micFallbackLogged)
            {
                _micFallbackLogged = true;
                _logger.LogEvent("MicFallback", $"gaze{settings?.micFallbackFeedbackGazeSeconds ?? 5f}s");
                Debug.Log($"[M3] 麦克风不可用 → 降级模式（始终引导；注视满 {FallbackGazeSec}s 直接反馈）");
            }
            bool due = _gaze.Dwell >= FallbackGazeSec;
            rawSpeechEnded = due && !_fallbackFired;  // 合成一次边沿
            _fallbackFired |= due;
        }
        else
        {
            rawSpeaking = _speaking;
            rawSpeechEnded = _prevSpeaking && !_speaking;
            bypassGate = true;
        }

        // ── v9 对话门控：外界讲话期间精灵语音全部延后；系统自己的 TTS/音效不能骗过 VAD ──
        bool selfAudio = (voiceCue != null && voiceCue.IsPlaying) ||
                         (audioPlayer != null && audioPlayer.IsPlaying);
        if (bypassGate)
        {
            _conversationGate.Reset();
            _speaking = rawSpeaking;
            speechEnded = rawSpeechEnded;
            realSpeechEnd = rawSpeechEnded;
            _listening = false;
            _hasPendingVoice = false;
            _prevGateListening = false;
            voiceCue?.SetGateAllowed(true);
            _prevExternalSpeech = _speaking;
        }
        else
        {
            _conversationGate.Tick(dt, rawSpeaking, selfAudio);
            _speaking = _conversationGate.ExternalSpeech;
            speechEnded = _prevExternalSpeech && !_speaking;
            realSpeechEnd = speechEnded;
            _prevExternalSpeech = _speaking;
            if (_conversationGate.Listening != _prevGateListening)
            {
                _prevGateListening = _conversationGate.Listening;
                _logger?.LogEvent("VoiceGate",
                    $"ext={(_conversationGate.ExternalSpeech ? 1 : 0)} listen={(_prevGateListening ? 1 : 0)} " +
                    $"self={(_conversationGate.SelfAudioSuppressing ? 1 : 0)}");
            }
            voiceCue?.SetGateAllowed(_conversationGate.VoiceAllowed);
            // 静默恢复：补播外界讲话期间挂起的一条（≤2s；过期丢弃，避免播过时引导）
            if (_conversationGate.VoiceAllowed && _hasPendingVoice)
            {
                if (_now - _pendingAt <= 2f)
                {
                    var cue = _pendingVoice;
                    var pr = _pendingPriority;
                    _hasPendingVoice = false;
                    PlayVoice(cue, pr);
                }
                else _hasPendingVoice = false;
            }
            // 注意：_speaking 现在是"有效外界讲话"，状态机不会再把自己的 TTS 当成老师说话
        }
        _prevSpeaking = rawSpeaking;

        // ── 跟踪（选目标 + 头部点 + 滤波）──
        var detections = simMode ? (simDetections ?? EmptyDetections)
                                 : (detector != null ? detector.Latest : EmptyDetections);
        var snapshot = _tracker.Tick(detections, dt);
        if (snapshot.valid) _everHadTarget = true;

        _targetLostEdge = _prevTargetValid && !snapshot.valid;
        _targetReacquiredEdge = !_prevTargetValid && snapshot.valid;
        if (_targetLostEdge) { _logger.LogEvent("TargetLost"); Signal("T", "Lost"); }
        if (_targetReacquiredEdge) { _logger.LogEvent("TargetAcquired"); Signal("T", "Acquired"); }
        _prevTargetValid = snapshot.valid;

        // ── 首寻 3D 漫游（2026-10-20）：第一次找到老师前的主动吸引 ──
        //   （开场不再播 AppearFly 长入场/长问候；0.2s 淡入后直接绕孩子真实漫游）
        UpdateFirstSearch(dt);

        // ── 注视判定 ──
        // 电脑端：视频里"人是否在画面中心"与"孩子是否在看"无关（相机是固定的），
        // 必须用键盘模拟；真机阶段相机会随头转动，才启用几何代理。
        bool looking;
        if (simMode)
        {
            looking = simLooking;
        }
        else if (simulateGazeWithKeyboard)
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
        _lookingNow = looking;
        _gaze.Tick(looking, dt);
        // ★2026-10-21：引导的"在不在看"口径 = 老师被检测到 且 孩子头/视线朝向老师。
        //   此前用 targetValid（老师是否还在相机里）→ 孩子移开视线后老师仍在广角画面中，
        //   真机实测 14s 后才触发寻找。现改为 looking，看开约 0.8s 即开始引导。
        _attending = snapshot.valid && looking;
        _listening = _attending && !bypassGate && _conversationGate.Listening;

        // ── 状态机 ──
        _sm.Tick(new InteractionInputs
        {
            speaking = _speaking,
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
            Signal("State", $"{_prevState}->{_sm.State}");
            _prevState = _sm.State;
        }
        // 降级模式的合成边沿按 Hidden 期为一轮（离开 Hidden 即解锁，下一轮可再触发）
        if (_sm.State != InteractionState.Hidden) _fallbackFired = false;

        // ── 事件 → 音效 / IP 动画 / 日志 ──
        if (_sm.GuideTriggered)
        {
            if (_searchActive)
            {
                // 首寻搜索已经承担开场引导：不播 AppearFly 长入场/长问候，避免两套动作打架
                _logger.LogEvent("GuideStartSuppressed", "firstSearch");
            }
            else
            {
                _logger.LogEvent("GuideStart", _simLooking ? "sim" : "auto");
                Signal("Fx", "Appear");
                Signal("Voice", snapshot.valid ? "GuideAppear" : "Find");
                audioPlayer?.PlayCue(AudioCue.GuideAppear);
                // 吸引模式（还没找到人）说"看看老师在哪儿呀"，正常引导说"看这里呀"
                PlayVoice(snapshot.valid ? VoiceCue.GuideAppear : VoiceCue.Find, VoicePriority.Guide);
                // 打断矩阵：状态机引导优先于庆典
                if (_celebrating)
                {
                    _celebrating = false; _celebrateUntil = _now; _celebrateLookAwayT = 0f;
                    ipWorldView?.EndCelebrate(0.45f);
                }
                StartIpAppear();
                Debug.Log("[M3] 引导出现 → IP 登场");
            }
        }
        if (_gaze.GazeStarted) _logger.LogEvent("GazeStart");
        if (_gaze.GazeSustained) { _logger.LogEvent("GazeSustained"); Signal("Gaze", "Sustained"); }
        if (_sm.FeedbackTriggered)
        {
            _logger.LogEvent("Feedback");
            Signal("Fx", "Feedback");
            Signal("Voice", "Feedback");
            audioPlayer?.PlayCue(AudioCue.Feedback);
            PlayVoice(VoiceCue.Feedback, VoicePriority.Critical);
            // 打断矩阵：庆典期间不穿插反馈位移动画（音效/语音照常）
            if (!_celebrating) StartIpFeedback(snapshot);
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
            // 打断矩阵：庆典/引导期间不做探头（避免与奖励/引导动作冲突）
            if (_peekTimer >= interval && _ipAnim == IpAnim.Hidden && !_celebrating && !_guideActive)
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

        // ── 锁定稳定性 + 重获反馈（2026-10-13 鲁棒性版；引导叙事见下方"流星引导导演"）──
        {
            _lockStableT = snapshot.valid ? _lockStableT + dt : Mathf.Max(0f, _lockStableT - dt * 0.15f);
            _lostT = snapshot.valid ? 0f : _lostT + dt;
            bool stableEnough = _lockStableT >= 0.35f;

            // 重获反馈：边沿后延迟 0.45s"确认"再发（首锁时稳定性尚在累积，边沿即刻判定会被门槛挡住
            // ——2026-10-13 测试机抓到：首锁庆祝被吞）。仍受 6s 冷却防快速来回刷屏。
            if (_targetReacquiredEdge) _pendingLockFxAt = _now + 0.45f;
            if (_pendingLockFxAt > 0f && _now >= _pendingLockFxAt)
            {
                _pendingLockFxAt = -1f;
                if (snapshot.valid && stableEnough && _now >= _lockFxCooldown)
                {
                    _lockFxCooldown = _now + 6f;
                    Signal("Fx", "Lock");
                    ipWorldView?.FxPulse("Lock");
                    Debug.Log("[M3] 重新锁定老师 → 找到啦反馈");
                }
            }
        }

        // ── 人脸候选（假脸过滤）+ 流星星星进度（2026-10-20 交互方案 v2）──
        //   注视老师（人脸居中且属于人体）→ 每 meteorStarSeconds 落一颗星（音阶提示）
        //   → 集满一圈 → 流星雨 + 精灵跳舞；星星只增不减（走神不清零），老师久离才清空
        bool faceCenteredNow = false;
        {
            float radius = settings != null ? settings.faceCenterRadius : 0.22f;

            // 数据源 + 新鲜度：模拟（测试机）或 FaceBoxRunner；超过新鲜度窗口视为没看（不累计）
            int ver = simMode
                ? Mathf.FloorToInt(_now * 30f)                            // 模拟：每 tick 视作批新数据
                : (faceRunner != null ? faceRunner.Version : -1);
            if (ver != _faceSeenVer) { _faceSeenVer = ver; _faceDataT = _now; }
            float freshWin = settings != null ? settings.faceFreshnessSeconds : 0.8f;
            bool freshFaces = _now - _faceDataT < freshWin;
            var faces = simMode ? _simFaces : (faceRunner != null ? faceRunner.LatestFaces : null);

            // 选"最可信的中央脸"：①框宽达标 ②必须属于某个检测到的人体（防海报/纹理假脸——
            // 2026-10-13 真机实测："检出 16 张脸"（房间纹理过检）；无关联时假脸居中也会触发奖励，
            // 奖励与孩子真实行为脱节）→ 取中心离视野中心最近者（= 孩子正在注视的老师的脸）
            bool hasCandidate = false;
            if (freshFaces && faces != null && faces.Count > 0)
            {
                float minW = settings != null ? settings.faceMinBoxWidth : 0.04f;
                float bestD = float.MaxValue;
                for (int i = 0; i < faces.Count; i++)
                {
                    var b = faces[i].normalizedBox;
                    if (b.width < minW) continue;
                    var c = new Vector2(b.x + b.width * 0.5f, b.y + b.height * 0.5f);
                    if (!FaceBelongsToAnyPerson(c, detections)) continue; // 不属于任何人体 → 非老师的脸
                    float d = Vector2.Distance(c, new Vector2(0.5f, 0.5f));
                    if (d < bestD) { bestD = d; hasCandidate = true; }
                }
                if (hasCandidate)
                {
                    _faceCenteredDist = bestD;
                    faceCenteredNow = bestD <= radius;
                }
            }
            if (!hasCandidate) _faceCenteredDist = 99f;

            // 真机诊断（2s 一条；有脸或有星星时打，避免空刷屏）
            _faceDiagT += dt;
            if (_faceDiagT >= 2f && (hasCandidate || _stars.LitStars > 0 || (faces != null && faces.Count > 0)))
            {
                _faceDiagT = 0f;
                string faceInfo = hasCandidate
                    ? $"主脸距中心 {_faceCenteredDist:F2}（{(faceCenteredNow ? "居中✓" : "偏离")}）"
                    : (freshFaces && faces != null && faces.Count > 0
                        ? $"检出 {faces.Count} 张脸但均不属于人体（假脸已过滤）"
                        : "无检出");
                Debug.Log($"[M3] 人脸: {faceInfo} | 星星 {_stars.LitStars}/{_stars.StarsPerRing}" +
                          $"（本颗进度 {_stars.StarProgress01:P0}）" + (_celebrating ? " | 🌠流星雨" : ""));
            }

            // 星星进度推进
            // ★2026-10-20 修（用户实测"流星奖励一次都没见过"）：计分条件从"BlazeFace 人脸居中"
            //   放宽为"孩子正在注视老师"（looking：人体框头部点居中 + 检测新鲜）——复用已验证的注视链路，
            //   人脸检测不再阻塞星星奖励（假脸过滤仍保留在 faceCenteredNow 供引导使用）。
            var sev = _stars.Tick(dt, looking && snapshot.valid);
            if (sev == StarEvent.StarDropped)
            {
                int idx = _stars.LitStars - 1;
                Signal("Star", "Drop");
                _logger.LogEvent("StarDrop", $"{_stars.LitStars}/{_stars.StarsPerRing}");
                meteorView?.LightStar(idx);
                audioPlayer?.PlayStarChime(idx);   // 音阶上行（8 颗星=一个八度）
                if (idx == 0) { PlayVoice(VoiceCue.StarFirst, VoicePriority.Ambient); Signal("Voice", "StarFirst"); }
                Debug.Log($"[M3] ⭐ 第 {_stars.LitStars} 颗星星落成（音阶 {idx + 1}/{_stars.StarsPerRing}）");
            }
            else if (sev == StarEvent.ShowerTriggered)
            {
                _celebrating = true;
                float dur = settings != null ? settings.meteorShowerDuration : 6f;
                _celebrateUntil = _now + dur;
                _celebrateLookAwayT = 0f; // 新庆典重置移开计时（避免上一轮的残留让它立刻收尾）
                // 打断矩阵：庆典优先于探头/反馈位移（回到陪伴位再跳舞）
                if (_ipAnim == IpAnim.Peek || _ipAnim == IpAnim.FeedbackPulse)
                {
                    _ipAnim = IpAnim.Companion; _ipAnimT = 0f;
                    _companionSwitchT = Random.Range(3f, 6f);
                }
                Signal("Star", "Shower");
                Signal("Face", "Celebrate"); // 兼容旧断言（流星雨取代原"20s 跳舞庆典"）
                _logger.LogEvent("MeteorShower");
                if (_ipAnim == IpAnim.Hidden) StartIpAppear(); // 精灵绝不缺席庆典
                meteorView?.PlayShower();
                stardustPath?.SetOn(false);
                audioPlayer?.PlayMeteorShower();
                ClearVoiceQueue(); // 庆典优先：清空排队
                PlayVoice(VoiceCue.MeteorShower, VoicePriority.Critical);
                ipWorldView?.Celebrate(dur);
                ipWorldView?.FxPulse("Celebrate");
                Debug.Log("[M3] 🌠 星星集满一圈 → 流星雨 + 精灵跳舞！");
            }
            else if (sev == StarEvent.Cleared)
            {
                Signal("Star", "Cleared");
                meteorView?.ClearStars();
                _logger.LogEvent("StarRingCleared");
                Debug.Log("[M3] 老师长时间不在 → 星环清空（重新开始积累）");
            }
            // 庆典逻辑（2026-10-20 修）：孩子移开视线 0.5s → 逐步柔和收尾（奖励跟随注视，不空转）
            if (_celebrating)
            {
                // 打断保险（2026-10-20）：引导一旦激活（老师丢失开始找），庆典必须收尾
                if (_guideActive)
                {
                    _celebrating = false; _celebrateUntil = _now; _celebrateLookAwayT = 0f;
                    ipWorldView?.EndCelebrate(0.4f);
                    Debug.Log("[M3] 引导激活 → 庆典强制收尾（打断保险）");
                }
                else
                {
                    if (looking && snapshot.valid) _celebrateLookAwayT = 0f;
                    else _celebrateLookAwayT += dt;
                    // 2026-10-20 二修：0.5s 确认移开 → 0.4s 内收尾（此前 0.8s，用户仍觉在跳）
                    if (_celebrateLookAwayT >= 0.5f && _celebrateUntil > _now + 0.4f)
                    {
                        _celebrateUntil = _now + 0.4f;
                        ipWorldView?.EndCelebrate(0.4f);
                        Debug.Log("[M3] 孩子移开视线 → 庆典停止（0.4s 收尾）");
                    }
                }
            }
            if (_celebrating && _now >= _celebrateUntil)
            {
                _celebrating = false;
                Debug.Log("[M3] 流星雨庆典结束（冷却后重新积累）");
            }
        }

        // ── 流星引导导演（三段式：疑惑→发现→带领→成功/强化/静默；2026-10-20）──
        {
            bool everLocked = _everHadTarget || _lockStableT >= 0.35f;
            // 触发/跟随/静默都以"孩子是否在看老师"为准（不再等老师离开相机视野）
            var gev = _guide.Tick(dt, _attending, everLocked, faceCenteredNow);
            bool hasStars = _stars.LitStars > 0;
            switch (gev)
            {
                case GuideEvent.EnterConfused:
                    Signal("Guide", "Confused");
                    Signal("Voice", "Find"); // 兼容旧断言（I3：丢失后 1.4s 内须有引导语音）
                    PlayVoice(hasStars ? VoiceCue.StarChase : VoiceCue.Confused, VoicePriority.Guide);
                    ipWorldView?.FxPulse("ReGuide");
                    _guideRepeatT = 0f;
                    _logger.LogEvent("GuideConfused", hasStars ? "stars" : "teacher");
                    // ★打断矩阵（2026-10-20）：引导优先——立即结束庆典、取消探头/反馈动画
                    if (_celebrating)
                    {
                        _celebrating = false; _celebrateUntil = _now; _celebrateLookAwayT = 0f;
                        ipWorldView?.EndCelebrate(0.45f);
                        Debug.Log("[M3] 引导开始 → 庆典结束（引导打断庆典）");
                    }
                    if (_ipAnim == IpAnim.Peek || _ipAnim == IpAnim.FeedbackPulse)
                    {
                        _ipAnim = IpAnim.Companion; _ipAnimT = 0f;
                        _companionSwitchT = Random.Range(3f, 6f);
                    }
                    Debug.Log("[M3] 引导：老师去哪里了呢？（疑惑）");
                    break;
                case GuideEvent.EnterFound:
                    Signal("Guide", "Found");
                    PlayVoice(hasStars ? VoiceCue.StarLead : VoiceCue.Found, VoicePriority.Guide);
                    ipWorldView?.FxPulse("Lock");
                    Debug.Log("[M3] 引导：找到方向了！（发现）");
                    break;
                case GuideEvent.EnterLeading:
                    Signal("Guide", $"Lead{_guide.Round}");
                    _guideRepeatT = 0f;
                    Debug.Log($"[M3] 引导：跟我来（第 {_guide.Round} 轮带领）");
                    break;
                case GuideEvent.RoundUp:
                    Signal("Guide", "RoundUp");
                    ipWorldView?.FxPulse("ReGuide");
                    Debug.Log($"[M3] 引导未跟随 → 强化到第 {_guide.Round} 轮");
                    break;
                case GuideEvent.EnterSuccess:
                    Signal("Guide", "Success");
                    ClearVoiceQueue(); // 清掉排队中的"跟我来"——成功之后不应再播引导话术
                    PlayVoice(VoiceCue.Success, VoicePriority.Critical);
                    ipWorldView?.FxPulse("Lock");
                    _logger.LogEvent("GuideSuccess", $"round{_guide.Round}");
                    Debug.Log("[M3] 孩子跟随了 → 找到老师了，真棒！");
                    break;
                case GuideEvent.EnterSilent:
                    Signal("Guide", "Silent");
                    PlayVoice(VoiceCue.Rest, VoicePriority.Ambient);
                    Debug.Log("[M3] 三轮未跟随 → 静默陪伴（等自然时机）");
                    break;
            }

            _guideActive = _guide.Phase == GuidePhase.Confused
                        || _guide.Phase == GuidePhase.Found
                        || _guide.Phase == GuidePhase.Leading;

            if (_guideActive)
            {
                // 带领语音重复节拍（第 2/3 轮更密）——★仅在孩子仍没看老师时喊：
                //   孩子一看老师就安静等待跟随确认（用 looking 而不是"老师是否在相机里"）
                if (!_attending)
                {
                    _guideRepeatT -= dt;
                    if (_guideRepeatT <= 0f)
                    {
                        _guideRepeatT = _guide.Round >= 2 ? 3.5f : 5f;
                        var cue = hasStars ? VoiceCue.StarLead
                                : _guide.Round >= 3 ? VoiceCue.Lead3
                                : _guide.Round >= 2 ? VoiceCue.Lead2 : VoiceCue.Lead1;
                        PlayVoice(cue, VoicePriority.Guide);
                    }
                }
                // ★2026-10-20 修：精灵位置改由 UpdateIpAnim 的"引导模式"统一处理——
                //   此前在这里写 _ipPos 会被每帧的动画状态覆盖（用户实测"精灵没有往老师方向偏移"）。
                if (ipWorldView != null)
                {
                    // 引导诊断（2s 一条）：确认空间锚点方向是否合理
                    _guideDiagT += dt;
                    if (_guideDiagT >= 2f)
                    {
                        _guideDiagT = 0f;
                        Vector2 hint = ipWorldView.TeacherViewportHint();
                        Debug.Log($"[Guide] 老师锚点={ipWorldView.CurrentTeacherPos.ToString("F2")} 方位提示=({hint.x:F2},{hint.y:F2}) " +
                                  $"相位={_guide.Phase} 轮={_guide.Round}");
                    }
                }
            }
            // 星尘路径：引导激活时显示（孩子没在看老师——即使老师仍在相机广角内）
            // 未建立老师空间锚点时不开（否则终点会退化成精灵自身位置 → 方向错误）
            bool pathOn = settings == null || settings.stardustPathEnabled;
            bool hasAnchor = ipWorldView == null || ipWorldView.HasTeacherAnchor;
            stardustPath?.SetOn(pathOn && _guideActive && hasAnchor);
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
            ipWorldView.visible = _celebrating || (_ipAnim != IpAnim.Hidden && _ipAlphaSmooth > 0.01f);
            ipWorldView.x01 = _ipPos.x; ipWorldView.y01 = _ipPos.y;
            if (snapshot.valid) ipWorldView.height01 = snapshot.boxHeight01;
            ipWorldView.alpha = _celebrating ? 1f : _ipAlphaSmooth; // 庆典全亮
            ipWorldView.scale = _ipScaleSmooth * (settings != null ? settings.ipSizeFactor : 1f);
            ipWorldView.tint = _ipAnim == IpAnim.FeedbackPulse ? new Color(1f, 0.95f, 0.6f) : Color.white;
            ipWorldView.companionMode = _ipAnim == IpAnim.Companion; // 陪伴 → 贴真实轮廓
            ipWorldView.sideHint = _companionSide;
            ipWorldView.targetValid = snapshot.valid; // 锚定规则：有效=跟随；丢失=冻结防漂移
            ipWorldView.targetX01 = _heldTargetPos.x; // 老师独立锚点（最后已知视口坐标；路标模式精灵飞走后老师方位单独算）
            ipWorldView.targetY01 = _heldTargetPos.y;
            ipWorldView.signpostMode = _guideActive;   // 路标模式：老师消失 → IP 飞来当路标（目标丢失也重构锚点）
            ipWorldView.pointing = _guideActive;       // "找老师"动作：周期性指向手势 + 身体朝老师侧转
            ipWorldView.faceTeacherBlend = _guideActive ? 0.5f : 0f;
            ipWorldView.animState = (IPWorldView.IPAnimState)(int)_ipAnim; // C：动画状态转发（枚举顺序一一对应）
        }

        // ── 流星/星尘视图推进（真机；模拟模式下视图为空=纯逻辑照跑）──
        if (meteorView != null)
        {
            Vector3 teacherPos = ipWorldView != null ? ipWorldView.CurrentTeacherPos
                                                     : transform.position + transform.forward * 2f;
            meteorView.SetRingTarget(teacherPos);
            meteorView.Tick(_now, dt);
        }
        if (stardustPath != null && stardustPath.IsOn)
        {
            Vector3 teacherPos = ipWorldView != null ? ipWorldView.CurrentTeacherPos
                                                     : transform.position + transform.forward * 2f;
            stardustPath.Tick(_now, dt, teacherPos);
            _pathLogT += dt;
            if (_pathLogT >= 2f)
            {
                _pathLogT = 0f;
                _logger?.LogEvent("StarPath", stardustPath.DebugSnapshot());
                Signal("Path", "Update");
            }
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
        PlayVoice(VoiceCue.Peek, VoicePriority.Ambient);
    }

    Vector3 IpVoicePos() => ipWorldView != null ? ipWorldView.CurrentWorldPos : transform.position;

    /// <summary>统一语音出口（v9 对话门控）：外界讲话时挂起 Critical/Guide 一条，Ambient 丢弃。</summary>
    void PlayVoice(VoiceCue cue, VoicePriority priority)
    {
        if (_conversationGate != null && !_conversationGate.VoiceAllowed)
        {
            _logger?.LogEvent("VoiceBlocked", $"{cue}:{priority}");
            Signal("Voice", "Blocked");
            if (priority != VoicePriority.Ambient)
            {
                _pendingVoice = cue;
                _pendingPriority = priority;
                _pendingAt = _now;
                _hasPendingVoice = true;
            }
            return;
        }
        voiceCue?.Play(cue, IpVoicePos(), priority);
        _logger?.LogEvent("VoicePlayed", $"{cue}:{priority}");
        Signal("Voice", "Played");
    }

    void ClearVoiceQueue()
    {
        _hasPendingVoice = false;
        voiceCue?.ClearQueue();
    }

    /// <summary>脸中心是否落在某个检测到的人体框的"头部区域"内（框上半部 + 少量边距）。
    /// 2026-10-13：把 BlazeFace 的过检假脸（海报/纹理）挡在奖励逻辑外——只认"老师身上的脸"。</summary>
    public static bool FaceBelongsToAnyPerson(Vector2 faceCenter, List<DetectionResult> persons)
    {
        if (persons == null) return false;
        for (int i = 0; i < persons.Count; i++)
            if (FaceBelongsToPerson(faceCenter, persons[i].normalizedBox)) return true;
        return false;
    }

    /// <summary>单框判定（纯函数，EditMode 测试覆盖）。人体框上半部 = 框高的 55% 区域；边距容忍检测抖动。</summary>
    public static bool FaceBelongsToPerson(Vector2 faceCenter, Rect personBox)
    {
        if (personBox.width <= 0f || personBox.height <= 0f) return false;
        return faceCenter.x > personBox.x - 0.05f
            && faceCenter.x < personBox.x + personBox.width + 0.05f
            && faceCenter.y > personBox.y - 0.12f
            && faceCenter.y < personBox.y + personBox.height * 0.55f;
    }

    /// <summary>目标点：有效→刷新保持值；短暂丢失（&lt;2.5s）→保持最后位置（防闪回中央）；彻底无目标→视野中央。</summary>
    Vector2 TargetOrCenter(TargetSnapshot s)
    {
        if (s.valid)
        {
            _heldTargetPos = s.headPoint01;
            _hasHeldTarget = true;
            _heldTargetTime = _now;
            return _heldTargetPos;
        }
        if (_hasHeldTarget && _now - _heldTargetTime < 2.5f) return _heldTargetPos;
        return new Vector2(0.5f, 0.5f);
    }

    /// <summary>平滑追赶后的目标（所有动画状态统一用它，吸收检测抖动）。
    /// ★2026-10-13 修正（自检 I7 实测：单帧跳变最高 0.605 视口单位、1.6 次/分钟"闪现"）：
    ///   此前"跨度 >0.4 直接吸附"在目标换位（如老师从画面中央走到侧面）时造成视觉瞬移；
    ///   现改为：>0.4 用"急促但可见"的追赶（时间常数 0.10s、限速 8/s）——快速飞过去而非闪过去；
    ///   只有 >1.0（重生/跨房间级）才直接吸附。</summary>
    Vector2 SmoothTarget(TargetSnapshot s, float dt)
    {
        var raw = TargetOrCenter(s);
        float d = Vector2.Distance(_smoothTarget, raw);
        if (d > 1.0f)
        {
            _smoothTarget = raw;
            _smoothTargetVel = Vector2.zero;
        }
        else
        {
            float st = d > 0.4f ? 0.10f : 0.15f;
            float ms = d > 0.4f ? 8f : 20f;
            _smoothTarget = Vector2.SmoothDamp(_smoothTarget, raw, ref _smoothTargetVel, st, ms, dt);
        }
        return _smoothTarget;
    }

    /// <summary>首寻 3D 漫游（2026-10-20）：第一次找到老师前，绕孩子 360° 真实飞 + 空间语音吸引。
    /// 没有老师锚点，所以不指向任何方向；目标是让孩子转头/转身找精灵，转头过程中相机扫到老师。</summary>
    void UpdateFirstSearch(float dt)
    {
        bool enabled = settings == null || settings.firstSearchEnabled;
        bool firstPhase = enabled && !_everHadTarget;

        if (firstPhase && !_searchActive)
        {
            // 等透视相机就绪再开始：否则会出现"先听见语音、却看不到精灵"的怪开场。
            // （IPWorldView 需要相机帧位姿来锚定/朝向；相机没就绪时它不会显示模型）
            CameraFrame startFrame = default;
            bool hasFrame = false;
            if (ipWorldView != null && ipWorldView.frameSource != null &&
                ipWorldView.frameSource.TryGetFrame(out startFrame) && startFrame.valid && startFrame.texture != null)
                hasFrame = true;
            bool viewReady = ipWorldView == null || hasFrame;
            if (!viewReady) return;

            // ★2026-10-21 修：启动瞬间 centerEyeAnchor 可能还是 (0,0,0)（真机日志：眼=(0,0,0) 地板Y=-1.5，
            //   精灵钻到地下）。优先用透视相机帧位姿（此时已确认有效），其次中心眼/主相机/模拟兜底。
            float fallbackEyeH = settings != null ? settings.firstSearchEyeHeight : 1.2f;
            Vector3 eyePos = transform.position + Vector3.up * fallbackEyeH;
            Quaternion eyeRot = transform.rotation;
            if (hasFrame)
            {
                eyePos = startFrame.cameraPose.position;
                eyeRot = startFrame.cameraPose.rotation;
            }
            else if (eye != null && eye.position.y > 0.5f)
            {
                eyePos = eye.position;
                eyeRot = eye.rotation;
            }
            else if (Camera.main != null)
            {
                eyePos = Camera.main.transform.position;
                eyeRot = Camera.main.transform.rotation;
            }

            float floorY = eyePos.y - fallbackEyeH;
            Bounds room;
            if (ipWorldView != null && ipWorldView.mruk != null && ipWorldView.mruk.TryGetRoomBounds(out room))
                floorY = room.min.y;
            else
                room = new Bounds(new Vector3(eyePos.x, floorY + 1.5f, eyePos.z), new Vector3(8f, 3f, 8f));

            _firstSearch = new FirstSearchLogic(
                settings != null ? settings.firstSearchMoveSpeed : 1.4f,
                settings != null ? settings.firstSearchRadius : 1.6f,
                settings != null ? settings.firstSearchHoldSeconds : 0.4f,
                settings != null ? settings.firstSearchVoiceInterval : 4f);
            _firstSearch.Begin(eyePos, eyeRot * Vector3.forward, floorY, room);

            _searchActive = true;
            _searchDiagT = 0f;
            _ipAnim = IpAnim.Search;   // 0.2s 快速淡入（不再播 AppearFly 长入场）
            _ipAnimT = 0f;
            _ipAlpha = 0f;
            _ipScale = 0.75f;
            if (ipWorldView != null) { ipWorldView.worldOverrideActive = true; ipWorldView.worldOverridePos = _firstSearch.CurrentTarget; }
            _logger.LogEvent("FirstSearchStart", $"floor{floorY:F1}");
            Signal("Search", "Start");
            Debug.Log($"[Search] 首寻开始：眼={eyePos.ToString("F2")} 地板Y={floorY:F2} " +
                      $"半径={settings?.firstSearchRadius:F1}m 语音间隔={settings?.firstSearchVoiceInterval:F1}s");
        }

        if (!_searchActive) return;

        if (!firstPhase)
        {
            // 第一次检测到老师 → 立即停止漫游（目标流程接管）
            _searchActive = false;
            _firstSearch?.Stop();
            if (ipWorldView != null) ipWorldView.worldOverrideActive = false;
            ClearVoiceQueue(); // 清掉排队中的"找找看"，保证"找到老师啦"清晰
            PlayVoice(VoiceCue.SearchFound, VoicePriority.Critical);
            Signal("Search", "End");
            _logger.LogEvent("FirstSearchEnd", "");
            _ipAnim = _sm.State == InteractionState.Guiding ? IpAnim.GuidingMove : IpAnim.Companion;
            _ipAnimT = 0f;
            Debug.Log("[Search] 第一次看到老师 → 停止漫游，交给正常流程");
            return;
        }

        var res = _firstSearch.Tick(dt);
        if (ipWorldView != null)
        {
            ipWorldView.worldOverrideActive = true;
            ipWorldView.worldOverridePos = res.target;
        }
        if (res.voiceDue)
        {
            var cue = SearchVoiceOrder[Mathf.Clamp(res.voiceSlot, 0, SearchVoiceOrder.Length - 1)];
            PlayVoice(cue, VoicePriority.Ambient);
            Signal("Search", "Voice");
            Debug.Log($"[Search] 语音 {cue}（第 {_firstSearch.VoiceCount} 句）→ 目标={res.target.ToString("F2")}");
        }
        _searchDiagT += dt;
        if (_searchDiagT >= 2f)
        {
            _searchDiagT = 0f;
            Debug.Log($"[Search] 漫游中：目标={res.target.ToString("F2")} 路径点={res.waypointIndex} " +
                      $"已走={_firstSearch.WaypointsVisited}");
        }
    }

    void UpdateIpAnim(float dt, TargetSnapshot snapshot)
    {
        _ipAnimT += dt;
        if (_ipAnim != IpAnim.Hidden) _everShown = true;
        Vector2 tgt = SmoothTarget(snapshot, dt); // 每帧刷新（先于各状态使用）
        // ★引导模式（2026-10-20 修）：老师离开视野时，精灵目标 = 老师相对方位（视野边缘方向）。
        //   此前引导偏移写在动画更新之后，每帧被这里的 tgt 覆盖 → 精灵几乎不偏移（用户实测）。
        if (_guideActive && ipWorldView != null && !_attending)
        {
            Vector2 hint = ipWorldView.TeacherViewportHint();
            tgt = Vector2.Lerp(tgt, hint, 0.85f);
        }
        // 陪伴换边：方位平滑滑过去（不再左右瞬移）
        _companionSideSmooth = Mathf.SmoothDamp(_companionSideSmooth, _companionSide, ref _companionSideVel, 0.45f, 20f, dt);

        // ★2026-10-24 注视/倾听态：孩子在看老师、且没有引导/庆典/反馈/探头/首寻时，
        //   切到安静的 Attentive/Listening（IdleFly），不再播 CompanionIdle 的右臂挥手。
        bool attentiveWanted = _attending && !_guideActive && !_celebrating
            && _sm.State != InteractionState.Guiding
            && _ipAnim != IpAnim.FeedbackPulse && _ipAnim != IpAnim.Peek
            && _ipAnim != IpAnim.Hidden && _ipAnim != IpAnim.Appearing
            && _ipAnim != IpAnim.Search;
        if (attentiveWanted)
        {
            var wanted = _listening ? IpAnim.Listening : IpAnim.Attentive;
            if (_ipAnim != wanted)
            {
                _ipAnim = wanted;
                int code = _listening ? 2 : 1;
                if (_prevListenLogged != code)
                {
                    _prevListenLogged = code;
                    _logger?.LogEvent("ListenState", _listening ? "listening" : "attentive");
                }
            }
        }
        else if (_ipAnim == IpAnim.Attentive || _ipAnim == IpAnim.Listening)
        {
            if (_prevListenLogged != 0)
            {
                _prevListenLogged = 0;
                _logger?.LogEvent("ListenState", "off");
            }
            _ipAnim = _sm.State == InteractionState.Guiding ? IpAnim.GuidingMove : IpAnim.Companion;
        }

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
                _ipPos = tgt + new Vector2(0f, -0.06f + Mathf.Sin(_now * 1.9f) * 0.010f);
                _ipAlpha = 1f;
                _ipScale = 1f;
                if (_sm.State != InteractionState.Guiding) // 离开引导 → 进入"陪伴模式"（不消失）
                {
                    _ipAnim = IpAnim.Companion;
                    _ipAnimT = 0f;
                    _companionSide = Random.value < 0.5f ? -1f : 1f;
                    _companionSwitchT = Random.Range(3f, 6f);
                    // 语音收敛（2026-10-20）：此处不再播"我在这儿"——与引导/成功语音重复会撞车
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
                    -0.035f + Mathf.Sin(_now * 1.35f) * 0.009f);
                _ipScale = 0.85f + 0.05f * Mathf.Sin(_now * 1.7f);
                if (_celebrating)
                {
                    _ipAlpha = 1f; // 庆典期间全亮——奖励必须看得见（2026-10-13）
                }
                else if (_sm.State == InteractionState.Idle)
                {
                    // 待机期：有目标（含短暂丢失保持期）半透明待着；长期无目标也【不消失】——
                    // ★2026-10-13 修复（用户实测："长时间没看到人脸，IP 自己消失了"）：
                    // 设计原则"小精灵不允许消失"——无目标时降到 0.35 半透明守候，绝不淡出为 0。
                    bool targetAlive = snapshot.valid
                        || (_hasHeldTarget && _now - _heldTargetTime < 2.5f);
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
            case IpAnim.Attentive:   // 注视：安静陪伴，轻悬浮（无挥手）
            case IpAnim.Listening:   // 倾听：同姿态；建模师 Listen 动作到位后由 IPWorldView 换动画
            {
                _ipPos = tgt + new Vector2(
                    _companionSideSmooth * 0.10f,
                    -0.03f + Mathf.Sin(_now * 1.35f) * 0.009f);
                _ipScale = 0.85f + 0.05f * Mathf.Sin(_now * 1.7f);
                _ipAlpha = Mathf.MoveTowards(_ipAlpha, 0.55f, dt * 0.8f);
                if (_sm.State == InteractionState.Guiding) _ipAnim = IpAnim.GuidingMove;
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
                float targetAlpha = t < dur - 0.25f ? 1f : 0.2f; // 保守化：淡到 0.2 即止（绝对禁止消失）
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
            case IpAnim.Search: // 首寻漫游：世界坐标由 IPWorldView.worldOverride 驱动；这里维护快速淡入
                _ipAlpha = Mathf.MoveTowards(_ipAlpha, 1f, dt * 8f);
                _ipScale = Mathf.MoveTowards(_ipScale, 1f, dt * 4f);
                break;
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
            // 首寻：快速淡入（0.2s 级别），不再有 1.2s 长入场
            _ipAlphaSmooth = Mathf.MoveTowards(_ipAlphaSmooth, _ipAlpha, dt * (_searchActive ? 8f : 2.2f));
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
        _hasHeldTarget = false;
        _smoothTarget = new Vector2(0.5f, 0.5f);
        _smoothTargetVel = Vector2.zero;
        _companionSideSmooth = _companionSide;
        _companionSideVel = 0f;
        _everShown = false;
        _ipSmoothInit = false;
        _lockStableT = 0f;
        _lockFxCooldown = 0f;
        _targetLostEdge = false;
        _targetReacquiredEdge = false;
        _lostT = 999f;
        _celebrating = false;
        _conversationGate?.Reset();
        _prevExternalSpeech = false;
        _listening = false;
        _prevGateListening = false;
        _prevListenLogged = -1;
        _hasPendingVoice = false;
        voiceCue?.SetGateAllowed(true);
        _firstSearch?.Stop();
        _searchActive = false;
        _searchDiagT = 0f;
        if (ipWorldView != null) ipWorldView.worldOverrideActive = false;
        _faceSeenVer = -1;
        _faceDataT = -99f;
        _simFaces = null;
        _guide?.Reset();
        _stars?.Reset();
        _guideRepeatT = 0f;
        _guideActive = false;
        meteorView?.ClearStars();
        stardustPath?.SetOn(false);
        Debug.Log($"[M3] 会话已重置：{_logger.SessionDir}");
    }

    void OnDestroy()
    {
        _logger?.Close();
    }
}
