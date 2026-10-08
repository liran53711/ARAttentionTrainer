// Assets/Tests/EditMode/OneEuroFilterTests.cs
// EditMode 单元测试：验证 One Euro Filter 的收敛性与平滑性。
using NUnit.Framework;
using System;
using System.Collections.Generic;

public class OneEuroFilterTests
{
    [Test]
    public void StepInput_ConvergesToTarget()
    {
        var f = new OneEuroFilter();
        f.Filter(0f, 1f / 30f); // 初始化
        float last = 0f;
        for (int i = 0; i < 300; i++) last = f.Filter(10f, 1f / 30f);
        Assert.Less(Math.Abs(last - 10f), 0.1f, "阶跃输入应在有限步内收敛到目标值");
    }

    [Test]
    public void JitteryInput_OutputIsSmootherThanInput()
    {
        var f = new OneEuroFilter();
        var outputs = new List<float>();
        for (int i = 0; i < 200; i++)
        {
            float noisy = 0.5f + (i % 2 == 0 ? 0.02f : -0.02f); // 模拟检测框逐帧抖动（峰峰值0.04）
            outputs.Add(f.Filter(noisy, 1f / 30f));
        }

        float min = float.MaxValue, max = float.MinValue;
        for (int i = 100; i < 200; i++) // 跳过初始暂态
        {
            min = Math.Min(min, outputs[i]);
            max = Math.Max(max, outputs[i]);
        }
        Assert.Less(max - min, 0.01f, "稳态输出峰峰值应远小于输入的 0.04");
    }

    [Test]
    public void Reset_ReinitializesToNewValue()
    {
        var f = new OneEuroFilter();
        for (int i = 0; i < 50; i++) f.Filter(10f, 1f / 30f);
        f.Reset();
        Assert.AreEqual(-5f, f.Filter(-5f, 1f / 30f), 1e-4f, "Reset 后首帧应直接返回新值");
    }
}
