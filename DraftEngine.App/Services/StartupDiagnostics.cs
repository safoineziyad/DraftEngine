using System;
using System.Runtime.InteropServices;

namespace DraftEngine.App.Services;

/// <summary>
/// Builds the content-free diagnostics shown when startup fails. It intentionally excludes
/// exception messages, file paths, contract text, account data and API keys so the text can
/// be pasted into a support request without leaking user content.
/// </summary>
public static class StartupDiagnostics
{
    /// <summary>Describes the build, OS, runtime and exception types only.</summary>
    public static string Build(Exception exception, bool databaseRestored)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var text =
            $"DraftEngine {AppVersion.Display}{Environment.NewLine}" +
            $"Windows {Environment.OSVersion.VersionString}{Environment.NewLine}" +
            $".NET {RuntimeInformation.FrameworkDescription}{Environment.NewLine}" +
            $"Error {exception.GetType().FullName}{Environment.NewLine}" +
            $"DatabaseRestored {databaseRestored}";

        if (exception.InnerException is { } inner)
        {
            text += Environment.NewLine + $"Cause {inner.GetType().FullName}";
        }

        return text;
    }
}
