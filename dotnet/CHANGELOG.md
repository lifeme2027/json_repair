# Changelog

All notable changes to the .NET port (`dotnet/`) are listed here. This port is unofficial and is not affiliated with or endorsed by the upstream [json_repair](https://github.com/mangiucugna/json_repair) project. The Python package in this repository has its own history and is not covered by this file.

## 0.1.0 (unreleased)

Not yet published to NuGet and no release or tag exists. Build from source; see [README.md](README.md).

### Added

- `JsonRepairNet` class library for .NET 8 with `JsonRepair.Repair`, `Repair<T>`, `RepairNode`, and `TryRepair`, plus `JsonRepairOptions` (indented output with a configurable indent size) and `JsonRepairException`. Its only dependency is `System.Text.Json` from the shared framework.
- `JsonRepairNet.Cli` project and `json-repair` dotnet tool: reads a file or standard input, `--output` / `-o` writes a file, `--indent [n]` pretty-prints.
- Repairs for common malformed input: missing or single/smart quotes, missing, repeated, and trailing commas, unclosed or mismatched containers, comments, markdown fences and surrounding prose, truncated values, Python-style `True` / `False` / `None`, and a few number edge cases. The full list is in [README.md](README.md).
- xUnit test project (92 tests at the time of writing) and a `dotnet` GitHub Actions workflow that builds and tests on push and pull request.
- NuGet package metadata (license, tags, README) for both packages. Both can be built locally with `dotnet pack -c Release`.

### Known limitations

- JSON Schema and Pydantic guidance, `strict` mode, and streaming chunk reads from the Python package are not implemented.
- Some rare string-boundary cases from the Python parser are not reproduced.
- Input nested deeper than 256 containers fails with `JsonRepairException`.
