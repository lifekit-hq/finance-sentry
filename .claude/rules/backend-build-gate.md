---
paths:
  - "backend/**/*.cs"
---

# Backend Build Gate

The build fails on any warning: `TreatWarningsAsErrors` is on in `backend/Directory.Build.props`, so local builds, the Stop hook and CI all enforce zero warnings.

After writing or modifying **any** `.cs` file, run `dotnet build backend/FinanceSentry.sln` and fix **all warnings** before moving on. Where `dotnet` is not on the PATH (the fleet box), run it inside `mcr.microsoft.com/dotnet/sdk:10.0` the way the `test` command in `.no-mistakes.yaml` does - never skip it.

- Remove unused `using` directives (`IDE0005`)
- Apply primary constructor where suggested (`IDE0290`)
- Resolve nullable reference warnings (`CS8618`, `CS8600`–`CS8604`) - do not suppress with `!` without a comment
- Apply safe IDE suggestions (`IDE0161`, `IDE0028`, `IDE0059`) that do not change runtime behaviour
- Zero warnings before the task is marked complete

Use `/csharp-quality` for a batch cleanup sweep across multiple files.
