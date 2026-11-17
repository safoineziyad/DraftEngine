using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DraftEngine.App.Tests;

/// <summary>Guards that WPF startup no longer blocks the dispatcher on initialization work.</summary>
public sealed class StartupAsyncTests
{
    [Fact]
    public void OnStartup_IsAnAsyncMethod()
    {
        var method = typeof(App).GetMethod(
            "OnStartup",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [Fact]
    public void StartupCode_HasNoBlockingTaskWaits()
    {
        var source = File.ReadAllText(FindSourceFile("App.xaml.cs"));

        Assert.DoesNotContain(".GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", source, StringComparison.Ordinal);
    }

    private static string FindSourceFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "DraftEngine.App", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
