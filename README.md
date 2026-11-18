# Kairo

Kairo is a background job scheduler for .NET 10. Its name comes from the Greek *kairos*, "the right moment".

> Status: **early development**. The repository is set up, but no features exist yet.

## Goals
- **Durable jobs:** fire-and-forget, delayed, recurring (cron or fluent API) and continuation jobs. Jobs get retries and a dead-letter state.
- **Reliable in a cluster:**
  - Delivery is at-least-once.
  - Jobs are owned through leases, so a crashed node's jobs are picked up again.
  - One leader schedules recurring jobs, so they are not duplicated.
- **Reflection-free:** a source generator registers jobs and payload serializers, so apps can publish with Native AOT.
- **Pluggable storage:** InMemory, PostgreSQL, SQL Server, Redis and JSON file.
- **Observable:** OpenTelemetry traces and metrics, health checks and an embedded dashboard.

## Repository layout
| Folder | Content |
|---|---|
| `src/` | Library packages (`Kairo.*`), all trim/AOT compatible |
| `tests/` | Unit and conformance tests |
| `samples/` | Sample apps; the Minimal API sample is published with Native AOT in CI |

## Build
Requires the .NET SDK pinned in `global.json`: 10.0.403 or later, the first SDK with the fix for the Brotli advisory GHSA-p3jp-9v83-76w9.

```bash
dotnet build Kairo.slnx
dotnet test Kairo.slnx
dotnet publish samples/Kairo.Samples.MinimalApi -r win-x64   # Native AOT check
```

Native AOT publish needs the platform toolchain: the C++ build tools on Windows, or clang on Linux.

## Code style
Code style is defined in `.editorconfig`, and the build enforces it (`EnforceCodeStyleInBuild` with warnings as errors). Run `dotnet format Kairo.slnx` to apply the fixable rules.

## License
[MIT](LICENSE)
