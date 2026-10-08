// Assets/Tests/EditMode/SessionLoggerTests.cs
// EditMode 单元测试：日志格式化函数（CSV 行、文件夹命名）。
using NUnit.Framework;
using System;

public class SessionLoggerTests
{
    [Test]
    public void FormatSampleCsv_ProducesExpectedColumns()
    {
        var line = SessionLogger.FormatSampleCsv(12.34f, true, false, true, 59.94f, 48.24f);
        Assert.AreEqual("12.34,1,0,1,59.9,48.2", line);
    }

    [Test]
    public void FormatSampleCsv_UsesInvariantDecimalSeparator()
    {
        // 某些系统区域设置用逗号做小数点——这里必须永远是"点"，否则 CSV 列会错位
        var line = SessionLogger.FormatSampleCsv(1.5f, false, false, false, 30f, 10f);
        Assert.AreEqual("1.50,0,0,0,30.0,10.0", line);
    }

    [Test]
    public void BuildSessionFolderName_MatchesPattern()
    {
        var name = SessionLogger.BuildSessionFolderName(new DateTime(2026, 10, 8, 9, 5, 7));
        Assert.AreEqual("20261008_090507", name);
    }
}
