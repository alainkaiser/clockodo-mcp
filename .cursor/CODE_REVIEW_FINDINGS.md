# Code Review Findings — Clockodo MCP

Baseline: `main` @ fc5c49b — 10/11 tests pass locally (stdio test needs runtime on PATH).

Post-fix baseline: 16/16 tests pass with `scripts/run-dev-mcp-server`.

## Implementation queue

| ID | Severity | Title | Status |
|----|----------|-------|--------|
| F1 | CRITICAL | Block `createRegister` via operation blocklist | done |
| F2 | CRITICAL | Validate `CLOCKODO_BASE_URL` (HTTPS + clockodo.com) | done |
| F3 | WARNING | Configure HttpClient timeout | done |
| F4 | WARNING | Wrap transport failures as McpException | done |
| F5 | WARNING | Read-only errors as McpException at source | done |
| F6 | WARNING | Cache `Active` catalog | done |
| F7 | WARNING | DST-safe date conversion with McpException | done |
| F9 | WARNING | Reject empty path parameter values | done |
| F10 | WARNING | Explicit int/long query encoding | done |
| F11 | WARNING | Materialize ListOperations once | done |
| F12 | NOTE | Validate RequiresBody on write | done |
| F13 | NOTE | Document retryAfter in tool descriptions | done |
| F16 | NOTE | README safety section | done |

Deferred (scope): F8 (write allowlist/tiers), F15 (connection pooling).

## Verification protocol

After each fix:
1. `dotnet build ClockodoMcp.slnx`
2. `dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj`
3. If fix-specific test added, confirm it passes
4. If worse → `git checkout -- .` and mark reverted
