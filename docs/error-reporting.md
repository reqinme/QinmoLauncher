# Runtime Error Reporting

Unhandled exceptions are filed as GitHub issues by `src/Polymerium.Avalonia/GitHubIssueReporter.cs`.
No third-party error-monitoring SDK is referenced by the build.

## How it is wired

`ErrorReporter.Report` is the single funnel. The process-level hooks in `App.axaml.cs`
(`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, `Dispatcher.UIThread.UnhandledException`)
and the shutdown path in `Program.cs` all report through it. `GitHubIssueReporter` is the transport;
`ErrorReporter` additionally writes a local dump when `Program.IsDebug`.

## Environment variables

| Variable | Required | Meaning |
| --- | --- | --- |
| `QINMOLAUNCHER_ERROR_REPORT_TOKEN` | yes | GitHub token, sent as `Authorization: Bearer`. |
| `QINMOLAUNCHER_ERROR_REPORT_REPO` | yes | Target repository as `owner/name`. |
| `QINMOLAUNCHER_ERROR_REPORT_DRY_RUN` | no | `1` / `true`: compose the payload and write it to disk without calling the API. |
| `QINMOLAUNCHER_ERROR_REPORT_SELFTEST` | no | `1` / `true`: run the self-check, print one line, and exit. |

Reporting is **disabled** unless both the token and the repository are set. No credential is embedded in the
binary, so a normal user build reports nothing.

## Token scope

Fine-grained token: **Issues: Read and write** on the target repository. Classic token: `public_repo` for a
public repository, `repo` for a private one. Nothing else is needed — the reporter calls only
`POST /repos/{owner}/{repo}/issues` and `GET /search/issues`.

> WARNING: the token lives in the environment of whatever process runs the launcher. Never embed it in a
> distributed build — anyone can read it out of the process and file issues in your repository. For a public
> release, either leave reporting disabled for end users or point the reporter at a small proxy that holds
> the token server-side.

## Trigger conditions

A report is filed only when all of the following hold:

1. `QINMOLAUNCHER_ERROR_REPORT_TOKEN` and `QINMOLAUNCHER_ERROR_REPORT_REPO` are set.
2. `_no_telemetry_` does not exist in the private config directory — the user opt-out wins over configuration.
3. The fingerprint has not been reported earlier in this process.
4. Fewer than 5 issues have been created in this process, and at least 60 seconds have passed since the last one.

## Deduplication

The fingerprint is the first 12 hex characters of `SHA256(exception type + first 3 stack frames)`, with the
user profile path replaced by `%USERPROFILE%`. It is embedded in the issue title as `[FP:<fingerprint>]`.

- Before creating, the reporter calls
  `GET /search/issues?q=repo:<owner>/<name> is:issue is:open in:title "FP:<fingerprint>"`
  and skips creation when an open issue already carries that fingerprint, so a crash that survives a restart
  does not open a second issue.
- If that search fails (offline, throttled), creation proceeds; the per-process limits above still bound it.

## Privacy

- The user profile path is replaced with `%USERPROFILE%` in the message, the stack trace, and the fingerprint.
- Machine name, account credentials, and game content are never included.
- Issues are created in a repository that is usually **public**. Reports are visible to anyone who can read it.

## Failure fallback

Reporting never throws and never blocks startup.

| Failure | Behaviour |
| --- | --- |
| Token or repository missing | Silent no-op. |
| `_no_telemetry_` present | Silent no-op. |
| Config path unavailable (brand names not configured yet) | Treated as opted out; nothing is sent. |
| API returns 4xx / 5xx | Payload written to `<private config>/error-reports/<timestamp>-failed-<code>.json`. |
| Network error or timeout | Payload written to `<private config>/error-reports/<timestamp>-failed-network.json`. |
| Process is terminating | Waits at most 5 seconds for the request, then exits regardless. |
| Local persistence fails | Ignored. |

`GET /search/issues` allows 30 requests per minute for an authenticated caller; a throttled search is treated
as "no duplicate" and the create call is attempted.

## Verifying a deployment

```powershell
$env:QINMOLAUNCHER_ERROR_REPORT_TOKEN    = "<token>"
$env:QINMOLAUNCHER_ERROR_REPORT_REPO     = "owner/name"
$env:QINMOLAUNCHER_ERROR_REPORT_SELFTEST = "1"
.\QinmoLauncher.exe
```

It prints exactly one line and exits:

| Output | Meaning |
| --- | --- |
| `created` | An issue was created. Chain verified end to end. |
| `duplicate-skipped` | An open issue already carries this fingerprint. |
| `failed <code>` | The API rejected the call; payload was written to disk. |
| `failed (<ExceptionType>)` | Network or transport failure; payload was written to disk. |
| `disabled (set ...)` | Token or repository is missing. |
| `dry-run (payload written to error-reports)` | Validated locally without calling the API. |

Add `QINMOLAUNCHER_ERROR_REPORT_DRY_RUN=1` to inspect the exact JSON payload without touching the API.
