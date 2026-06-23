using DBKeeper.Core.Helpers;

namespace DBKeeper.Tests;

public class SqlIdentifierGuardTests
{
    [Fact]
    public void EnsureSimpleIdentifier_ShouldAllowSimpleNames()
    {
        SqlIdentifierGuard.EnsureSimpleIdentifier("MES_DB_2026", "数据库");
        SqlIdentifierGuard.EnsureSimpleIdentifier("_TraceLog", "表");
    }

    [Fact]
    public void EnsureSimpleIdentifier_ShouldRejectCompoundOrUnsafeNames()
    {
        Assert.Throws<InvalidOperationException>(() => SqlIdentifierGuard.EnsureSimpleIdentifier("dbo.TraceLog", "表"));
        Assert.Throws<InvalidOperationException>(() => SqlIdentifierGuard.EnsureSimpleIdentifier("[TraceLog]", "表"));
        Assert.Throws<InvalidOperationException>(() => SqlIdentifierGuard.EnsureSimpleIdentifier("TraceLog;DELETE", "表"));
        Assert.Throws<InvalidOperationException>(() => SqlIdentifierGuard.EnsureSimpleIdentifier("2026TraceLog", "表"));
    }
}
