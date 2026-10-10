// Assets/Scripts/UI/DeviceDataPanel.cs
// 真机数据面板：世界空间面板显示运行数据（替代原来的相机画面监视器）。
// 4Hz 刷新文本；数据源全部来自现有组件，本组件不含任何逻辑。
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class DeviceDataPanel : MonoBehaviour
{
    [Header("引用")]
    public FaceDetector detector;
    public MicVadService vad;
    public M3DebugDriver driver;
    public MrukBootstrap mruk;    // 空间数据状态（未就绪时显示一行提示）
    public Text text;

    float _timer;
    readonly StringBuilder _sb = new StringBuilder(256);

    void Update()
    {
        if (text == null) return;
        _timer += Time.deltaTime;
        if (_timer < 0.25f) return; // 4Hz 足够看，省开销
        _timer = 0f;

        _sb.Clear();
        _sb.Append("FPS ").Append(Mathf.RoundToInt(driver != null ? driver.SmoothFps : 0f));
        _sb.Append("   推理 ").Append(detector != null ? $"{detector.LastInferenceMs:F0}ms" : "--");
        _sb.Append("   检测 ").Append(detector != null ? detector.Latest.Count : 0).Append('\n');

        _sb.Append("状态 ").Append(driver != null ? driver.State.ToString() : "--");
        _sb.Append("   档位 ").Append(driver != null ? driver.LevelName : "--");
        _sb.Append("   注视 ").Append(driver != null ? driver.GazeDwell.ToString("F1") : "0.0").Append("s\n");

        if (vad != null)
        {
            _sb.Append("VAD ").Append(vad.LastEnergyDb.ToString("F0")).Append("dB");
            _sb.Append("  底").Append(vad.Logic != null ? vad.Logic.NoiseFloorDb.ToString("F0") : "--").Append("dB");
            _sb.Append(vad.MicRunning ? "  麦✓" : "  麦✗");
        }
        else
        {
            _sb.Append("VAD --");
        }

        if (mruk != null && !mruk.HasRoomData)
            _sb.Append('\n').Append("空间 ").Append(mruk.Summary);

        text.text = _sb.ToString();
    }
}
