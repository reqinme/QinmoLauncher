using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TridentCore.Abstractions;

namespace Polymerium.Avalonia;

// 运行时异常上报到 GitHub Issue。
// 凭据只从环境变量读取，二进制内不内嵌 token：未配置时整条链路静默禁用。
internal static class GitHubIssueReporter
{
    private const string TOKEN_VARIABLE = "QINMOLAUNCHER_ERROR_REPORT_TOKEN";
    private const string REPO_VARIABLE = "QINMOLAUNCHER_ERROR_REPORT_REPO";
    private const string DRY_RUN_VARIABLE = "QINMOLAUNCHER_ERROR_REPORT_DRY_RUN";
    private const string SELFTEST_VARIABLE = "QINMOLAUNCHER_ERROR_REPORT_SELFTEST";

    private const string FALLBACK_DIRECTORY = "error-reports";
    private const string API_ROOT = "https://api.github.com";

    // GitHub 的 issue body 上限是 65536 字符，留出余量给正文模板。
    private const int BODY_LIMIT = 60000;
    // GitHub 的 issue title 上限是 256 字符。
    private const int TITLE_LIMIT = 250;
    // 单进程最多创建 5 个 issue，且两次创建间隔不小于 60 秒——崩溃循环不得刷屏。
    private const int MAX_ISSUES = 5;
    private const int TERMINATING_WAIT_MS = 5000;
    private const int TIMEOUT_SECONDS = 15;

    private static readonly TimeSpan MIN_INTERVAL = TimeSpan.FromSeconds(60);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS) };
    private static readonly ConcurrentDictionary<string, int> Occurrences = new();
    private static readonly object Gate = new();
    private static readonly string? Token = Environment.GetEnvironmentVariable(TOKEN_VARIABLE)?.Trim();
    private static readonly string? Repository = Environment.GetEnvironmentVariable(REPO_VARIABLE)?.Trim();
    private static readonly bool DryRun = Truthy(Environment.GetEnvironmentVariable(DRY_RUN_VARIABLE));
    private static int _created;
    private static DateTimeOffset _lastCreatedAt = DateTimeOffset.MinValue;

    public static bool Configured => !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(Repository);

    // NOTE: 隐私开关必须先于一切上报判断，且读取失败时按"已关闭"处理——
    //  用户没有明确同意就不能上报，路径未就绪（BrandNames 尚未配置）也不能当默许。
    private static bool TelemetrySuppressed()
    {
        try
        {
            return File.Exists(PathDef.Default.FileOfTelemetrySwitch());
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static bool IsEnabled => Configured && !TelemetrySuppressed();

    public static void Report(Exception exception, ErrorReporter.ErrorReportMeta meta)
    {
        try
        {
            if (!IsEnabled)
            {
                return;
            }

            var fingerprint = Fingerprint(exception);
            Occurrences.AddOrUpdate(fingerprint, 1, static (_, count) => count + 1);
            if (!Reserve(fingerprint))
            {
                return;
            }

            var payload = Payload(exception, meta, fingerprint);
            if (DryRun)
            {
                Persist(payload, "dry-run");
                return;
            }

            // WARNING: 进程正在终止时必须同步等待，否则请求还没发出去进程就没了；
            //  但等待有上限，上报绝不允许拖住崩溃退出。
            if (meta.Terminating)
            {
                Task.WaitAny(new[] { DeliverAsync(fingerprint, payload) }, TERMINATING_WAIT_MS);
            }
            else
            {
                _ = Task.Run(() => DeliverAsync(fingerprint, payload));
            }
        }
        catch (Exception)
        {
            // 上报自身绝不抛出——它运行在崩溃路径上，抛出去会掩盖原始异常。
        }
    }

    // 自检：用合成异常走完整链路并返回一行结果，供部署脚本确认配置真的可用。
    public static string SelfCheck()
    {
        try
        {
            if (!Configured)
            {
                return $"disabled (set {TOKEN_VARIABLE} and {REPO_VARIABLE})";
            }

            if (TelemetrySuppressed())
            {
                return "suppressed by telemetry switch";
            }

            // 真抛真接，才能拿到真实堆栈；消息里刻意带一条用户目录路径，用来直接验证脱敏。
            Exception exception;
            try
            {
                throw new InvalidOperationException(
                    "QinmoLauncher error-reporting self-check. Redaction probe: "
                    + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
                    new TimeoutException("synthetic inner exception"));
            }
            catch (Exception caught)
            {
                exception = caught;
            }
            var meta = new ErrorReporter.ErrorReportMeta(ErrorReporter.ErrorReportSource.LifetimeStartup,
                                                         "selfcheck",
                                                         false,
                                                         false,
                                                         ErrorReporter.ErrorReportLevel.Warning);
            var fingerprint = Fingerprint(exception);
            var payload = Payload(exception, meta, fingerprint);
            if (DryRun)
            {
                Persist(payload, "dry-run");
                return $"dry-run (payload written to {FALLBACK_DIRECTORY})";
            }

            return DeliverAsync(fingerprint, payload).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            return $"failed ({exception.GetType().Name}: {exception.Message})";
        }
    }

    public static bool SelfTestRequested => Truthy(Environment.GetEnvironmentVariable(SELFTEST_VARIABLE));

    private static bool Reserve(string fingerprint)
    {
        lock (Gate)
        {
            // NOTE: 同一指纹每次运行只尝试一次。代价是首次投递失败（断网）就放弃这一份，
            //  换来的是崩溃循环下绝不可能重复投递；要改成失败重试，需同时放宽 MAX_ISSUES。
            if (Occurrences[fingerprint] > 1)
            {
                return false;
            }

            if (_created >= MAX_ISSUES)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastCreatedAt < MIN_INTERVAL)
            {
                return false;
            }

            _created++;
            _lastCreatedAt = now;
            return true;
        }
    }

    private static async Task<string> DeliverAsync(string fingerprint, string payload)
    {
        var repository = Repository;
        if (string.IsNullOrEmpty(repository))
        {
            return "disabled";
        }

        try
        {
            if (await ExistsAsync(repository, fingerprint).ConfigureAwait(false))
            {
                return "duplicate-skipped";
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{API_ROOT}/repos/{repository}/issues")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            Authorize(request);
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Persist(payload, $"failed-{(int)response.StatusCode}");
                return $"failed {(int)response.StatusCode}";
            }

            return "created";
        }
        catch (Exception exception)
        {
            Persist(payload, "failed-network");
            return $"failed ({exception.GetType().Name})";
        }
    }

    private static async Task<bool> ExistsAsync(string repository, string fingerprint)
    {
        try
        {
            var query = Uri.EscapeDataString($"repo:{repository} is:issue is:open in:title \"FP:{fingerprint}\"");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                                                       $"{API_ROOT}/search/issues?q={query}&per_page=1");
            Authorize(request);
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 查重失败按"不存在"处理：投递本身还有去重与频率上限兜着。
                return false;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (JsonNode.Parse(body)?["total_count"]?.GetValue<int>() ?? 0) > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Authorize(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        // GitHub 拒绝没有 User-Agent 的请求。
        request.Headers.TryAddWithoutValidation("User-Agent", $"{Program.Brand}/{Program.Version}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Token}");
    }

    private static string Payload(Exception exception, ErrorReporter.ErrorReportMeta meta, string fingerprint)
    {
        var origin = Origin(exception);
        var title = $"[auto] {exception.GetType().Name} in {origin} ({meta.Source}/{meta.Phase}) [FP:{fingerprint}]";
        if (title.Length > TITLE_LIMIT)
        {
            title = title[..TITLE_LIMIT];
        }

        var payload = new JsonObject
        {
            ["title"] = title, ["body"] = Body(exception, meta, fingerprint, origin)
        };
        return payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Body(Exception exception, ErrorReporter.ErrorReportMeta meta, string fingerprint,
                               string origin)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"<!-- QinmoLauncher 自动上报，指纹 {fingerprint}。同一异常在本进程内只投递一次。 -->");
        builder.AppendLine();
        builder.AppendLine("| 项 | 值 |");
        builder.AppendLine("| --- | --- |");
        Row(builder, "异常类型", $"`{exception.GetType().FullName}`");
        Row(builder, "入口", origin);
        Row(builder, "来源", meta.Source.ToString());
        Row(builder, "阶段", meta.Phase);
        Row(builder, "级别", meta.Level.ToString());
        Row(builder, "Critical", meta.Critical ? "true" : "false");
        Row(builder, "终止进程", meta.Terminating ? "true" : "false");
        Row(builder, "发生时间", $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Row(builder, "应用版本", Program.Version);
        Row(builder, "运行时", RuntimeInformation.FrameworkDescription);
        Row(builder, "操作系统", RuntimeInformation.OSDescription);
        Row(builder, "进程架构", RuntimeInformation.ProcessArchitecture.ToString());
        Row(builder, "界面语言", CultureInfo.CurrentUICulture.Name);
        builder.AppendLine();
        builder.AppendLine("### 消息");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(Redact(exception.Message));
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("### 堆栈");
        builder.AppendLine();
        builder.AppendLine("```text");
        // WARNING: DumpCore 的输出里含带绝对路径的堆栈与内层异常消息，
        //  必须整段脱敏后再拼接；漏掉这里会把用户名写进公开 issue。
        var dump = new StringBuilder();
        ErrorReporter.DumpCore(dump, exception, 0);
        builder.AppendLine(Redact(dump.ToString()).TrimEnd());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine($"_用户目录已替换为 `%USERPROFILE%`；不包含用户名、机器名与账户凭据。_");

        var body = builder.ToString();
        return body.Length > BODY_LIMIT ? body[..BODY_LIMIT] + "\n\n_(正文超长已截断)_" : body;
    }

    private static void Row(StringBuilder builder, string name, string value) =>
        builder.AppendLine($"| {name} | {value} |");

    private static string Fingerprint(Exception exception)
    {
        var builder = new StringBuilder(exception.GetType().FullName);
        if (exception.StackTrace is { } stack)
        {
            var frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var index = 0; index < Math.Min(3, frames.Length); index++)
            {
                builder.Append('\n').Append(Redact(frames[index]));
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string Origin(Exception exception)
    {
        if (exception.TargetSite is { } site)
        {
            return $"{site.DeclaringType?.Name}.{site.Name}";
        }

        var frame = exception.StackTrace?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .FirstOrDefault();
        if (frame is null)
        {
            return "unknown";
        }

        var match = Regex.Match(frame, @"\bat\s+([^\(]+)\(");
        return match.Success ? match.Groups[1].Value.Trim() : frame[..Math.Min(80, frame.Length)];
    }

    // 堆栈里带绝对路径，含用户名；issue 可能是公开的，必须先抹掉。
    private static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? text : text.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    // 投递失败时把原始载荷落盘，避免报告彻底丢失。
    private static void Persist(string payload, string reason)
    {
        try
        {
            var directory = Path.Combine(PathDef.Default.PrivateConfigDirectory(), FALLBACK_DIRECTORY);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{reason}.json");
            File.WriteAllText(path, payload);
        }
        catch (Exception)
        {
            // 兜底本身失败也只能放弃。
        }
    }

    private static bool Truthy(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
