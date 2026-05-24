# Changelog

All notable changes to Cachemix are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Cachemix.Abstractions** — shared contracts: cache descriptors, the event
  model, health-report types, and the `ICacheValueRedactor` extension point.
- **Cachemix.Core** — the interception engine and telemetry pipeline: decorators
  for `IMemoryCache` and `IDistributedCache`, a bounded telemetry channel, the
  telemetry store and background processor, an OpenTelemetry meter, the cache
  health evaluator, `CachemixOptions`, and the `AddCachemix()` wiring. Fail-open
  by design — interception never throws into the host's cache path.
- **Cachemix.Hybrid** — `HybridCache` support, with tag tracking and
  value-factory timing.
- **Cachemix.StackExchangeRedis** — Redis key enumeration via `SCAN`, parsing
  the Microsoft distributed-cache hash format.
- **Cachemix.AspNetCore** — the `/cachemix` dashboard: self-contained
  middleware, a SignalR hub, the security model (environment gate, authorization
  filters, redaction, hardened response headers), and an embedded HTML/CSS/JS UI
  with a dark/light executive theme, a live key grid, a hit-ratio chart, an
  event feed and a health panel.
- **Cachemix** — a meta-package bringing in the engine, both providers and the
  dashboard.
- Three sample applications (Minimal API / `IMemoryCache`, MVC / Redis, and
  `HybridCache`).
- A test suite spanning core engine unit tests, integration tests for the `SCAN`
  key provider (run against a local Redis or Valkey server, or a throwaway
  container when none is found), and in-memory dashboard tests over `TestServer`,
  plus a BenchmarkDotNet project measuring the overhead the `IMemoryCache`
  observation decorator adds over an undecorated cache.
- Repository foundation: multi-targeted build (`net8.0` / `net9.0` / `net10.0`),
  Central Package Management, `global.json` pinned to SDK 10.0.300, `.editorconfig`,
  the `Cachemix.slnx` solution, `nuget.config`, CI / release / CodeQL workflows,
  architecture documentation, and repository-hygiene files.

[Unreleased]: https://github.com/isureshsubramanian/Cachemix/commits/main
