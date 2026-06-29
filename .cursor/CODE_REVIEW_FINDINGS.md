# Code Review Findings — Clockodo MCP

Baseline: `main` @ fc5c49b — 10/11 tests pass locally (stdio test needs runtime on PATH).

Post-fix baseline: 16/16 tests pass with `scripts/run-dev-mcp-server`.

## Implementation queue

| ID | Severity | Title | Status |
|----|----------|-------|--------|
| F1 | CRITICAL | Block `createRegister` via operation blocklist | done |
| F2 | CRITICAL | Validate `CLOCKODO_BASE_URL` (HTTPS + clockodo.com) | done |
| F3 | WARNING | Configure HttpClient timeout + pooled connection lifetime | done |
| F4 | WARNING | Wrap transport failures as McpException | done |
| F5 | WARNING | Read-only errors as McpException at source | already on main; added business-tool test coverage |
| F6 | WARNING | Cache `Active` catalog | done |
| F7 | WARNING | Graceful date-conversion errors (not full DST remap) | done |
| F9 | WARNING | Reject empty path parameter values (clearer message) | done |
| F10 | WARNING | Culture-invariant numeric query encoding | done (int/long covered by decimal; double fallback kept) |
| F11 | WARNING | Materialize ListOperations once | done |
| F12 | NOTE | Validate RequiresBody on write | done |
| F13 | NOTE | Document retryAfter in tool descriptions | done |
| F16 | NOTE | README safety section | done |

## Self-review follow-ups (second pass)

| ID | Issue | Resolution |
|----|-------|-----------|
| H1 | F4 caught `TaskCanceledException`, swallowing genuine host cancellation | Re-throw `OperationCanceledException` when the host token is cancelled; HttpClient timeouts still become tool errors. Tested both paths. |
| M1 | Blocklist narrow; README oversold "high-risk ops blocklisted" | README now states the blocklist is narrow and `CLOCKODO_READ_ONLY` is the real write guardrail. `blockedHidden` now counts ops actually present in the catalog. |
| M3 | F9/F12 tests only asserted exception type | Tests now assert error messages. |
| L1 | F10 int/long branches unreachable (decimal already matches) | Removed dead branches; kept double fallback for out-of-decimal-range magnitudes. |
| L4/L5 | Base-URL: trailing-dot FQDN rejected, IPv6 loopback missing, weak test | Normalize trailing dot + bracketed IPv6; allow `::1` and `127.0.0.0/8`; added subdomain-suffix, userinfo, and trailing-dot test cases. |
| L7 | Dead branch in `DescribeUnavailableOperation` | Removed. |

Deferred (scope): F8 (tiered write allowlist for admin/delete ops). Full `IHttpClientFactory` migration not needed; `SocketsHttpHandler` with `PooledConnectionLifetime` covers connection staleness.

## Verification protocol

After each fix:
1. `dotnet build ClockodoMcp.slnx`
2. `dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj`
3. If fix-specific test added, confirm it passes
4. If worse → `git checkout -- .` and mark reverted
