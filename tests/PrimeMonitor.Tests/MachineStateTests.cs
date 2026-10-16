using PrimeMonitor.Monitor;
using Xunit.Abstractions;
using static PrimeMonitor.Tests.TestData;

namespace PrimeMonitor.Tests;

public class MachineStateTests(ITestOutputHelper output)
{
    private static List<Alert> Send(MachineState state, long seq, long durationMs = 20000, bool first = false, double atSeconds = 0) =>
        state.OnMessage(Msg(seq, durationMs), first, T0.AddSeconds(atSeconds), Band20s).ToList();

    [Fact]
    public void OnMessage_FirstMessageEver_AcceptsAnySeqAsBaseline()
    {
        var state = new MachineState("1");

        var alerts = Send(state, 50, first: true);

        Assert.Empty(alerts);
        Assert.Equal(50, state.Snapshot().LastSeq);
    }

    [Fact]
    public void OnMessage_SeqOneOnNewConnection_TreatedAsWorkerRestart()
    {
        var state = new MachineState("1");
        Send(state, 10, first: true);

        var alerts = Send(state, 1, first: true, atSeconds: 60);

        Assert.Empty(alerts);
        Assert.Equal(1, state.Snapshot().LastSeq);
    }

    [Fact]
    public void OnMessage_SeqSkipsAhead_RaisesSeqGap()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        var alerts = Send(state, 4, atSeconds: 20);

        var alert = Assert.Single(alerts);
        Assert.Equal(AlertType.SeqGap, alert.Type);
        Assert.Contains("2 missing", alert.Detail);
    }

    [Fact]
    public void OnMessage_SeqRepeated_RaisesSeqDuplicate()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);
        Send(state, 2, atSeconds: 20);

        var alerts = Send(state, 2, atSeconds: 20.1);

        Assert.Equal(AlertType.SeqDuplicate, Assert.Single(alerts).Type);
        Assert.Equal(2, state.Snapshot().LastSeq);
    }

    [Fact]
    public void OnMessage_SeqOneOnSameConnection_RaisesSeqDuplicate()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);
        Send(state, 2, atSeconds: 20);

        var alerts = Send(state, 1, atSeconds: 40);

        Assert.Equal(AlertType.SeqDuplicate, Assert.Single(alerts).Type);
    }

    [Fact]
    public void OnMessage_SlowOnEveryMessage_RaisesTooSlowOnce()
    {
        var state = new MachineState("1");

        var alerts = new[] { Send(state, 1, 30000, first: true), Send(state, 2, 30000, atSeconds: 20), Send(state, 3, 30000, atSeconds: 40) }
            .SelectMany(a => a).ToList();

        Assert.Equal(AlertType.TooSlow, Assert.Single(alerts).Type);
    }

    [Fact]
    public void OnMessage_SlowThenHealthyThenSlow_RaisesTooSlowTwice()
    {
        var state = new MachineState("1");

        Send(state, 1, 30000, first: true);
        Send(state, 2, 20000, atSeconds: 20);
        Send(state, 3, 30000, atSeconds: 40);

        Assert.Equal(2, state.Snapshot().Count(AlertType.TooSlow));
    }

    [Fact]
    public void OnMessage_ReportsHealthyButArrivesLate_RaisesTooSlow()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        var alerts = Send(state, 2, 20000, atSeconds: 30);

        var alert = Assert.Single(alerts);
        Assert.Equal(AlertType.TooSlow, alert.Type);
        Assert.Contains("measured interval 30000ms", alert.Detail);
    }

    [Fact]
    public void OnMessage_LongGapAcrossReconnect_DoesNotRaiseTooSlow()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        var alerts = Send(state, 2, 20000, first: true, atSeconds: 120);

        Assert.Empty(alerts);
    }

    [Fact]
    public void OnMessage_ReportedTooFast_RaisesTooFast()
    {
        var state = new MachineState("1");

        var alerts = Send(state, 1, 10000, first: true);

        Assert.Equal(AlertType.TooFast, Assert.Single(alerts).Type);
    }

    [Fact]
    public void CheckSilence_ExactlyAtThreshold_RaisesNothing()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        Assert.Null(state.CheckSilence(T0 + Threshold20s, Threshold20s));
    }

    [Fact]
    public void CheckSilence_JustPastThreshold_RaisesUnresponsive()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        var alert = state.CheckSilence(T0 + Threshold20s + TimeSpan.FromMilliseconds(1), Threshold20s);

        Assert.Equal(AlertType.Unresponsive, alert?.Type);
        Assert.True(state.Snapshot().Unresponsive);
    }

    [Fact]
    public void CheckSilence_StillSilentOnLaterTicks_RaisesUnresponsiveOnce()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        for (var s = 26; s <= 60; s++) state.CheckSilence(T0.AddSeconds(s), Threshold20s);

        Assert.Equal(1, state.Snapshot().Count(AlertType.Unresponsive));
    }

    [Fact]
    public void OnMessage_AfterUnresponsive_RaisesRecoveredAndClearsFlag()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);
        state.CheckSilence(T0.AddSeconds(30), Threshold20s);

        var alerts = Send(state, 2, atSeconds: 40);

        Assert.Contains(alerts, a => a.Type == AlertType.Recovered);
        Assert.False(state.Snapshot().Unresponsive);
    }

    [Fact]
    public void OnMessage_AfterSilenceOnSameConnection_AlsoRaisesTooSlow()
    {
        // Agreed behaviour: the work really did arrive late, so the measured interval still counts.
        var state = new MachineState("1");
        Send(state, 1, first: true);
        state.CheckSilence(T0.AddSeconds(30), Threshold20s);

        var alerts = Send(state, 2, atSeconds: 40);

        Assert.Contains(alerts, a => a.Type == AlertType.TooSlow);
    }

    [Fact]
    public void OnDisconnected_LastConnection_RaisesDisconnectedAndShowsDisconnectedState()
    {
        var state = new MachineState("1");
        Send(state, 1, first: true);

        var alert = state.OnDisconnected("socket closed");

        Assert.Equal(AlertType.Disconnected, alert.Type);
        Assert.Equal("DISCONNECTED", state.Snapshot().State);
    }

    [Fact]
    public void CheckSilence_RacingAnArrivingMessage_NeverLeavesMachineFlagged()
    {
        // The watchdog reads last-arrival and sets the flag; OnMessage writes last-arrival and clears the flag.
        // Interleaved without the lock, the watchdog can flag using the stale time after the message has
        // already been handled, leaving a machine that just sent stuck as UNRESPONSIVE.
        const int iterations = 20_000;
        var states = Enumerable.Range(0, iterations).Select(i => new MachineState($"m{i}")).ToArray();
        foreach (var s in states) s.OnMessage(Msg(1), true, T0, Band20s);
        var at = T0.AddSeconds(30);

        var overlaps = Race.Run(2, iterations, (i, t) =>
        {
            if (t == 0) states[i].CheckSilence(at, Threshold20s);
            else states[i].OnMessage(Msg(2), false, at, Band20s);
        });

        var snapshots = states.Select(s => s.Snapshot()).ToList();
        var watchdogFirst = snapshots.Count(s => s.Count(AlertType.Unresponsive) == 1);
        output.WriteLine($"RACE: {overlaps} of {iterations} iterations overlapped; watchdog won {watchdogFirst}, message won {iterations - watchdogFirst}");
        Assert.True(overlaps > 0, "race window was never hit; test proves nothing");
        Assert.All(snapshots, s =>
        {
            Assert.False(s.Unresponsive, $"{s.MachineId} left flagged after sending");
            Assert.Equal(s.Count(AlertType.Unresponsive), s.Count(AlertType.Recovered));
        });
    }

    [Fact]
    public void RecordEvent_SameTypeRepeatedly_CountsEveryOccurrence()
    {
        var state = new MachineState("1");

        state.RecordEvent(AlertType.NotPrime, 1, "x");
        state.RecordEvent(AlertType.NotPrime, 2, "x");

        Assert.Equal(2, state.Snapshot().Count(AlertType.NotPrime));
    }

    [Fact]
    public void RecordEvent_ManyThreadsOnOneMachineAtOnce_CountsEveryAlert()
    {
        // Simulates verifiers and the connection loop raising alerts on the same machine simultaneously.
        const int threads = 4, iterations = 5000;
        var state = new MachineState("1");

        var overlaps = Race.Run(threads, iterations, (i, t) => state.RecordEvent(AlertType.NotPrime, i, "race"));

        output.WriteLine($"RACE: {overlaps} of {threads * iterations} calls started while another thread was inside RecordEvent");
        Assert.True(overlaps > 0, "race window was never hit; test proves nothing");
        Assert.Equal(threads * iterations, state.Snapshot().Count(AlertType.NotPrime));
    }
}
