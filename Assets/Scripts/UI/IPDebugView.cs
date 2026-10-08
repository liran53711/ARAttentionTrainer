// Assets/Scripts/UI/IPDebugView.cs
// 电脑端 IP（小精灵）占位显示：在视频画面上画一个程序生成的圆形占位角色。
// 驱动层（M3DebugDriver）每帧喂视觉参数；本组件不做任何逻辑。
// 组员产出真实 2D 精灵素材后，替换本文件里的贴图与绘制即可（接口不变）。
using UnityEngine;

public class IPDebugView : MonoBehaviour
{
    // 由驱动层每帧设置（归一化坐标，左上原点）
    public bool visible;
    public float x01 = 0.5f, y01 = 0.5f;
    public float alpha = 1f;
    public float scale = 1f;
    public Color tint = Color.white;

    /// <summary>驱动层写入的一行状态（画在屏幕上，调试用）</summary>
    public string statusLine = "";

    Texture2D _disc;
    VideoFileFrameSource _source;

    void Awake() { _source = GetComponent<VideoFileFrameSource>(); }

    void EnsureTexture()
    {
        if (_disc != null) return;
        const int s = 64;
        _disc = new Texture2D(s, s, TextureFormat.RGBA32, false);
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
                // 简单"脸"：紫色圆 + 两个眼睛点
                c = new Color32(170, 128, 255, 255);
                float ex = Mathf.Abs(dx) - r * 0.30f, ey = dy + r * 0.12f;
                if (ex * ex + ey * ey < (r * 0.10f) * (r * 0.10f))
                    c = new Color32(40, 30, 70, 255);
            }
            else if (ring) c = new Color32(255, 255, 255, 190);
            else c = new Color32(0, 0, 0, 0);
            px[y * s + x] = c;
        }
        _disc.SetPixels32(px);
        _disc.Apply();
    }

    void OnGUI()
    {
        EnsureTexture();

        // 视频显示区域（与 VideoDebugDisplay 的 ScaleToFit 一致）
        float areaX = 0f, areaY = 0f, areaW = Screen.width, areaH = Screen.height;
        if (_source != null && _source.OutputTexture != null)
        {
            float vw = _source.OutputTexture.width, vh = _source.OutputTexture.height;
            float fit = Mathf.Min(Screen.width / vw, Screen.height / vh);
            areaW = vw * fit; areaH = vh * fit;
            areaX = (Screen.width - areaW) * 0.5f;
            areaY = (Screen.height - areaH) * 0.5f;
        }

        if (visible && alpha > 0.01f)
        {
            float size = Mathf.Min(areaW, areaH) * 0.18f * scale;
            float cx = areaX + x01 * areaW;
            float cy = areaY + y01 * areaH;
            var prev = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size),
                _disc, ScaleMode.ScaleToFit, true);
            GUI.color = prev;
        }

        // 状态栏（解释"为什么精灵出现/消失了"）
        if (!string.IsNullOrEmpty(statusLine))
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            st.normal.textColor = new Color(0.65f, 1f, 0.65f);
            GUI.Label(new Rect(10, 66, 1400, 24), statusLine, st);
        }

        // 操作提示
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        style.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(10, Screen.height - 30, 900, 22),
            "K = 切换模拟注视　L = 切换模拟讲话　R = 重置会话", style);
    }
}
