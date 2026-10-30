# Changelog

All notable changes to the .NET port (`dotnet/`) are listed here. This port is unofficial and is not affiliated with or endorsed by the upstream [json_repair](https://github.com/mangiucugna/json_repair) project. The Python package in this repository has its own history and is not covered by this file.

## Unreleased

### Added

- `json-repair` accepts `--inline`/`-i` to replace the input file, matching the Python CLI. Tests cover stdin, file input, stdout, `--output`/`-o`, `--inline`, `--indent`, `--help`, and error exit codes. `CliRunner.Run` is public so those tests can supply streams.

### Fixed

- Members after a premature top-level `}` with a missing colon (`{"key": "value"}, "key2" "value2"}`) are merged into the object.

Aligned 20 more inputs from the upstream Python test suite with `repair_json` (json-repair 0.64.2). Each one has a regression test in `UpstreamParityTests`:

- Incomplete exponents: `[1e+]` and `[1e-]` become `["1e"]`; a bare `1e` still becomes `1`.
- Parentheses work as arrays: `("a", "b", "c")` becomes `["a","b","c"]`, `{"empty": ()}` becomes `{"empty":[]}`, and a one-item group such as `("x")` becomes the item itself.
- `{"key:value}` becomes `{"key":"value"}`.
- Trailing whitespace and newlines are removed from keys (`{"key_1\n": "value"}`).
- `{"" key":"val"` becomes `{" key":"val"}`.
- Bare values keep balanced braces: `{text:words{words in brackets}}` and `{text:words{words in brackets}m}`.
- A `#` comment before an inner `}` no longer drops the members that follow it.
- `{"key", 105,12,` becomes `{"key":"105,12"}`.
- An extra `}` between objects in an array is skipped.
- An array closed with `}` (`{"key1": ["value1", "value2"}, "key2": ...}`) keeps the members that follow it.
- Members after a premature top-level `}` (`{"key": "value"}, "key2": "value2"}` and similar) are merged into the object.
- `"According...""ans": "YES"` closes the string and starts the next key.

Aligned 18 more upstream inputs (209 of 225 upstream `repair_json` test inputs now match json-repair 0.64.2 as parsed JSON, up from 191), each with a regression test in `UpstreamParityTests`:

- Several top-level values: an empty value is replaced by the next non-empty one, a later value with the same shape replaces the earlier one, and empty trailing values are dropped (`[]{}` becomes `[]`, `[]{"key":"value"}` becomes `{"key":"value"}`, `{"key": "value"}, []` and `{"key": "value"}, {}` become `{"key":"value"}`, `[{"key":"value"}][{"key":"value_after"}]` becomes `[{"key":"value_after"}]`).
- Empty partial items in arrays are dropped: `[{]` and `["` become `[]`.
- Sets become arrays: `{'key1', 'key2'}` becomes `["key1","key2"]`; `[{5}s ` becomes `[[5]]`.
- Arrays that follow an array-valued member are merged into it (`{ "key": ["arrayvalue"], ["arrayvalue1"], ["arrayvalue2"], "key3": "value3" }`, `{ "key": [[1, 2, 3], "a", "b"], [[4, 5, 6], [7, 8, 9]] }`, and the `headers`/`rows` table case).
- A quote followed by a bare word ends an array string when the string has no inner quotes (`["value1" value2", "value3"]`, `"consectetur" comment"`).
- Stray words between members are dropped (`SHOULD_NOT_EXIST`, `AAAA`), and a bare object value stops at a quote (`{0:a"0"` becomes `{"0":"a"}`).

## 0.1.1

Maintenance release. Published to NuGet as `JsonRepairNet` and `JsonRepairNet.Cli` by jun chen, the repository owner.

### Fixed

- A key with only a trailing quote is treated as an unquoted key, so `{"a": 1, b": 2}` now repairs to `{"a":1,"b":2}`, matching upstream Python `json_repair`.

### Changed

- The `JsonRepairNet.Cli` package now includes the MIT `LICENSE` file, as the `JsonRepairNet` package already did.
- Release builds are now deterministic (`Deterministic` and `ContinuousIntegrationBuild`), so the packed assemblies and symbols no longer contain local build paths.
- The test suite now has 140 tests.

## 0.1.0

Published to NuGet as `JsonRepairNet` and `JsonRepairNet.Cli` by jun chen, the repository owner. No Git tag or GitHub release exists for it. You can also build from source; see [README.md](README.md).

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
- Other upstream test inputs that still differ (15 of 225 checked against json-repair 0.64.2). Upstream handles these with string heuristics that are hard to port without breaking other cases, or it returns an empty string where this port returns valid JSON:
  - Bare literals and empty strings at the top level (`True`, `False`, `Null`, `""`, `"`, `'"'`): upstream returns an empty string; this port returns `true`, `false`, `null`, or `""`.
  - Truncated key after an object in an array (`[{"key": "value", "key`): upstream returns `[{"key":"value"},["key"]]`.
  - Stray quotes and words inside strings or between members (`COMMENT "notes": "lorem "ipsum", sic."`, doubled quotes like `""answer""` and `""""`, `{ "a" : "{ b": {} }" }`, the unquoted sentences in `{"lorem": sic tamet. ...}`, and the long `"lorem "ipsum" dolor ..."` array).
  - Escaped JSON inside a string (`{"key": "{\\"key\\\":[...`) and a markdown fence inside a string value (`{"key": "```json {...}```"}`).
- `NaN` / `Infinity`: Python emits those non-JSON tokens; this port returns a valid JSON string, currently `["NaN, Infinity"]` for `[NaN, Infinity]`.
- Leading zeros: Python coerces `[007, 1.50]` to `[7, 1.5]`; this port keeps `"007"` as a string and `1.50` as a number.
- Unclosed quoted scalars such as `"abc`: Python returns empty output; this port closes the string (`"abc"`).
- Empty, whitespace-only, plain-text, and unquoted bare-word inputs: Python returns an empty string; this port requires valid JSON, so `Repair` throws `JsonRepairException` and `TryRepair` returns false.
- Some other rare Python parser quirks (including garbage output for a missing colon such as `{"a" 1}`) are not reproduced.

### Known limitations

- JSON Schema and Pydantic guidance, `strict` mode, and streaming chunk reads from the Python package are not implemented.
- Some rare string-boundary cases from the Python parser are not reproduced.
- Input nested deeper than 256 containers fails with `JsonRepairException`.
