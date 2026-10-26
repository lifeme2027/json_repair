# JsonRepairNet

[![dotnet](https://github.com/composehk/json_repair/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/composehk/json_repair/actions/workflows/dotnet.yml)

[English](#english) | [中文](#中文)

## English

> **Unofficial port.** JsonRepairNet is an unofficial .NET port. It is not affiliated with or endorsed by the upstream project or its author. The repair behavior is based on [json_repair](https://github.com/mangiucugna/json_repair) by Stefano Baccianella ([@mangiucugna](https://github.com/mangiucugna)), which is released under the MIT license. Credit for the original design and test scenarios belongs to the upstream project. This port covers part of that behavior (see [Limits](#limits)).

A .NET 8 class library and `dotnet` tool that repair malformed JSON. This port lives beside the Python package and does not replace it.

> **Status:** version `0.1.0` is not yet published to NuGet. Build it from source (see below).

- Target framework: `net8.0`
- Package id: `JsonRepairNet`
- Tool command: `json-repair`
- Runtime dependencies: `System.Text.Json` only (included in the shared framework)

### Library

```csharp
using JsonRepairNet;
using System.Text.Json.Nodes;

string json = JsonRepair.Repair("{name: \"Ada\", ok: True,}");
// {"name":"Ada","ok":true}

if (JsonRepair.TryRepair(text, out string? repaired))
{
    Console.WriteLine(repaired);
}

JsonObject? document = JsonRepair.Repair<JsonObject>(text);
JsonNode? node = JsonRepair.RepairNode(text);
if (JsonRepair.TryRepair(text, out JsonNode? any))
{
    Console.WriteLine(any);
}

string pretty = JsonRepair.Repair(text, JsonRepairOptions.Indented(2));
```

`Repair` throws `JsonRepairException` when the input cannot be repaired. `TryRepair` returns `false` for null, blank, or unrecoverable input. JSON `null` is a successful value: the string form is `null`, and the node form is a null reference.

Valid JSON is returned unchanged. Set `JsonRepairOptions.WriteIndented` when you want that text pretty-printed. `IndentSize` is the number of spaces per level, from 0 through 16.

Build, test, and pack from this repo (the package is not on NuGet yet):

```bash
dotnet build dotnet/JsonRepairNet/JsonRepairNet.sln -c Release
dotnet test dotnet/JsonRepairNet/JsonRepairNet.sln -c Release
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet/JsonRepairNet.csproj -c Release -o /tmp/json-repair-nupkg
```

To use the packed library from another project, add the output folder as a package source:

```bash
dotnet add package JsonRepairNet --version 0.1.0 --source /tmp/json-repair-nupkg
```

Once published to NuGet, this will become:

```bash
dotnet add package JsonRepairNet
```

### CLI

`json-repair` reads one file or, when the file is omitted, standard input. `--output` (`-o`) writes a file. `--indent` pretty-prints. A following integer sets the indent width; the default width is 2.

```bash
json-repair broken.json --output fixed.json --indent 2
cat broken.json | json-repair --indent
json-repair --help
```

Run it from this repo without installing:

```bash
dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- broken.json --output fixed.json --indent 2
```

Install the tool locally after packing:

```bash
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet.Cli/JsonRepairNet.Cli.csproj -c Release -o /tmp/json-repair-nupkg
dotnet tool install --global --add-source /tmp/json-repair-nupkg JsonRepairNet.Cli
```

Once published to NuGet, this will become `dotnet tool install --global JsonRepairNet.Cli`.

The CLI writes one trailing newline. Exit code `0` means the repair succeeded. Exit code `1` means bad arguments, a missing file, or input that could not be repaired.

### Supported repairs

- Missing quotes around object keys or string values.
- Missing commas, repeated commas, and trailing commas.
- Missing closers, a missing opening `{` before a quoted key, and mismatched `)`, `]`, or `}`.
- Single quotes and common smart quotes rewritten as JSON double quotes.
- `//` line comments, `/* */` block comments (including an unclosed block), and `#` line comments outside strings.
- Markdown fences (` ```json ` … ` ``` ` or plain ` ``` `) and prose before or after a JSON value.
- Truncated strings, arrays, and objects. Unclosed containers are closed; a missing value becomes `""`.
- `True` / `False` / `None` and other casings of `true` / `false` / `null`, including at the top level.
- Valid JSON left byte-for-byte as it arrived, including spacing.
- Underscores inside numbers (`82_461_110`), a leading dot (`.25`), and incomplete exponents (`1e`, `1e+`).
- Values such as `1/3`, `10-20`, and `1notanumber` kept as strings.
- `...` placeholders inside arrays are skipped.
- Parentheses: `(1, 2)` becomes an array; `(1)` stays the scalar `1`.
- When a complete markdown fence is present, each fenced payload is repaired. Several payloads become one JSON array.
- Several top-level JSON values become one JSON array.
- Duplicate keys in repaired objects keep the last value.

### Limits

- JSON Schema and Pydantic guidance are not part of this port. `strict` mode and streaming chunk reads are not part of it either.
- Invalid input nested deeper than 256 containers fails with `JsonRepairException` instead of growing the call stack without a bound.
- A complete `` ``` `` fence wins over JSON written outside the fence.
- Plain prose with no JSON value makes `TryRepair` return `false`.
- The writer emits compact JSON for repairs. It does not try to copy the original spacing of broken input.
- Some rare string-boundary cases from the Python parser are not reproduced. The scenarios listed above are covered by the test suite.

## 中文

> **非官方移植。** JsonRepairNet 是非官方的 .NET 移植，与上游项目及其作者没有隶属关系，也未获得其认可。修复行为参考 Stefano Baccianella（[@mangiucugna](https://github.com/mangiucugna)）的 [json_repair](https://github.com/mangiucugna/json_repair)，上游以 MIT 许可证发布。原始设计和测试场景的功劳属于上游项目。本移植只覆盖其中一部分行为（见下方“已知限制”）。

这是 json_repair 核心修复能力的 .NET 8 类库和 `dotnet` 工具，与 Python 包放在同一仓库，互不影响。

> **状态：** `0.1.0` 版本尚未发布到 NuGet，请从源码构建（见下文）。

- 目标框架：`net8.0`
- 包名：`JsonRepairNet`
- 命令名：`json-repair`
- 运行时依赖：只有共享框架里的 `System.Text.Json`

### 库

```csharp
using JsonRepairNet;
using System.Text.Json.Nodes;

string json = JsonRepair.Repair("{name: \"Ada\", ok: True,}");
// {"name":"Ada","ok":true}

if (JsonRepair.TryRepair(text, out string? repaired))
{
    Console.WriteLine(repaired);
}

JsonObject? document = JsonRepair.Repair<JsonObject>(text);
JsonNode? node = JsonRepair.RepairNode(text);
if (JsonRepair.TryRepair(text, out JsonNode? any))
{
    Console.WriteLine(any);
}

string pretty = JsonRepair.Repair(text, JsonRepairOptions.Indented(2));
```

无法修复时，`Repair` 抛出 `JsonRepairException`，`TryRepair` 返回 `false`。空串、空白和纯说明文字属于无法修复。JSON 的 `null` 是成功结果：字符串形式是 `null`，节点形式是空引用。

合法 JSON 按原文字返回。需要美化时使用 `JsonRepairOptions.WriteIndented`。`IndentSize` 是每一级的空格数，范围是 0 到 16。

在本仓库中构建、测试和打包（包尚未发布到 NuGet）：

```bash
dotnet build dotnet/JsonRepairNet/JsonRepairNet.sln -c Release
dotnet test dotnet/JsonRepairNet/JsonRepairNet.sln -c Release
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet/JsonRepairNet.csproj -c Release -o /tmp/json-repair-nupkg
```

在其他项目中使用打好的包时，把输出目录作为包源：

```bash
dotnet add package JsonRepairNet --version 0.1.0 --source /tmp/json-repair-nupkg
```

发布到 NuGet 之后，可以改用：

```bash
dotnet add package JsonRepairNet
```

### 命令行

`json-repair` 从文件读取；省略文件时从标准输入读取。`--output`（`-o`）写入文件。`--indent` 美化输出。后面可以跟空格数，默认 2。

```bash
json-repair broken.json --output fixed.json --indent 2
cat broken.json | json-repair --indent
json-repair --help
```

不安装、直接在仓库里运行：

```bash
dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- broken.json --output fixed.json --indent 2
```

打包后安装：

```bash
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet.Cli/JsonRepairNet.Cli.csproj -c Release -o /tmp/json-repair-nupkg
dotnet tool install --global --add-source /tmp/json-repair-nupkg JsonRepairNet.Cli
```

发布到 NuGet 之后，可以直接使用 `dotnet tool install --global JsonRepairNet.Cli`。

命令行会在结果末尾加一个换行。退出码 `0` 表示修复成功。退出码 `1` 表示参数错误、文件不存在，或输入无法修复。

### 支持的修复

- 对象键或字符串值缺少引号。
- 缺少逗号、重复逗号、尾随逗号。
- 缺少结束括号、引号键前面缺少 `{`，以及 `)`、`]`、`}` 不匹配。
- 单引号和常见弯引号改成 JSON 双引号。
- 字符串外的 `//` 行注释、`/* */` 块注释（含未闭合块注释）和 `#` 行注释。
- Markdown 代码块（` ```json ` … ` ``` ` 或普通 ` ``` `），以及 JSON 前后的说明文字。
- 截断的字符串、数组和对象。未闭合的容器会被补上；缺失的值变成 `""`。
- `True` / `False` / `None`，以及 `true` / `false` / `null` 的其他大小写，包括出现在顶层时。
- 合法 JSON 原样保留，包括空格和换行。
- 数字里的下划线（`82_461_110`）、前导小数点（`.25`）和不完整指数（`1e`、`1e+`）。
- `1/3`、`10-20`、`1notanumber` 这类值保留为字符串。
- 数组里的 `...` 占位会被跳过。
- 圆括号：`(1, 2)` 变成数组，`(1)` 保持标量 `1`。
- 存在成对的 `` ``` `` 代码块时，只修复代码块里的内容。多段代码块会合成一个 JSON 数组。
- 多个顶层 JSON 值会合成一个 JSON 数组。
- 修复对象时，重复键保留最后一次的值。

### 已知限制

- 本移植不包含 JSON Schema、Pydantic 引导修复、`strict` 模式和分块流式读取。
- 非法输入的容器嵌套超过 256 层时，会抛出 `JsonRepairException`，避免调用栈无限增长。
- 成对的 `` ``` `` 代码块优先于围栏外的 JSON。
- 只有说明文字、没有 JSON 值时，`TryRepair` 返回 `false`。
- 修复结果使用紧凑 JSON，不会沿用损坏输入原来的空格。
- Python 解析器里少数很细的字符串边界情况没有逐条照搬。上面列出的场景由测试覆盖。
