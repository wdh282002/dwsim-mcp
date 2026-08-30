# dwsim-mcp

中文说明。[English README](README.md) 是主文档，内容最完整。

面向 [DWSIM](https://dwsim.org) 开源化工流程模拟器的
[Model Context Protocol](https://modelcontextprotocol.io) 服务器。它把流程图、流股、
单元操作、热力学和求解器暴露成 MCP 工具，让 MCP 客户端能端到端地建模、配置和求解。

本仓库是上游 `DanWBR/dwsim10` 中 `tools/DWSIM.MCPServer` 的**分发与贡献载体**：
包含完整源码、产出免安装可执行文件的构建脚本，以及可直接打到 DWSIM 源码树上的补丁。

## 这个版本修了什么

### 严密精馏塔（DistillationColumn / AbsorptionColumn）

在 DWSIM 里把一股流接到严密塔上是**两件事**，不是一件：

1. 图形连接（`ConnectObjects`）；
2. 在塔的 `MaterialStreams` 字典里写一条 `StreamInformation` 记录，带塔板号和行为
   （进料 / 塔顶 / 塔釜 / 侧线）。

求解器读的是第 2 项。DWSIM 自带的 `ConnectFeed()` 把两件事绑在一起做，于是：

- 端口一旦已经连上（流程图保存重载后就是常态），它直接抛
  *"The requested connection between the given objects cannot be done."*；
- 塔板列表没重建时，它抛 `IndexOutOfRange` —— 因为 `NumberOfStages` 只是个普通自动
  属性，改它并不会生成塔板对象，只有 `SetNumberOfStages(n)` 会。

现在 `dwsim_unitop_connect` 把两步拆开、图形连接按需跳过，因此调用是幂等的。

新增工具：

| 工具 | 作用 |
|---|---|
| `dwsim_column_set_stages` | 设置塔板数（调用 `SetNumberOfStages`）并为新建塔板初始化压力分布 |
| `dwsim_column_get_streams` | 诊断：塔真正认识的流股、行为、塔板号，外加塔板表 |
| `dwsim_column_set_spec` | 设置两个操作规格（`C` 冷凝器/回流，`R` 再沸器/塔釜） |
| `dwsim_column_set_feed_stage` | 把已连接的进料挪到别的塔板 |

### 流股组成

`dwsim_stream_add_material` 和 `dwsim_stream_set_conditions` 以前不管传什么都喂给
`SetCompoundMassFlow`。传摩尔分数（最自然的做法）会得到一条质量流量看着正常、
**摩尔流量为 0** 的流股，再往下传就成了零流量进料。

两个工具现在都有 `composition_basis` 参数：

| 取值 | 含义 |
|---|---|
| `mole_fraction`（默认） | 摩尔分数，自动归一化 |
| `mass_fraction` | 质量分数，自动归一化 |
| `molar` | 各组分摩尔流量，mol/s |
| `mass` | 各组分质量流量，kg/s |

## 构建与安装

需要 .NET 10 SDK，以及一个已构建好的 DWSIM 二进制目录（解压的 DWSIM 安装包，或之前
发布过的 `dwsim-mcp` 目录 —— 任何含 `DWSIM.Automation.dll` 的目录都行）。

```bash
git clone https://github.com/<你>/dwsim-mcp.git
cd dwsim-mcp

# Linux / macOS / Git Bash
./scripts/build.sh --dwsim-bin /path/to/dwsim/bin

# Windows PowerShell
.\scripts\build.ps1 -DwsimBinDir D:\DWSIM\dwsim-mcp
```

产物在 `dist/`，是**自包含**的，目标机器不需要装 .NET。整个 `dist` 目录拷走即可。

注册到 MCP 客户端（`~/.workbuddy/mcp.json` 或同类文件）：

```json
{
  "mcpServers": {
    "dwsim": {
      "command": "D:\\DWSIM\\dwsim-mcp\\dwsim-mcp.exe",
      "args": ["--stdio"]
    }
  }
}
```

验证：

```bash
python scripts/smoke_test.py dist/dwsim-mcp.exe
```

## 塔的正确配置顺序

```
1. dwsim_column_set_stages  stages=40 top_pressure=101325 pressure_drop_per_stage=200
2. dwsim_unitop_connect     feed_stream=FEED       feed_port=20      # feed_port 是塔板号
3. dwsim_unitop_connect     product_stream=DISTILLATE product_port=0
4. dwsim_unitop_connect     product_stream=BOTTOMS    product_port=1
5. dwsim_column_set_spec    spec_id=C stype=Stream_Ratio            value=3
6. dwsim_column_set_spec    spec_id=R stype=Product_Molar_Flow_Rate value=50 unit=mol/s
7. dwsim_column_get_streams                                          # 求解前核对
8. dwsim_solve_run
```

塔板号：`0` 冷凝器，`1` 最上层板，`n-1` 再沸器。`product_port >= 2` 注册侧线采出。

排错表见 [docs/rigorous-column.md](docs/rigorous-column.md)，完整脚本见
[examples/rigorous_column.py](examples/rigorous_column.py)。

## 贡献回上游

改动属于 DWSIM 本身。`patches/dwsim10-rigorous-column-mcp.patch` 可以直接打到源码树上：

```bash
git clone https://github.com/DanWBR/dwsim10.git
cd dwsim10
git am ../dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
```

[UPSTREAM_PR.md](UPSTREAM_PR.md) 是可直接粘贴的 PR 描述。

## 许可证

**GNU GPL v3.0**。这不是选择：DWSIM 是 GPL-3.0，本项目链接了它的程序集，属于衍生作品。

- 可以自由使用、修改、再分发，包括商用；
- 分发二进制时必须同时提供对应源码（指向本仓库的链接即可）；
- **不能**改成 MIT / Apache / BSD 等许可证；
- 你的 `.dwxmz` 模拟文件、工艺数据、计算结果不受 GPL 源码分发义务约束。

## 目录结构

```
src/                        MCP 服务器源码（与上游 tools/DWSIM.MCPServer 一一对应）
scripts/build.ps1           Windows 自包含构建
scripts/build.sh            Linux / macOS / Git Bash 自包含构建
scripts/smoke_test.py       对构建产物做端到端断言
patches/                    用于上游化的 git 补丁
docs/rigorous-column.md     塔配置与排错
examples/                   完整示例脚本
UPSTREAM_PR.md              PR 描述
```

## 致谢

DWSIM 由 Daniel Medeiros 及贡献者开发 —— <https://github.com/DanWBR/dwsim>。
本分支的塔与组成修复记录在 [CHANGELOG.md](CHANGELOG.md)。
