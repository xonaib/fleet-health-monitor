using System.Collections.Concurrent;

namespace PrimeMonitor.Monitor;

public sealed class MachineRegistry
{
    private readonly ConcurrentDictionary<string, MachineState> _machines = new();

    // GetOrAdd may run the factory twice under a race, but every caller gets the same stored instance.
    public MachineState GetOrAdd(string machineId) => _machines.GetOrAdd(machineId, id => new MachineState(id));

    public IEnumerable<MachineState> All => _machines.Values;
}
