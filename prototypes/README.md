# Prototypes

Throw-away code for design spikes. Nothing here is part of
`MedReminder.sln`, nothing ships, and nothing here may be referenced
from `src/`. Production code re-implements what a spike validates.

## B1.SyncPrototype — spike S9

Spike S9 of [`docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md`](../docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md):
the ledger derivation, the merge rules and the convergence simulation,
in pure `net10.0` code. It references `MedReminder.Domain` and reuses
`ConsumptionMaterializer`, `DailyConsumption`, `SuspensionState` and
`RunOutForecast` unchanged.

| File | Content |
|---|---|
| `B1.SyncPrototype/Hlc.cs` | Hybrid logical clock |
| `B1.SyncPrototype/Operations.cs` | Replicated operations (facts and field assignments) |
| `B1.SyncPrototype/Register.cs` | Last-writer-wins register with version history |
| `B1.SyncPrototype/Replica.cs` | Replicated state, idempotent apply, state hash |
| `B1.SyncPrototype/LedgerDeriver.cs` | Derived ledger, count anchors, derived epoch |
| `B1.SyncPrototype/Simulation/` | Simulated cloud folder, devices, random histories |
| `B1.SyncPrototype.Tests/ParityHarness.cs` | Runs each action on the real use cases (oracle) and on the prototype |
| `B1.SyncPrototype.Tests/ParityTests.cs` | Acceptance (a): parity with today's behavior |
| `B1.SyncPrototype.Tests/ConvergenceTests.cs` | Acceptance (b): convergence of 2–5 devices |
| `B1.SyncPrototype.Tests/DocumentedDifferenceTests.cs` | Deliberate differences (D6, D15, same-date schedule) |

The oracle is the real Application use cases over the in-memory
repositories of `tests/MedReminder.Application.Tests/Support`, linked
into the test project.

### Running

Runs on Linux, macOS or Windows with the .NET 10 SDK:

```
dotnet test prototypes/B1.SyncPrototype.Tests -c Release
```

The default run uses 300 seeds per randomized test. The recorded result
used 10 000:

```
S9_PARITY_SEEDS=10000 S9_SIM_SEEDS=10000 dotnet test prototypes/B1.SyncPrototype.Tests -c Release
```

`S9_FIRST_SEED` and `S9_SIM_FIRST_SEED` replay a single failing seed.

### Scope limits

UTC only (no time zones, no DST); no encryption; in-memory storage, no
EF Core; no generation reset; no notification planning. Results and
findings are recorded in §18 of the analysis document.
