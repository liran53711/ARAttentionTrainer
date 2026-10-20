// Assets/Tests/EditMode/FirstSearchLogicTests.cs
// 首寻 3D 漫游纯逻辑单测（2026-10-20）：360° 覆盖 / 房间夹紧 / 语音节拍 / 停止。
using NUnit.Framework;
using UnityEngine;

public class FirstSearchLogicTests
{
    static Bounds BigRoom() => new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(10f, 3f, 10f));

    static FirstSearchLogic Started(Vector3 forward = default, Bounds? room = null)
    {
        var l = new FirstSearchLogic(moveSpeed: 1.4f, radius: 1.6f, holdSeconds: 0.4f, voiceInterval: 4f);
        Vector3 f = forward.sqrMagnitude < 1e-4f ? Vector3.forward : forward.normalized;
        l.Begin(new Vector3(0f, 1.6f, 0f), f, 0f, room ?? BigRoom());
        return l;
    }

    [Test]
    public void Begin_ActivatesAndStartlesVoiceWithinQuarterSecond()
    {
        var l = Started();
        Assert.IsTrue(l.Active);
        var r = l.Tick(0.2f);
        Assert.IsTrue(r.active);
        Assert.IsTrue(r.voiceDue, "搜索开始后 0.2s 内应说第一句");
        Assert.AreEqual(0, r.voiceSlot, "第一句应是开场句");
        Assert.AreEqual(1, l.VoiceCount);
    }

    [Test]
    public void Waypoints_CoverFullCircle()
    {
        var l = Started();
        var wps = l.PreviewWaypoints();
        Assert.GreaterOrEqual(wps.Length, 6, "至少 6 个路径点才能覆盖四周");
        bool behind = false, left = false, right = false;
        foreach (var wp in wps)
        {
            float az = Mathf.Atan2(wp.x, wp.z) * Mathf.Rad2Deg; // 以 +Z 为前
            if (Mathf.Abs(az) > 135f) behind = true;
            if (az < -35f && az > -145f) left = true;
            if (az > 35f && az < 145f) right = true;
        }
        Assert.IsTrue(behind, "必须有老师可能在的正后方路径点");
        Assert.IsTrue(left && right, "左右两侧都要覆盖");
    }

    [Test]
    public void Waypoints_StayInsideRoomBounds()
    {
        var room = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 6f));
        var l = Started(room: room);
        var wps = l.PreviewWaypoints();
        Assert.GreaterOrEqual(wps.Length, 6, "必须先有路径点才谈夹紧");
        foreach (var wp in wps)
        {
            Assert.GreaterOrEqual(wp.x, room.min.x + 0.3f);
            Assert.LessOrEqual(wp.x, room.max.x - 0.3f);
            Assert.GreaterOrEqual(wp.z, room.min.z + 0.3f);
            Assert.LessOrEqual(wp.z, room.max.z - 0.3f);
            Assert.GreaterOrEqual(wp.y, 0.75f);
            Assert.LessOrEqual(wp.y, room.max.y - 0.25f);
        }
    }

    [Test]
    public void SmallRoom_ClampsWaypointsInside()
    {
        var room = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(1.4f, 2.6f, 1.4f));
        var l = Started(room: room);
        var wps = l.PreviewWaypoints();
        Assert.GreaterOrEqual(wps.Length, 6, "必须先有路径点才谈夹紧");
        foreach (var wp in wps)
        {
            Assert.GreaterOrEqual(wp.x, room.min.x + 0.3f - 1e-3f);
            Assert.LessOrEqual(wp.x, room.max.x - 0.3f + 1e-3f);
            Assert.GreaterOrEqual(wp.z, room.min.z + 0.3f - 1e-3f);
            Assert.LessOrEqual(wp.z, room.max.z - 0.3f + 1e-3f);
        }
    }

    [Test]
    public void Tick_VisitsMultipleWaypoints()
    {
        var l = Started();
        var seen = new System.Collections.Generic.HashSet<Vector3>();
        for (int i = 0; i < 30 * 20; i++)
        {
            var r = l.Tick(1f / 30f);
            seen.Add(r.target);
        }
        Assert.Greater(l.WaypointsVisited, 3, "20 秒内应走过多个路径点");
        Assert.GreaterOrEqual(seen.Count, 4, "精灵应实际移动而不是停在原地");
    }

    [Test]
    public void Voice_RepeatsAtInterval_WithoutImmediateRepeat()
    {
        var l = Started();
        int lastSlot = -1, due = 0;
        for (int i = 0; i < 30 * 13; i++)
        {
            var r = l.Tick(1f / 30f);
            if (!r.voiceDue) continue;
            due++;
            Assert.AreNotEqual(lastSlot, r.voiceSlot, "连续两句不能是同一句");
            lastSlot = r.voiceSlot;
        }
        Assert.GreaterOrEqual(due, 3, "13 秒至少 3 句（4s 间隔）");
        Assert.LessOrEqual(due, 4, "13 秒不应超过 4 句（避免话痨）");
    }

    [Test]
    public void Stop_EndsMovementAndVoice()
    {
        var l = Started();
        Assert.IsTrue(l.Active, "Begin 后应处于搜索状态");
        Assert.IsTrue(l.Tick(0.2f).active, "停止前应仍在搜索");
        l.Stop();
        Assert.IsFalse(l.Active);
        var r = l.Tick(5f);
        Assert.IsFalse(r.active);
        Assert.IsFalse(r.voiceDue);
    }

    [Test]
    public void Forward_OrientationRotatesPattern()
    {
        var l = Started(new Vector3(1f, 0f, 0f)); // 孩子朝 +X
        var wps = l.PreviewWaypoints();
        float maxX = float.MinValue;
        foreach (var wp in wps) maxX = Mathf.Max(maxX, wp.x);
        Assert.Greater(maxX, 0.8f, "路径应随孩子初始朝向旋转（前方 +X 有路径点）");
    }
}
