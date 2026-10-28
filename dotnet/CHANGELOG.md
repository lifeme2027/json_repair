# Changelog

All notable changes to the .NET port (`dotnet/`) are listed here. This port is unofficial and is not affiliated with or endorsed by the upstream [json_repair](https://github.com/mangiucugna/json_repair) project. The Python package in this repository has its own history and is not covered by this file.

## 0.1.0 (unreleased)

Not yet published to NuGet and no release or tag exists. Build from source; see [README.md](README.md).

### Added

- `JsonRepairNet` class library for .NET 8 with `JsonRepair.Repair`, `Repair<T>`, `RepairNode`, and `TryRepair`, plus `JsonRepairOptions` (indented output with a configurable indent size) and `JsonRepairException`. Its only dependency is `System.Text.Json` from the shared framework.
- `JsonRepairNet.Cli` project and `json-repair` dotnet tool: reads a file or standard input, `--output` / `-o` writes a file, `--indent [n]` pretty-prints.
- Repairs for common malformed input: missing or single/smart quotes, missing, repeated, and trailing commas, unclosed or mismatched containers, comments, markdown fences and surrounding prose, truncated values, Python-style `True` / `False` / `None`, and a few number edge cases. The full list is in [README.md](README.md).
- xUnit test project (140 tests at the time of writing) and a `dotnet` GitHub Actions workflow that builds and tests on push and pull request.
- NuGet package metadata (license, tags, README) for both packages. Both can be built locally with `dotnet pack -c Release`.
- Upstream-parity tests that compare repaired output to Python `json_repair` as parsed JSON, plus documented-difference cases for intentional .NET behavior.

### Fixed

- Skip extra colons after an object member's first colon so `{"a":: 1, "b": 2}` repairs to `{"a":1,"b":2}`.
- Unquoted keys that end with a quote: `{"a": 1, b": 2}` and `{a": 1}` now repair to `{"a":1,"b":2}` and `{"a":1}` instead of keeping the stray quote in the key, matching Python `json_repair`. The quote is only stripped when the key has no other quote and a colon follows.

### Known differences from upstream

- Mid-value block comments: Python can treat `/* c */` as the value (`{"a":""}` for `{"a": /* c */ 1, "b": 2}`); this port skips the comment and keeps the following value.
- Truncated keys: Python drops a dangling key (`{"a": 1, "b` → `{"a":1}`); this port closes it as `{"a":1,"b":""}`.
- `NaN` / `Infinity`: Python emits those non-JSON tokens; this port returns a valid JSON string, currently `["NaN, Infinity"]` for `[NaN, Infinity]`.
- Leading zeros: Python coerces `[007, 1.50]` to `[7, 1.5]`; this port keeps `"007"` as a string and `1.50` as a number.
- Unclosed quoted scalars such as `"abc`: Python returns empty output; this port closes the string (`"abc"`).
- Empty, whitespace-only, plain-text, and unquoted bare-word inputs: Python returns an empty string; this port requires valid JSON, so `Repair` throws `JsonRepairException` and `TryRepair` returns false.
- Some other rare Python parser quirks (including garbage output for a missing colon such as `{"a" 1}`) are not reproduced.

### Known limitations

- JSON Schema and Pydantic guidance, `strict` mode, and streaming chunk reads from the Python package are not implemented.
- Some rare string-boundary cases from the Python parser are not reproduced.
- Input nested deeper than 256 containers fails with `JsonRepairException`.
