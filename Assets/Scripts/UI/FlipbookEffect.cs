// Assets/Scripts/UI/FlipbookEffect.cs
// 序列帧动效播放器（2026-10-20 动效重构核心组件）。
// 用途：把"静态贴图 + 程序化缩放"升级为"序列帧图集动画"——泡泡表情、光晕/光环等统一走这里。
// 原理：图集纹理上按行优先网格切帧，逐帧推进材质的 _MainTex 缩放/偏移（UV 动画），零解码开销。
// 资源约定见 FlipbookLibrary / docs/specs/2026-10-20-flipbook-effects-requirements.md。
using UnityEngine;

public class FlipbookEffect : MonoBehaviour
{
    [Header("播放配置（Bind 时设置）")]
    public int cols = 8;          // 横向帧数
    public int rows = 1;          // 纵向行数（多行时按"从左到右、从上到下"播放）
    public float fps = 12f;
    public bool loop = true;

    MeshRenderer _renderer;
    Material _mat;
    float _t;
    int _frame = -1;
    bool _playing;

    public bool IsPlaying => _playing;
    public int TotalFrames => Mathf.Max(1, cols * rows);

    /// <summary>绑定到渲染器并替换其材质纹理为图集（实例材质，不影响共享资源）。</summary>
    public void Bind(MeshRenderer r, Texture2D atlas, int cols, int rows, float fps, bool loop)
    {
        _renderer = r;
        this.cols = Mathf.Max(1, cols);
        this.rows = Mathf.Max(1, rows);
        this.fps = Mathf.Max(1f, fps);
        this.loop = loop;
        if (r != null)
        {
            if (r.sharedMaterial == null)
            {
                r.sharedMaterial = new Material(Shader.Find("ARAttention/GuideArrow"));
            }
            // 每实例独立材质（UV/颜色互不影响）
            _mat = new Material(r.sharedMaterial);
            r.sharedMaterial = _mat;
        }
        if (_mat != null && atlas != null)
        {
            _mat.SetTexture("_MainTex", atlas);
            _mat.SetTextureScale("_MainTex", new Vector2(1f / this.cols, 1f / this.rows));
        }
        ApplyFrame(0);
    }

    /// <summary>从第一帧开始播放（或续播）。</summary>
    public void Play(bool fromStart = true)
    {
        _playing = true;
        if (fromStart) { _t = 0f; _frame = -1; ApplyFrame(0); }
    }

    public void Stop() { _playing = false; }

    void Update() => Advance(Time.deltaTime);

    /// <summary>推进动画（预览/测试可直接调用）。</summary>
    public void Advance(float dt)
    {
        if (!_playing || _mat == null) return;
        _t += dt;
        int f = Mathf.FloorToInt(_t * fps);
        if (f >= TotalFrames)
        {
            if (loop) f %= TotalFrames;
            else { f = TotalFrames - 1; _playing = false; }
        }
        ApplyFrame(f);
    }

    void ApplyFrame(int f)
    {
        if (f == _frame || _mat == null) return;
        _frame = f;
        int col = f % cols;
        int row = f / cols;                                     // 行优先：0=第一行（视觉顶部）
        float ox = col / (float)cols;
        float oy = (rows - 1 - row) / (float)rows;              // UV v 向上 → 顶部行偏移最大
        _mat.SetTextureOffset("_MainTex", new Vector2(ox, oy));
    }
}
