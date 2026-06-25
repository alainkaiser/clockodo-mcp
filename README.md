# clockodo-mcp

A small stdio MCP server for the Clockodo REST API, built with the official .NET MCP SDK.

The bundled operation catalog is generated from `https://docs.clockodo.com/openapi.yaml` and currently targets Clockodo OpenAPI version `2026-06-24`. Operations marked `deprecated` in the OpenAPI spec are hidden and cannot be called.

## Tools

- `clockodo_server_info` - inspect catalog version, operation coverage, runtime safety settings, and native MCP comparison notes.
- `clockodo_current_time` - get current UTC/local time for reliable relative date calculations.
- `clockodo_me` - get the current authenticated Clockodo user.
- `clockodo_get_my_absences` - read absences for the authenticated user.
- `clockodo_get_entries_by_timeframe` - read entries for common relative timeframes or a custom date range.
- `clockodo_list_operations` - search current non-deprecated operations.
- `clockodo_get_operation` - inspect one operation, including query/path parameters and top-level JSON body fields.
- `clockodo_read` - call current non-deprecated `GET` operations.
- `clockodo_write` - call current non-deprecated `POST`, `PUT`, and `DELETE` operations.

## Compared With Clockodo's Native MCP

Clockodo documents a native HTTP MCP endpoint at `https://mcp.clockodo.com/mcp` using the MCP-specific headers `X-Clockodo-User` and `X-Clockodo-Key`.

This project is intentionally different:

- local stdio transport, so API credentials can stay in an ignored `.env` file via `scripts/run-clockodo-mcp`;
- full generated wrapper over the current, non-deprecated OpenAPI operations instead of only the selected native MCP tools;
- explicit `clockodo_read` and `clockodo_write` split plus optional `CLOCKODO_READ_ONLY=true`;
- `clockodo_get_operation` and `clockodo_server_info` for agent-side discovery before touching data;
- small read-only convenience tools for current user, own absences, and timeframe-based entry reads.

## Install

Prerequisite: .NET 10 SDK.

```bash
git clone git@github.com:alainkaiser/clockodo-mcp.git
cd clockodo-mcp
dotnet publish src/Clockodo.Mcp/Clockodo.Mcp.csproj -c Release -o dist
cp .env.example .env
```

Edit `.env`:

```bash
CLOCKODO_API_USER="you@example.com"
CLOCKODO_API_KEY="your-api-key"
CLOCKODO_EXTERNAL_APPLICATION="clockodo-mcp;you@example.com"
```

`.env` is ignored by git. Optional settings:

- `CLOCKODO_ACCEPT_LANGUAGE`: `en`, `de`, or `fr`; defaults to `en`.
- `CLOCKODO_READ_ONLY=true`: blocks write operations.

## Agent Config

Use the wrapper script so credentials stay in `.env`.

Codex:

```toml
[mcp_servers.clockodo]
command = "/Users/alainkaiser/Documents/dev/personal/clockodo-mcp/scripts/run-clockodo-mcp"
args = []
```

Claude Desktop, Cursor, Windsurf, and other stdio MCP clients:

```json
{
  "mcpServers": {
    "clockodo": {
      "command": "/Users/alainkaiser/Documents/dev/personal/clockodo-mcp/scripts/run-clockodo-mcp",
      "args": []
    }
  }
}
```

## Usage

Call `clockodo_server_info` without arguments to inspect server coverage and runtime safety state:

```json
{}
```

Find an operation:

```json
{
  "search": "users me"
}
```

Inspect a write operation before composing `bodyJson`:

```json
{
  "operationId": "createServiceV4"
}
```

Call a read operation:

```json
{
  "operationId": "getUsersMeV4"
}
```

Read entries for an inclusive custom date range:

```json
{
  "timeframe": "custom",
  "timeZone": "Europe/Zurich",
  "dateSince": "2026-06-01",
  "dateUntil": "2026-06-30"
}
```

Supported `timeframe` values: `today`, `yesterday`, `this_week`, `last_week`, `this_month`, `last_month`, `custom`.

Call a write operation:

```json
{
  "operationId": "createServiceV4",
  "bodyJson": "{\"name\":\"Consulting\"}"
}
```

## Refresh API Catalog

```bash
scripts/update-clockodo-catalog.rb
dotnet run --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj
```

## Verify

```bash
dotnet build ClockodoMcp.slnx
dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj
```

Test the published executable path used by agents:

```bash
CLOCKODO_MCP_COMMAND="/Users/alainkaiser/Documents/dev/personal/clockodo-mcp/scripts/run-clockodo-mcp" \
dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj
```

Run the opt-in live check:

```bash
set -a
. ./.env
set +a
CLOCKODO_LIVE_TEST=1 dotnet run --no-build --project tests/Clockodo.Mcp.Tests/Clockodo.Mcp.Tests.csproj
```

The live check calls `getUsersMeV4` only.
