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
    public IPDebugView ipView;
    public AudioCuePlayer audioPlayer;
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

    // 模拟输入状态
    bool _speaking = true;        // 默认"老师在讲话"
    bool _prevSpeaking = true;
    bool _prevTargetValid;
    bool _simLooking;             // K 键：模拟"孩子在注视"

    // IP 动画运行时
    enum IpAnim { Hidden, Appearing, GuidingMove, Hiding, FeedbackPulse, Peek }
    IpAnim _ipAnim = IpAnim.Hidden;
    float _ipAnimT;
    Vector2 _ipFrom = new Vector2(0.5f, 0.5f);
    Vector2 _ipPos = new Vector2(0.5f, 0.5f);
    float _ipAlpha;
    float _ipScale = 1f;

    float _sampleTimer;
    float _fpsSmooth = 60f;
    float _peekTimer;          // 隐藏期微提示计时
    InteractionState _prevState = InteractionState.Idle;
    static readonly List<DetectionResult> EmptyDetections = new List<DetectionResult>();

    void Start()
    {
        _sm = settings != null
            ? new InteractionStateMachine(settings.reGuideCooldownSeconds, settings.feedbackDurationSeconds)
            : new InteractionStateMachine();
        _tracker = new TargetTrackerLogic();
        _gaze = new GazeLogic(
            settings != null ? settings.gazeStartSeconds : 0.8f,
            settings != null ? settings.gazeSustainSeconds : 2f);

        _logger = new SessionLogger(Application.persistentDataPath);
        _logger.LogEvent("SessionStart");
        Debug.Log($"[M3] 会话数据目录：{_logger.SessionDir}");
        string speechMode = (useMicVad && vadService != null && vadService.MicRunning)
            ? "麦克风VAD（对着麦克风讲话＝老师在说话）"
            : "键盘L模拟";
        Debug.Log($"[M3] 讲话输入：{speechMode}");
        Debug.Log("[M3] 键盘：K=切换模拟注视 | L=切换模拟讲话(仅无麦时) | R=重置会话");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) dt = 1f / 60f;
        _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / dt, 0.05f);

        // ── 输入：注视（键盘K模拟）+ 讲话（麦克风VAD优先，键盘L回退）──
        bool vadActive = useMicVad && vadService != null && vadService.MicRunning;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.kKey.wasPressedThisFrame) _simLooking = !_simLooking;
            if (kb.lKey.wasPressedThisFrame && !vadActive) _speaking = !_speaking;
            if (kb.rKey.wasPressedThisFrame) ResetSession();
        }

        bool speechEnded;
        if (vadActive)
        {
            speechEnded = vadService.Logic.SpeechEnded;
            _speaking = vadService.Logic.Speaking;
        }
        else
        {
            speechEnded = _prevSpeaking && !_speaking;
        }
        _prevSpeaking = _speaking;

        // ── 跟踪（选目标 + 头部点 + 滤波）──
        var detections = detector != null ? detector.Latest : EmptyDetections;
        var snapshot = _tracker.Tick(detections, dt);

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
            _prevState = _sm.State;
        }

        // ── 事件 → 音效 / IP 动画 / 日志 ──
        if (_sm.GuideTriggered)
        {
            _logger.LogEvent("GuideStart", _simLooking ? "sim" : "auto");
            audioPlayer?.PlayCue(AudioCue.GuideAppear);
            StartIpAppear();
            Debug.Log("[M3] 引导出现 → IP 登场");
        }
        if (_gaze.GazeStarted) _logger.LogEvent("GazeStart");
        if (_gaze.GazeSustained) _logger.LogEvent("GazeSustained");
        if (_sm.FeedbackTriggered)
        {
            _logger.LogEvent("Feedback");
            audioPlayer?.PlayCue(AudioCue.Feedback);
            StartIpFeedback(snapshot);
            Debug.Log("[M3] 正反馈！IP 重现");
        }
        if (speechEnded) _logger.LogEvent("SpeechEnd");

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
        if (ipView != null)
        {
            ipView.visible = _ipAnim != IpAnim.Hidden && _ipAlpha > 0.01f;
            ipView.x01 = _ipPos.x; ipView.y01 = _ipPos.y;
            ipView.alpha = _ipAlpha; ipView.scale = _ipScale;
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
    }

    void StartIpAppear()
    {
        _ipFrom = new Vector2(0.5f, 0.5f); // 从视野中央出现
        _ipPos = _ipFrom;
        _ipAnim = IpAnim.Appearing;
        _ipAnimT = 0f;
        _ipAlpha = 0f;
        _ipScale = 0.6f;
    }

    void StartIpFeedback(TargetSnapshot snapshot)
    {
        // 从目标附近的随机方位"探出头来"
        var basePos = snapshot.valid ? snapshot.headPoint01 : new Vector2(0.5f, 0.5f);
        float ang = Random.Range(0f, Mathf.PI * 2f);
        _ipPos = basePos + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.12f;
        _ipAnim = IpAnim.FeedbackPulse;
        _ipAnimT = 0f;
        _ipAlpha = 0f;
        _ipScale = 0.5f;
    }

    void StartIpPeek(TargetSnapshot snapshot)
    {
        // 从老师头的左/右侧短暂探出头来（不打断注视的"微提示"）
        var basePos = snapshot.valid ? snapshot.headPoint01 : new Vector2(0.5f, 0.5f);
        float side = Random.value < 0.5f ? -1f : 1f;
        _ipPos = basePos + new Vector2(side * 0.10f, Random.Range(-0.05f, 0.02f));
        _ipAnim = IpAnim.Peek;
        _ipAnimT = 0f;
        _ipScale = 0.7f;
    }

    Vector2 TargetOrCenter(TargetSnapshot s) => s.valid ? s.headPoint01 : new Vector2(0.5f, 0.5f);

    void UpdateIpAnim(float dt, TargetSnapshot snapshot)
    {
        _ipAnimT += dt;
        switch (_ipAnim)
        {
            case IpAnim.Appearing: // 0.5s 渐入，同时向目标移动（共 1.2s）
            {
                float k = Mathf.Clamp01(_ipAnimT / 0.5f);
                _ipAlpha = k;
                _ipScale = Mathf.Lerp(0.6f, 1f, k);
                _ipPos = Vector2.Lerp(_ipFrom, TargetOrCenter(snapshot) + new Vector2(0f, -0.06f),
                    Mathf.Clamp01(_ipAnimT / 1.2f));
                if (_ipAnimT >= 1.2f) { _ipAnim = IpAnim.GuidingMove; _ipAnimT = 0f; }
                break;
            }
            case IpAnim.GuidingMove: // 悬浮在目标上方，轻微起伏
                _ipPos = TargetOrCenter(snapshot) + new Vector2(0f, -0.06f + Mathf.Sin(Time.time * 3f) * 0.012f);
                _ipAlpha = 1f;
                _ipScale = 1f;
                if (_sm.State != InteractionState.Guiding) // 离开引导 → 开始隐藏
                {
                    _ipAnim = IpAnim.Hiding;
                    _ipAnimT = 0f;
                    audioPlayer?.PlayCue(AudioCue.Hide);
                }
                break;
            case IpAnim.Hiding: // 1s 渐隐（"躲起来"，注意力交付给人脸）
                _ipAlpha = 1f - Mathf.Clamp01(_ipAnimT / 1f);
                if (_ipAnimT >= 1f) { _ipAnim = IpAnim.Hidden; _ipAlpha = 0f; }
                break;
            case IpAnim.FeedbackPulse: // 闪现 + 脉冲
            {
                float appear = Mathf.Clamp01(_ipAnimT / 0.15f);
                _ipAlpha = appear;
                _ipScale = 0.5f + 0.5f * appear + 0.15f * Mathf.Sin(_ipAnimT * 10f) * appear;
                float dur = settings != null ? settings.feedbackDurationSeconds : 2f;
                if (_ipAnimT >= dur) { _ipAnim = IpAnim.Hidden; _ipAlpha = 0f; }
                break;
            }
            case IpAnim.Peek: // 短暂探头：0.2s 淡入 → 保持 → 0.25s 淡出（带轻微摆动）
            {
                float dur = settings != null ? settings.peekDurationSeconds : 0.8f;
                const float fadeIn = 0.2f, fadeOut = 0.25f;
                float t = _ipAnimT;
                if (t < fadeIn) _ipAlpha = t / fadeIn;
                else if (t < dur - fadeOut) _ipAlpha = 1f;
                else _ipAlpha = Mathf.Clamp01((dur - t) / fadeOut);
                _ipScale = Mathf.Lerp(0.75f, 0.95f, Mathf.PingPong(t * 1.2f, 1f));
                if (t >= dur) { _ipAnim = IpAnim.Hidden; _ipAlpha = 0f; }
                break;
            }
            case IpAnim.Hidden:
                _ipAlpha = 0f;
                break;
        }
    }

    void ResetSession()
    {
        _logger?.Close();
        _logger = new SessionLogger(Application.persistentDataPath);
        _logger.LogEvent("SessionStart");
        _tracker.Reset();
        _gaze.Reset();
        _sm.Reset();
        _ipAnim = IpAnim.Hidden;
        _ipAlpha = 0f;
        _prevTargetValid = false;
        Debug.Log($"[M3] 会话已重置：{_logger.SessionDir}");
    }

    void OnDestroy()
    {
        _logger?.Close();
    }
}
