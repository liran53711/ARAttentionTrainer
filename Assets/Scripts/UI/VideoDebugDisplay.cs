// Assets/Scripts/UI/VideoDebugDisplay.cs
// 调试显示：把视频帧源的画面全屏画在 Game 视图上，并显示帧状态。
// 用 OnGUI 实现 —— 不依赖 UI 系统配置，放哪都能看到。
using UnityEngine;

[RequireComponent(typeof(VideoFileFrameSource))]
public class VideoDebugDisplay : MonoBehaviour
{
    VideoFileFrameSource _source;
    int _frameCount;
    long _lastTimestampNs = -1;

    void Awake() { _source = GetComponent<VideoFileFrameSource>(); }

    void Update()
    {
        // 统计帧推进次数（时间戳变化 = 视频在动）
        if (_source.IsReady && _source.TryGetFrame(out var frame) && frame.timestampNs != _lastTimestampNs)
        {
            _lastTimestampNs = frame.timestampNs;
            _frameCount++;
        }
    }

    void OnGUI()
    {
        if (_source.OutputTexture != null)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _source.OutputTexture, ScaleMode.ScaleToFit);

        var style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        style.normal.textColor = _source.IsReady ? Color.green : Color.yellow;
        GUI.Label(new Rect(10, 10, 700, 30),
            $"帧源: {(_source.IsReady ? "运行中 ✓" : "未就绪（等视频加载/检查文件名）")}   " +
            $"已验证帧: {_frameCount}   时间戳: {_lastTimestampNs / 1_000_000} ms", style);
    }
}
