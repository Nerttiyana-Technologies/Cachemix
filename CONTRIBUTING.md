# Contributing to Cachemix

Thanks for your interest in improving Cachemix. This guide covers how to get a
working build and what we expect in a pull request.

## Prerequisites

- .NET SDK **10.0.300** or newer (pinned in `global.json`).
- Docker — required only for the Redis integration tests.

## Build & test

```bash
dotnet restore Cachemix.slnx
dotnet build   Cachemix.slnx -c Release
dotnet test    Cachemix.slnx -c Release
```

Or use the all-in-one scripts, which mirror CI exactly:

```bash
./build/pack.sh        # bash
pwsh ./build/pack.ps1  # PowerShell
```

## Project layout

- `src/` — shipping libraries, multi-targeted (net8.0 / net9.0 / net10.0).
- `tests/` — unit and integration tests.
- `samples/` — runnable example apps.
- `benchmarks/` — BenchmarkDotNet overhead measurements.
- `docs/` — architecture and roadmap.

See [`docs/architecture-and-roadmap.md`](docs/architecture-and-roadmap.md) for the design.

## Pull requests

- Branch from `main`; keep each PR focused on one change.
- Add or update tests for any behavior change.
- Run `dotnet build` and `dotnet test` locally — CI builds with warnings as errors.
- Update `CHANGELOG.md` under the `[Unreleased]` heading.
- Public APIs need XML documentation comments.

## Coding style

Style is enforced by `.editorconfig`. Two project-specific rules matter most:

1. **Cachemix must never throw into the host's cache path.** Interception code is
   wrapped so a failure degrades telemetry silently and leaves the cache call intact.
2. **Keep the interception hot path allocation-free.** Telemetry events are
   value types written into a pre-allocated bounded channel.

## Reporting bugs & requesting features

Use the GitHub issue templates. For security issues, follow
[`SECURITY.md`](SECURITY.md) — do not open a public issue.

## License

By contributing, you agree that your contributions are licensed under the
project's MIT license.
