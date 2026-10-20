// Assets/Tests/EditMode/StardustPathTests.cs
// 星尘路几何单测（2026-10-20 v6）：验证"起点在视野内 / 终点在老师脚下 / 绕开孩子 / 方向正确"。
using NUnit.Framework;
using UnityEngine;

public class StardustPathTests
{
    const float EyeY = 1.6f;
    const float Floor = 0f;

    static void Path(Vector3 teacher,
                     out Vector3 p0, out Vector3 c1, out Vector3 c2, out Vector3 p3)
    {
        StardustPath.ComputePath(new Vector3(0f, EyeY, 0f), Vector3.forward, Floor, teacher,
            0.90f, 0.65f, 0.95f, 0.30f, out p0, out c1, out c2, out p3);
    }

    [Test]
    public void Start_IsInFrontAndInsideVerticalFov()
    {
        Path(new Vector3(0f, 1.3f, 3f), out var p0, out _, out _, out _);
        Assert.Greater(p0.z, 0.5f, "路头必须在孩子前方");
        Assert.Less(p0.y, EyeY, "路头必须低于视线");
        float depression = Mathf.Atan2(EyeY - p0.y, p0.z) * Mathf.Rad2Deg;
        Assert.Less(depression, 45f, "路头必须在垂直视野内（否则看不见路的开头）");
        Assert.Greater(depression, 10f, "路头应明显低于视线，形成 3D 下垂感");
    }

    [Test]
    public void End_IsAtTeacherFeet_WithinSideOffset()
    {
        var t = new Vector3(2f, 1.3f, 1.5f);
        Path(t, out _, out _, out _, out var p3);
        Assert.AreEqual(Floor + 0.05f, p3.y, 0.001f, "终点应贴地（老师脚下）");
        var flat = new Vector2(p3.x - t.x, p3.z - t.z).magnitude;
        Assert.AreEqual(0.30f, flat, 0.02f, "终点应在老师身侧 0.3m");
    }

    [Test]
    public void End_IsOnSideAwayFromChild()
    {
        var t = new Vector3(1.2f, 1.3f, -1.0f);
        Path(t, out _, out _, out _, out var p3);
        // 老师到孩子的方向 = (-1.2,0,1)；终点偏移应与该方向相反（远离孩子）
        Vector3 teacherToChild = new Vector3(-t.x, 0f, -t.z).normalized;
        Vector3 off = new Vector3(p3.x - t.x, 0f, p3.z - t.z).normalized;
        Assert.Less(Vector3.Dot(teacherToChild, off), 0f, "路尾应落在远离孩子的一侧");
    }

    [Test]
    public void Curve_KeepsClearanceFromChild_TeacherBehind()
    {
        var t = new Vector3(0f, 1.3f, -2.6f);
        Path(t, out var p0, out var c1, out var c2, out var p3);
        float min = float.MaxValue;
        for (int i = 0; i <= 200; i++)
        {
            var p = StardustPath.CubicBezier(p0, c1, c2, p3, i / 200f);
            min = Mathf.Min(min, new Vector2(p.x, p.z).magnitude);
        }
        Assert.Greater(min, 0.8f, $"老师位于正后方时路径也不能穿过孩子（最近 {min:F2}m）");
    }

    [Test]
    public void Curve_KeepsClearanceFromChild_TeacherBehindRight()
    {
        var t = new Vector3(1.9f, 1.3f, -1.7f);
        Path(t, out var p0, out var c1, out var c2, out var p3);
        float min = float.MaxValue;
        for (int i = 0; i <= 200; i++)
        {
            var p = StardustPath.CubicBezier(p0, c1, c2, p3, i / 200f);
            min = Mathf.Min(min, new Vector2(p.x, p.z).magnitude);
        }
        Assert.Greater(min, 0.8f, $"侧后方路径也不能穿过孩子（最近 {min:F2}m）");
    }

    [Test]
    public void Curve_EndsAtTeacherSide_ForAllBearings()
    {
        var cases = new[]
        {
            new Vector3(0f, 1.3f, 3f),      // 前
            new Vector3(2.4f, 1.3f, 1.2f),  // 右前
            new Vector3(2.8f, 1.3f, 0.2f),  // 右
            new Vector3(1.9f, 1.3f, -1.7f), // 右后
            new Vector3(0f, 1.3f, -2.6f),   // 后
        };
        foreach (var t in cases)
        {
            Path(t, out var p0, out var c1, out var c2, out var p3);
            var end = StardustPath.CubicBezier(p0, c1, c2, p3, 1f);
            Assert.Less(Vector3.Distance(new Vector3(end.x, 0f, end.z),
                                         new Vector3(t.x, 0f, t.z)), 0.35f,
                        $"终点应落在老师身旁（老师={t}）");
            // 方向：路线整体应向老师一侧推进（最后 25% 的平均位置比路头更接近老师）
            var mid = StardustPath.CubicBezier(p0, c1, c2, p3, 0.5f);
            float d0 = new Vector2(p0.x - t.x, p0.z - t.z).magnitude;
            float dm = new Vector2(mid.x - t.x, mid.z - t.z).magnitude;
            Assert.Less(dm, d0, $"路径中段应比路头更接近老师（老师={t}）");
        }
    }

    [Test]
    public void RoadWidth_ShrinksForShortPaths()
    {
        Assert.Less(StardustPath.RoadHalfWidthFor(0.4f, 0.5f), 0.2f,
            "很短的星路不能保持 1m 宽，否则会变成一团星星");
        Assert.AreEqual(0.5f, StardustPath.RoadHalfWidthFor(5f, 0.5f), 1e-3f,
            "长星路保持设定路宽");
        Assert.GreaterOrEqual(StardustPath.RoadHalfWidthFor(0.01f, 0.5f), 0.05f,
            "再短也保留最小可见宽度");
    }

    [Test]
    public void CubicBezier_HitsBothEndpoints()
    {
        var a = new Vector3(-1f, 0.5f, 2f);
        var b = new Vector3(0f, 3f, 1f);
        var c = new Vector3(2f, 0.1f, -1f);
        var d = new Vector3(1.2f, 0f, 4f);
        Assert.Less(Vector3.Distance(a, StardustPath.CubicBezier(a, b, c, d, 0f)), 1e-4f);
        Assert.Less(Vector3.Distance(d, StardustPath.CubicBezier(a, b, c, d, 1f)), 1e-4f);
    }

    [Test]
    public void Curve_DescendsFromEyeLevelToFloor()
    {
        Path(new Vector3(0f, 1.3f, 3f), out var p0, out var c1, out var c2, out var p3);
        Assert.Greater(p0.y, 0.5f, "路头应在空中（眼前）");
        Assert.AreEqual(Floor + 0.05f, p3.y, 0.001f, "路尾应贴地");
        // 中段高度介于两者之间（形成下垂弧线，而不是一条空中直线）
        var mid = StardustPath.CubicBezier(p0, c1, c2, p3, 0.5f);
        Assert.Less(mid.y, p0.y);
        Assert.Greater(mid.y, p3.y);
    }
}
