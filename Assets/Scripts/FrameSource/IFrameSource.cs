// Assets/Scripts/FrameSource/IFrameSource.cs
// 帧源抽象接口：真机 = PCA 相机；电脑 Editor = 视频文件 / 网络摄像头。
// 目的：让检测、状态机等大部分逻辑不用戴头显就能开发和调试（约 70% 的工作量）。
public interface IFrameSource
{
    /// <summary>帧源是否已就绪（相机已启动 / 视频已开始播放）。</summary>
    bool IsReady { get; }

    /// <summary>取最新一帧。返回 false 表示当前无可用帧。</summary>
    bool TryGetFrame(out CameraFrame frame);
}
