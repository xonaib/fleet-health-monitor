# Fleet Health Monitor (PrimeMonitor)

N worker processes ("machines") generate prime numbers and stream them over TCP to a monitor process.
The monitor tells you **which machine** has a problem and **what kind** of problem: a wrong answer,
work that is too slow or too fast, lost or replayed messages, garbage on the wire, or a machine that has
gone quiet.

## The brief

- Several processes generate logs (a prime number plus a timestamp); one more process watches those streams.
- Each prime should take about **20 s** to generate, with **±20%** allowed deviation.
- An evaluation function checks that every number really is prime.
- Any number of machines; the monitor must be reliable: a mistake is not tolerable.

## Assumptions

| Topic | Decision |
|---|---|
| Healthy band | 20 s ± 20% → **16–24 s**, both edges inclusive. |
| Too slow (> 24 s) | **error**: the machine is not keeping up. |
| Too fast (< 16 s) | **warning**: a fixed workload finishing early can mean skipped work or misreporting. |
| Trusting the worker | The monitor checks the worker's reported duration **and** the arrival interval it measures itself, so a worker can't hide slowness by under-reporting. The interval across a reconnect is not judged (it measures the outage, which is reported separately). |
| Silence | No message for longer than band upper bound + grace (24 s + 1 s) → **unresponsive**, even if the socket is still open. |
| Sequence numbers | Each worker numbers its messages from 1 per run. A gap means lost messages; a repeat means a replay. `seq=1` on a new connection is a worker restart, not a replay. The first message after a monitor restart sets the baseline. |
| Primality | Deterministic Miller-Rabin with the first 12 primes as bases: exact for the whole 64-bit range, no probabilistic false "prime". |
| Configuration | All thresholds live in `appsettings.json`. Dev uses `ExpectedSeconds: 2` (band 1.6–2.4 s) so a demo runs in seconds. |

## Architecture

```
 worker 1 ─┐                        MONITOR
 worker 2 ─┼── TCP, one JSON ──►  TcpListener ── one async read loop per connection
 worker N ─┘   object per line          │
                                        │  fast checks only, never blocks:
                                        │  parse · sequence · duration band · measured interval
                                        │  update MachineState (per-machine lock)
                                        │
                                        ├── TryWrite ──► bounded Channel ──► VerifierPool (N readers)
                                        │   full? → UNVERIFIED            Miller-Rabin → NOT_PRIME
                                        │
          SilenceWatchdog (1 s timer) ──┤  machines silent past threshold → UNRESPONSIVE
          StatusReporter (10 s timer) ──┘  one STATUS line per machine
```

Message format (newline-delimited JSON):

```json
{"machineId":"1","seq":42,"prime":1000003,"durationMs":20113,"sentAtUtc":"2026-10-16T10:00:00Z"}
```

Key decisions:

- **The read loop never waits.** Primality checks go through a bounded queue with `TryWrite`. If the
  queue is full, the prime is reported `UNVERIFIED` rather than silently dropped, and a slow verifier
  can never make a healthy machine look silent.
- **Per-machine state changes only under that machine's lock.** Several threads touch the same machine:
  its connection, the verifiers, the watchdog, and possibly a second connection claiming the same ID.
  Alerts are built under the lock and logged outside it.
- **Liveness is checked by a timer, not by reads.** A machine that keeps its socket open but stops
  sending produces no reads at all, so only a timer can notice it.
- **The worker resends after a failed send.** A dropped connection then shows up as at most a
  duplicate (warning), never as lost work.

### Code map

| Path | Role |
|---|---|
| `src/PrimeMonitor/Program.cs` | Picks the mode (`monitor` or `worker`) and wires services. |
| `Shared/PrimeMessage.cs` | Wire format and strict parsing. |
| `Shared/Primality.cs` | Deterministic Miller-Rabin. |
| `Worker/WorkerRunner.cs` | Generates primes, paces to the expected time, reconnects with backoff (cap 5 s). |
| `Monitor/MonitorRunner.cs`, `ConnectionHandler.cs` | Accept loop and per-connection read loop. |
| `Monitor/Rules.cs` | Pure functions: duration band and sequence checks. |
| `Monitor/MachineState.cs`, `MachineRegistry.cs` | Per-machine state and alert decisions. |
| `Monitor/VerifierPool.cs` | Primality verification off the read path. |
| `Monitor/SilenceWatchdog.cs`, `StatusReporter.cs` | Liveness timer and periodic status. |
| `Monitor/Alerts.cs` | Alert types, severities, log format. |

## Running it

Requires the .NET 9 SDK (pinned in `global.json`).

```bash
dotnet build
dotnet run --project src/PrimeMonitor -- monitor
dotnet run --project src/PrimeMonitor -- worker --id 1
```

Start the monitor first, then one worker per terminal with different `--id` values. Run `dotnet build`
once up front: parallel `dotnet run` calls otherwise compete for the same build output.

Things to try:

- **Stop a worker** (close its window): `DISCONNECTED` immediately, then `UNRESPONSIVE` about 3–4 s later.
  Restart it: `RECOVERED`.
- **Restart the monitor:** workers back off, reconnect, and carry on with no false sequence gaps.
- **Send a bad machine by hand** (PowerShell): composite number, then a sequence gap, then garbage:

  ```powershell
  $c = [Net.Sockets.TcpClient]::new("127.0.0.1", 5050); $w = [IO.StreamWriter]::new($c.GetStream()); $w.NewLine = "`n"; $w.AutoFlush = $true
  $w.WriteLine('{"machineId":"9","seq":1,"prime":561,"durationMs":2000,"sentAtUtc":"2026-10-16T10:00:00Z"}')
  $w.WriteLine('{"machineId":"9","seq":5,"prime":1000003,"durationMs":2000,"sentAtUtc":"2026-10-16T10:00:02Z"}')
  $w.WriteLine('garbage'); $c.Close()
  ```

Run the tests:

```bash
dotnet test
```

## Alert reference

Every alert is one log line naming the machine:

```
ALERT severity=error type=TOO_SLOW machine=1 seq=17 detail="reported 3100ms, measured interval 3104ms, band 1600-2400ms"
```

| Alert | Severity | Raised when | Raised how often |
|---|---|---|---|
| `NOT_PRIME` | error | The verifier finds the number is composite. | Every occurrence |
| `TOO_SLOW` | error | Reported duration or measured arrival interval is above the band. | On change: once, then again only after recovering |
| `TOO_FAST` | warning | Reported duration is below the band. | On change |
| `UNRESPONSIVE` | error | No message for longer than the band's upper bound + grace (connection may still be open). | On change |
| `RECOVERED` | info | An unresponsive machine sends again. | On change |
| `SEQ_GAP` | error | `seq` jumps forward (messages lost). | Every occurrence |
| `SEQ_DUPLICATE` | warning | `seq` repeats or goes backwards on the same run. | Every occurrence |
| `MALFORMED` | error | A line fails to parse. The connection stays open. | Every occurrence |
| `DISCONNECTED` | warning | The machine's socket closes or errors (not raised when the monitor itself shuts down). | Every occurrence |
| `UNVERIFIED` | warning | The verifier queue is full, so the prime was not checked. | Every occurrence |

Ongoing conditions (slow, fast, silent) alert on state change so a persistently slow machine produces one
alert, not one per message. Discrete events (a wrong answer, a lost message) alert every time: hiding the
second composite number would be a mistake.

Every 10 s the monitor also logs a status line per machine:

```
STATUS machine=2 state=UNRESPONSIVE lastSeen=4.9s msgs=2 alerts=UNRESPONSIVE=1,DISCONNECTED=1
```

## Testing

68 xUnit tests cover primality (including Carmichael numbers, a strong pseudoprime to bases 2–7, and the
largest 64-bit prime, plus a cross-check against trial division for every n < 100 000), band edges,
sequence rules, the alert state machine, the watchdog on a fake clock, and disconnects over real sockets.

Concurrency risks are tested by forcing the race: a barrier releases all threads at once, thousands of
times, and each test prints how often the calls actually overlapped. Each was also run with the
per-machine lock removed to confirm the test fails without it.

| Risk | Test |
|---|---|
| Two connections with the same machine ID send the same `seq` at once (check-then-act on `seq`; also races registry creation) | Exactly one `SEQ_DUPLICATE` per machine, no lost updates |
| Verifiers and the read loop raising alerts on one machine at once | Every alert counted |
| Watchdog silence check racing an arriving message | A machine that has just sent is never left flagged `UNRESPONSIVE` |

## Known limitations

- **No fault injection on the worker yet.** Faults are demonstrated by stopping workers or sending lines by hand (above).
- **No cap on line length.** A stream that never sends a newline grows memory until the connection drops.
- **No web dashboard.** Output is console alerts plus periodic status lines.
- **No persistence, no authentication, a single monitor instance.**
- Two connections sharing one machine ID share that machine's interval state, and it only shows
  `DISCONNECTED` once both have closed.

## Scaling

- Liveness checks are cheap and real-time. Primality checks go through a bounded queue, so slow
  verification never makes a healthy machine look silent.
- If verification is expensive, each machine needs roughly one verifier's worth of CPU. Beyond that, scale
  verifiers out, or sample (and verify every prime from machines already flagged).
- Async I/O handles thousands of connections on one box. Beyond that, shard machines across monitors by
  machine ID, or publish through a message broker.
- A single monitor is a single point of failure: run two, or have the monitor emit its own heartbeat.
- Queue depth and the age of the oldest unverified item should themselves be alerts.
