// Assets/Tests/EditMode/RoomClampTests.cs
// 空间围栏（IPWorldView.ClampToRoom）纯函数单测 —— 2026-10-11
// 房间：x∈[-2,2], y∈[0,2.6], z∈[-1.5,1.5]（世界坐标，含地板/天花板高度）
using NUnit.Framework;
using UnityEngine;

public class RoomClampTests
{
    static Bounds Room() => new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(4f, 2.6f, 3f));

    [Test]
    public void Inside_Unchanged()
    {
        var p = new Vector3(1f, 1.5f, 0.5f);
        Assert.AreEqual(p, IPWorldView.ClampToRoom(p, Room(), 0.35f, 0.45f, 0.35f));
    }

    [Test]
    public void ThroughWall_PulledBackToMargin()
    {
        var r = IPWorldView.ClampToRoom(new Vector3(2.5f, 1.5f, -2f), Room(), 0.35f, 0.45f, 0.35f);
        Assert.AreEqual(2f - 0.35f, r.x, 1e-4f);
        Assert.AreEqual(-1.5f + 0.35f, r.z, 1e-4f);
        Assert.AreEqual(1.5f, r.y, 1e-4f);
    }

    [Test]
    public void BelowFloor_Lifted()
    {
        var r = IPWorldView.ClampToRoom(new Vector3(0f, -0.5f, 0f), Room(), 0.35f, 0.45f, 0.35f);
        Assert.AreEqual(0.45f, r.y, 1e-4f);
    }

    [Test]
    public void AboveCeiling_PushedDown()
    {
        var r = IPWorldView.ClampToRoom(new Vector3(0f, 3.9f, 0f), Room(), 0.35f, 0.45f, 0.35f);
        Assert.AreEqual(2.6f - 0.35f, r.y, 1e-4f);
    }

    [Test]
    public void DegenerateRoom_NoClamp()
    {
        // 无地板/天花板锚点时 MRUK 可能给全零 Bounds——必须原样返回（否则一切被吸到原点）
        var p = new Vector3(9f, -9f, 9f);
        Assert.AreEqual(p, IPWorldView.ClampToRoom(p, new Bounds(Vector3.zero, Vector3.zero), 0.35f, 0.45f, 0.35f));
    }

    [Test]
    public void ThinRoom_XNotClamped_ZClamped()
    {
        // x 方向比两边距之和还窄 → x 不约束（防 min/max 反转）；z 正常约束
        var room = new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(0.5f, 2.6f, 3f));
        var r = IPWorldView.ClampToRoom(new Vector3(5f, 1.5f, -5f), room, 0.35f, 0.45f, 0.35f);
        Assert.AreEqual(5f, r.x, 1e-4f);
        Assert.AreEqual(-1.5f + 0.35f, r.z, 1e-4f);
    }
}
