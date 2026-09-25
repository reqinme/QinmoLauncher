using System;
using System.Globalization;

namespace Polymerium.Avalonia.Utilities;

public static class DownloadPolicyEnvironmentHelper
{
    public const string MirrorModeVariable = "TRIDENT_DOWNLOAD_MIRROR";
    public const string ParallelismVariable = "TRIDENT_DOWNLOAD_PARALLELISM";
    public const string AttemptTimeoutVariable = "TRIDENT_DOWNLOAD_ATTEMPT_TIMEOUT";

    private const string BmclapiMode = "bmclapi";

    // WARNING: 下载层只读环境变量，这里是设置页与下载层之间唯一的桥接点。
    //  值回到默认时必须清除变量，否则用户关不掉镜像、也回不到自动并发。
    public static void Apply(bool mirrorEnabled, uint parallelism, uint attemptTimeoutSeconds)
    {
        Assign(MirrorModeVariable, mirrorEnabled ? BmclapiMode : null);
        Assign(ParallelismVariable, parallelism > 0 ? parallelism.ToString(CultureInfo.InvariantCulture) : null);
        Assign(AttemptTimeoutVariable,
               attemptTimeoutSeconds > 0 ? attemptTimeoutSeconds.ToString(CultureInfo.InvariantCulture) : null);
    }

    private static void Assign(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
}