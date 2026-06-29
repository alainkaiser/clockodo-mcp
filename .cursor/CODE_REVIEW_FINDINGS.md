# Code Review Findings — Clockodo MCP

Baseline: `main` @ fc5c49b — 10/11 tests pass locally (stdio test needs runtime on PATH).

## Implementation queue

| ID | Severity | Title | Status |
|----|----------|-------|--------|
| F1 | CRITICAL | Block `createRegister` via operation blocklist | pending |
| F2 | CRITICAL | Validate `CLOCKODO_BASE_URL` (HTTPS + clockodo.com) | pending |
| F3 | WARNING | Configure HttpClient timeout | pending |
| F4 | WARNING | Wrap transport failures as McpException | pending |
| F5 | WARNING | Read-only errors as McpException at source | pending |
| F6 | WARNING | Cache `Active` catalog | pending |
| F7 | WARNING | DST-safe date conversion with McpException | pending |
| F9 | WARNING | Reject empty path parameter values | pending |
| F10 | WARNING | Explicit int/long query encoding | pending |
| F11 | WARNING | Materialize ListOperations once | pending |
| F12 | NOTE | Validate RequiresBody on write | pending |
| F13 | NOTE | Document retryAfter in tool descriptions | pending |
| F16 | NOTE | README safety section | pending |

Deferred (scope): F8 (write allowlist/tiers), F15 (connection pooling).

## Verification protocol

After each fix:
1. `dotnet build ClockodoMcp.slnx`
2. `dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj`
3. If fix-specific test added, confirm it passes
4. If worse → `git checkout -- .` and mark reverted
