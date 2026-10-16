# CLAUDE.md

## How to work with me
- Read README.md first. It holds the requirements and design.
- Before writing code, give a short plan (max 6 bullets) and STOP for my approval.
- Work one step at a time. After each step, stop and tell me: files changed, why, how to verify.
- If something fails, don't silently fix it in a loop. Tell me what failed and propose a fix.
- Keep code minimal. No extra layers, interfaces or patterns unless I ask.
- Never commit. I commit.

## Stack and conventions
- .NET 9, pinned with global.json. ASP.NET Core Minimal APIs.
- Database: EF Core + SQLite, file at ./data/app.db.
- File storage: ./blobs/, path read from appsettings.json ("BlobRoot"). Never absolute paths.
- Async: all I/O is async. Every async method takes a CancellationToken as its last
  parameter and passes it all the way down. Never use .Result, .Wait() or async void
  (except event handlers).
- Endpoints take a CancellationToken parameter.
- Background work: BackgroundService. In-process queues: bounded Channel<T>.
- Logging: ILogger with structured fields. Include the main entity ID in every log line.
- Errors: validation failures return 400 with ProblemDetails.
- Every new endpoint gets a request in the project's .http file.
- Tests: xUnit, one behaviour per test, named Method_Scenario_Expected.
- If code has a concurrency risk (check-then-act, shared state, duplicate requests),
  write a test that forces the race (e.g. a gate that releases all callers at once),
  and confirm in the output or logs that the race path was actually hit.
  Tell me which concurrency risks you saw and which ones you tested.
- Comments: only explain WHY, at non-obvious decisions (ordering of operations,
  invariants, concurrency handling, deliberate trade-offs). No comments that restate the code.

## Invariants (filled in after the design phase)
-