using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Polymerium.Avalonia;

internal static class ErrorReporter
{
    public static void Report(object core, ErrorReportMeta meta)
    {
        if (core is Exception ex)
        {
            GitHubIssueReporter.Report(ex, meta);
        }

        Dump(core);
    }

    private static void Dump(object core)
    {
        // 仅调试模式转储错误报告——Prod 目录大概率只读。
        if (!Program.IsDebug)
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "dumps", $"Exception-{DateTimeOffset.Now.ToFileTime()}.log");
        var sb = new StringBuilder($"""
                                    // {DateTimeOffset.Now.ToString()}
                                    // QinmoLauncher: {typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                                                                  ?.InformationalVersion.Split('+')[0] ?? Program.Version}
                                    // Avalonia: {Assembly.GetEntryAssembly()?.GetName().Version}

                                    """);
        sb.AppendLine();
        DumpCore(sb, core, 0);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, sb.ToString());
    }

    internal static void DumpCore(StringBuilder builder, object core, int level)
    {
        switch (core)
        {
            case AggregateException ae:
                builder.AppendLine($"""
                                    --- LEVEL: {level} ---
                                    Exception: {ae.GetType().Name}
                                    Message: {ae.Message}
                                    StackTrace: {ae.StackTrace}

                                    """);
                foreach (var inner in ae.InnerExceptions)
                {
                    DumpCore(builder, inner, level + 1);
                }

                if (ae.InnerException is not null)
                {
                    DumpCore(builder, ae.InnerException, level + 1);
                }

                break;
            case Exception e:
                builder.AppendLine($"""
                                    --- LEVEL: {level} ---
                                    Exception: {e.GetType().Name}
                                    Message: {e.Message}
                                    StackTrace: {e.StackTrace}

                                    """);
                if (e.InnerException is not null)
                {
                    DumpCore(builder, e.InnerException, level + 1);
                }

                break;
            default:
                builder.AppendLine($"""
                                    --- LEVEL: {level} ---
                                    Content: {core.ToString()}

                                    """);
                break;
        }
    }

    internal enum ErrorReportSource
    {
        AppDomainUnhandled, DispatcherUnhandled, TaskUnobserved, NetworkUnobserved, LifetimeStartup, LifetimeShutdown
    }

    internal enum ErrorReportLevel
    {
        Warning, Error, Fatal
    }

    internal readonly record struct ErrorReportMeta(
        ErrorReportSource Source,
        string Phase,
        bool Critical,
        bool Terminating,
        ErrorReportLevel Level);
}
