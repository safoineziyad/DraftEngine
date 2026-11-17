using System.IO;
using DraftEngine.App.Services;

namespace DraftEngine.App.Tests;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void Build_IncludesBuildRuntimeAndErrorType_ButNoUserContent()
    {
        var exception = new InvalidOperationException(
            "Contract 'Secret Client NDA' failed at C:\\Users\\Zbook\\AppData\\DraftEngine\\draftengine.db",
            new IOException("token sk-secret-value"));

        var text = StartupDiagnostics.Build(exception, databaseRestored: true);

        Assert.Contains($"DraftEngine {AppVersion.Display}", text);
        Assert.Contains("Windows ", text);
        Assert.Contains(".NET ", text);
        Assert.Contains(typeof(InvalidOperationException).FullName!, text);
        Assert.Contains(typeof(IOException).FullName!, text);
        Assert.Contains("DatabaseRestored True", text);

        Assert.DoesNotContain("Secret Client NDA", text);
        Assert.DoesNotContain("sk-secret-value", text);
        Assert.DoesNotContain("AppData", text);
        Assert.DoesNotContain("draftengine.db", text);
    }

    [Fact]
    public void Build_RecordsWhenNoBackupWasRestored()
    {
        var text = StartupDiagnostics.Build(new InvalidOperationException("no backup"), false);

        Assert.Contains("DatabaseRestored False", text);
        Assert.DoesNotContain("Cause ", text);
    }
}
