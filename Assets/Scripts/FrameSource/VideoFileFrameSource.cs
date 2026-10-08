// Assets/Scripts/FrameSource/VideoFileFrameSource.cs
// 视频文件帧源：在电脑上开发调试用（不戴头显）。
// 原理：Unity VideoPlayer 把视频播到 RenderTexture，每帧打包成 CameraFrame。
// 用法：测试视频放到 Assets/StreamingAssets/ 下（如 test.mp4），场景里挂本组件。
// 注意：本帧源只用于 Editor/PC；真机走 PcaFrameSource。
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class VideoFileFrameSource : FrameSourceBehaviour
{
    [Header("视频")]
    [Tooltip("StreamingAssets 下的文件名，例如 test.mp4")]
    public string videoFileName = "test.mp4";

    [Tooltip("内部渲染分辨率（无需和原视频一致）")]
    public int renderTextureSize = 512;

    [Header("假相机参数（Editor 近似值；真机从 PCA 内参取真值）")]
    [Tooltip("像素焦距：约等于 画面宽度 / (2*tan(水平FOV/2))")]
    public float assumedFocalLengthPx = 800f;

    [Tooltip("假设的相机位置（Editor 没有真头显，用固定值）")]
    public Vector3 assumedCameraPosition = new Vector3(0f, 1.5f, 0f);

    /// <summary>供调试显示用的渲染纹理（视频画面）。</summary>
    public RenderTexture OutputTexture { get; private set; }

    VideoPlayer _player;

    public override bool IsReady => _player != null && _player.isPrepared && _player.frame > 0;

    void Awake()
    {
        string path = Path.Combine(Application.streamingAssetsPath, videoFileName);
#if !UNITY_ANDROID
        if (!File.Exists(path))
        {
            Debug.LogError($"[VideoFileFrameSource] 找不到视频文件：{path}\n" +
                           "请把测试视频放到 Assets/StreamingAssets/ 下，文件名与 Inspector 中一致。");
            return;
        }
#else
        // Android 上 StreamingAssets 在 APK 内部（jar 路径），File.Exists 不可用；
        // 直接把路径交给 VideoPlayer，它能处理 jar:file:// 形式的 URL。
#endif

        OutputTexture = new RenderTexture(renderTextureSize, renderTextureSize, 0);
        OutputTexture.Create();

        _player = gameObject.AddComponent<VideoPlayer>();
        _player.source = VideoSource.Url;
        _player.url = path;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.targetTexture = OutputTexture;
        _player.isLooping = true;
        _player.playOnAwake = false;
        // 播放视频原声：麦克风（VAD）能"听到老师讲话"，实现全自动闭环演示
        _player.audioOutputMode = VideoAudioOutputMode.Direct;
        _player.prepareCompleted += _ => _player.Play();
        _player.Prepare();
    }

    void OnDestroy()
    {
        if (OutputTexture != null)
        {
            OutputTexture.Release();
            Destroy(OutputTexture);
        }
    }

    public override bool TryGetFrame(out CameraFrame frame)
    {
        frame = default;
        if (!IsReady) return false;

        frame.texture = OutputTexture;
        frame.cameraPose = new Pose(assumedCameraPosition, Quaternion.identity);
        frame.focalLengthPx = assumedFocalLengthPx;
        frame.timestampNs = (long)(_player.time * 1e9);
        frame.valid = true;
        return true;
    }
}
