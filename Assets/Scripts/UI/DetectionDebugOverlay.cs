// Assets/Scripts/UI/DetectionDebugOverlay.cs
// 电脑端调试：把检测框（归一化坐标，左上角原点）画在视频画面的显示区域上。
// 仅用于 Editor/PC；真机阶段用世界空间框（M3 实现）。
using UnityEngine;

[RequireComponent(typeof(VideoFileFrameSource))]
public class DetectionDebugOverlay : MonoBehaviour
{
    public FaceDetector detector;

    VideoFileFrameSource _source;

    void Awake() { _source = GetComponent<VideoFileFrameSource>(); }

    void OnGUI()
    {
        if (detector == null || _source == null || _source.OutputTexture == null) return;

        // 计算视频在屏幕上的显示区域（与 VideoDebugDisplay 的 ScaleToFit 保持一致）
        float vw = _source.OutputTexture.width, vh = _source.OutputTexture.height;
        float scale = Mathf.Min(Screen.width / vw, Screen.height / vh);
        float w = vw * scale, h = vh * scale;
        var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        // 绿色框 + 置信度标注
        const float t = 2f;
        var confStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        confStyle.normal.textColor = Color.green;

        GUI.color = Color.green;
        foreach (var d in detector.Latest)
        {
            var b = d.normalizedBox;
            var r = new Rect(
                area.x + b.x * area.width,
                area.y + b.y * area.height,
                b.width * area.width,
                b.height * area.height);

            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.y - 20f, 160f, 18f), $"person {d.confidence:0.00}", confStyle);
            GUI.color = Color.green;
        }
        GUI.color = Color.white;

        // 汇总信息
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        style.normal.textColor = Color.green;
        GUI.Label(new Rect(10, 40, 700, 24),
            $"检出: {detector.Latest.Count}   推理+回读耗时: {detector.LastInferenceMs:0.0} ms", style);
    }
}
