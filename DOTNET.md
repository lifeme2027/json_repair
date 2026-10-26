# .NET port

This repo keeps the original Python package and adds a C# / .NET 8 port of the core repair behavior.

- Code: [dotnet/JsonRepairNet](dotnet/JsonRepairNet)
- Usage, supported repairs, and limits: [dotnet/README.md](dotnet/README.md)
- Library package id: `JsonRepairNet` (project `dotnet/JsonRepairNet/src/JsonRepairNet`)
- CLI package id / project: `JsonRepairNet.Cli` (`dotnet/JsonRepairNet/src/JsonRepairNet.Cli`)
- Tool command: `json-repair`
- Status: unofficial port, not affiliated with or endorsed by the upstream project; not yet published to NuGet. Release notes: [dotnet/CHANGELOG.md](dotnet/CHANGELOG.md)

Run the CLI from source (no install needed):

```bash
dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- broken.json --output fixed.json --indent 2
cat broken.json | dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- --indent
```

Or pack it and install it as a local .NET tool (details in [dotnet/README.md](dotnet/README.md)):

```bash
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet.Cli/JsonRepairNet.Cli.csproj -c Release -o /tmp/json-repair-nupkg
dotnet tool install --global --add-source /tmp/json-repair-nupkg JsonRepairNet.Cli
json-repair --help
```

Python sources under `src/json_repair` are unchanged.

# .NET 移植

仓库保留原来的 Python 包，并额外提供 C# / .NET 8 核心修复移植。

- 代码：[dotnet/JsonRepairNet](dotnet/JsonRepairNet)
- 用法、支持场景和限制：[dotnet/README.md](dotnet/README.md)
- 类库包名：`JsonRepairNet`（项目 `dotnet/JsonRepairNet/src/JsonRepairNet`）
- 命令行包名 / 项目：`JsonRepairNet.Cli`（`dotnet/JsonRepairNet/src/JsonRepairNet.Cli`）
- 命令行名称：`json-repair`
- 状态：非官方移植，与上游项目没有隶属关系，也未获其认可；尚未发布到 NuGet。更新记录：[dotnet/CHANGELOG.md](dotnet/CHANGELOG.md)

不安装、直接从源码运行命令行：

```bash
dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- broken.json --output fixed.json --indent 2
cat broken.json | dotnet run --project dotnet/JsonRepairNet/src/JsonRepairNet.Cli -- --indent
```

也可以打包后安装为 .NET 工具（详见 [dotnet/README.md](dotnet/README.md)）：

```bash
dotnet pack dotnet/JsonRepairNet/src/JsonRepairNet.Cli/JsonRepairNet.Cli.csproj -c Release -o /tmp/json-repair-nupkg
dotnet tool install --global --add-source /tmp/json-repair-nupkg JsonRepairNet.Cli
json-repair --help
```

`src/json_repair` 下的 Python 源码没有改动。
