# 与上游 DWSIM MCP 的差异

**上游基线**：`DanWBR/dwsim10` @ `5dc43b6e35d015608546f7689e2a54f1ebd3c3cf`
（2026-08-28，"fix: ship the Python standard library and put it on the IronPython search path"）

**改动规模**：2 个文件，+459 / −20 行。

```
tools/DWSIM.MCPServer/Tools/UnitOps/UnitOpTools.cs   +409 / −15
tools/DWSIM.MCPServer/Tools/Streams/StreamTools.cs    +70  / −5
```

没有改动 DWSIM 引擎的任何一行，没有新增依赖，没有公开 API 破坏性变更。

---

## 一句话概括

上游的 MCP 服务器能建模普通单元操作，但**严密精馏塔完全用不了**，而且**流股组成会被静默写错**。
这两类问题都修了，另外补了 4 个塔专用工具。

---

## 功能对照表

| 能力 | 上游 `dwsim10@5dc43b6` | 本版本 |
|---|---|---|
| 常规单元操作（换热器、泵、混合器…）连接 | ✅ | ✅ 不变 |
| 严密塔：设置塔板数 | ❌ 只能改计数器，不生成塔板对象 | ✅ `dwsim_column_set_stages` |
| 严密塔：连接进料并指定塔板 | ❌ 报错 | ✅ `dwsim_unitop_connect` 的 `feed_port` = 塔板号 |
| 严密塔：连接塔顶/塔釜产品 | ❌ 报错 | ✅ `product_port` 0/1，`>=2` 为侧线 |
| 严密塔：诊断已注册的流股 | ❌ 无 | ✅ `dwsim_column_get_streams` |
| 严密塔：设置两个操作规格 | ❌ 无 | ✅ `dwsim_column_set_spec` |
| 严密塔：改进料板位置 | ⚠️ 存在但永远报"未连接"（按 Tag 匹配，实际应按对象 Name） | ✅ 已修复，缺失时自动建条目 |
| 流股组成：摩尔分数 | ❌ 被当成质量流量，导致摩尔流量=0 | ✅ `composition_basis=mole_fraction`（默认） |
| 流股组成：其他基准 | ❌ 无 | ✅ `mass_fraction` / `molar`(mol/s) / `mass`(kg/s) |
| 构建方式 | 只能在 DWSIM 源码树内用 `ProjectReference` | ✅ 双模式：树内 或 指向预编译 DWSIM 目录 |
| 工具总数 | 48 | **52** |

---

## 逐条说明

### 1. 严密塔（核心改造）

**上游的行为**

严密塔（`DistillationColumn` / `AbsorptionColumn`）的连接在 DWSIM 里是**两件独立的事**：

1. 图形连接 —— `Flowsheet.ConnectObjects()`
2. 在塔的 `MaterialStreams` 字典里写一条 `StreamInformation` 记录，携带 `AssociatedStage`（塔板）和 `StreamBehavior`（Feed / Distillate / BottomsLiquid / Sidedraw）

**求解器只读第 2 项。** 上游的 `dwsim_unitop_connect` 只做了第 1 项（对非塔单元足够了），
所以塔一直报：

```
RD_COLUMN: One or more of the stream connections to the column is missing.
```

即便画布上看起来已经连好了。

**为什么不能直接调 DWSIM 自带的 `ConnectFeed()`**

它把两件事绑成一个原子操作，于是：

- 端口一旦已连上（流程图保存重载后就是常态）→ 抛
  *"The requested connection between the given objects cannot be done."*
- 塔板列表没重建时 → 抛 `IndexOutOfRange`

而 `NumberOfStages` 在 DWSIM 里只是个普通自动属性，改它**不会**生成塔板对象；
只有 `SetNumberOfStages(n)` 会。

**本版本的做法**

`dwsim_unitop_connect` 把两步拆开：图形连接仅在未连接时执行，字典条目总是写入。
调用因此变成**幂等**的 —— 可以重复调用，也可以用来修复已存在的塔。

新增 4 个工具：

| 工具 | 作用 |
|---|---|
| `dwsim_column_set_stages` | 调 `SetNumberOfStages(n)`；并把新建塔板的压力从 0 Pa 初始化（否则求解发散） |
| `dwsim_column_get_streams` | 诊断：塔真正认识的流股、行为、塔板号 + 塔板表 |
| `dwsim_column_set_spec` | 设置 `C`（冷凝器/回流）与 `R`（再沸器/塔釜）两个操作规格 |
| `dwsim_column_set_feed_stage` | 修复：改按对象 `Name`（`MAT-…`）匹配，而非图形 Tag |

### 2. 流股组成（静默数据损坏）

**上游的行为**

```csharp
foreach (var prop in composition.Properties())
    builder.SetCompoundMassFlow(prop.Name, prop.Value.Value<double>());
```

参数文档写的是 `{compound: mass_fraction}`，但实现不管传什么都喂给 `SetCompoundMassFlow`。
传摩尔分数（最自然的做法）会得到一条**质量流量看着正常、摩尔流量精确为 0** 的流股，
再往下传就成了零流量进料 —— 这类 bug 不会报错，只会让结果莫名其妙。

**本版本的做法**

新增 `composition_basis` 参数，四种取值：`mole_fraction`（默认，归一化）、
`mass_fraction`（归一化）、`molar`（mol/s）、`mass`（kg/s）。

### 3. 构建方式（工程性改动）

上游的 `.csproj` 只支持在 DWSIM 源码树内构建，引用 11 个 `ProjectReference`。
本版本改成条件化的双模式：

- 不传 `DWSIM_BIN_DIR` → 保持上游的 `ProjectReference` 方式，**上游构建不受影响**
- 传 `-p:DWSIM_BIN_DIR=<目录>` → 改用 `Reference` + HintPath，指向任意已构建的 DWSIM 目录

这是让"发给别人直接用"成为可能的那个改动。

---

## 证据

**上游会失败、本版本成功的最小场景**（`examples/rigorous_column.py`，纯 MCP 从零搭建）：

```
stages:      {"number_of_stages": 40, "stage_entries": 40, "pressures_initialised": 28}
feed:        {"connections": ["feed:FEED->stage20(Stage20)"]}
distillate:  {"connections": ["product:DISTILLATE->distillate(Condenser)"]}
bottoms:     {"connections": ["product:BOTTOMS->bottoms(Reboiler)"]}

column sees:
  FEED         Feed           stage 20
  DISTILLATE   Distillate     stage 0
  BOTTOMS      BottomsLiquid  stage 39

solved: True

DISTILLATE   T=337.76 K  n=50.000 mol/s   Methanol x=1.0000
BOTTOMS      T=373.13 K  n=50.000 mol/s   Water    x=1.0000
```

**冒烟测试**（`scripts/smoke_test.py`，10 项全绿）：
52 个工具、5 个塔工具在位、摩尔分数组成正确设置流量、Heater 求解至 373.15 K。

---

## 关于"改造"

严格说这不是重写，是在上游架构内的**缺陷修复 + 能力补齐**：

- 没有替换任何架构（工具注册仍靠 `[McpTool]` 反射发现，传输层、会话层、RPC 层一行未动）
- 没有引入新依赖
- 没有触碰 DWSIM 引擎
- 所有新增代码都在 MCP 工具层，与上游既有的 `dynamic` / 反射风格保持一致

`dynamic` 和反射的使用是**必要的**：塔的 `StreamInformation`、`ColumnSpec`、`Stage`
这些类型定义在 VB 工程里，从 C# 侧编译时不可见。每个用到它们的地方都收在小helper里，
并注释了原因。

---

## 许可证

DWSIM 是 **GPL-3.0**，本项目链接其程序集，属于衍生作品，因此同样是 **GPL-3.0**。
这不是可选项：不能改成 MIT / Apache / BSD。分发二进制必须同时提供获取源码的途径
（指向本仓库的链接即可）。

受影响的是本 MCP 服务器本身。**不**包括你的 `.dwxmz` 模拟文件、工艺数据和计算结果。
