# dwsim-mcp

A [Model Context Protocol](https://modelcontextprotocol.io) server for
[DWSIM](https://dwsim.org), the open-source chemical process simulator. It exposes the
flowsheet, the streams, the unit operations, the thermodynamics and the solver as MCP
tools, so an MCP client can build, configure and solve a simulation end to end.

This repository is a **distribution and contribution vehicle** for the server that lives
upstream at `DanWBR/dwsim10` under `tools/DWSIM.MCPServer`. It carries the full source
plus build scripts that produce a self-contained, ready-to-run executable, and a patch
that applies the same changes to a DWSIM source checkout.

---

## What this fork adds

Rigorous columns (DistillationColumn / AbsorptionColumn) could not be driven through MCP
at all, and stream compositions were silently corrupted. Both are fixed here.

### Rigorous columns

Wiring a stream to a rigorous column is **two** things in DWSIM, not one:

1. the graphic connection (`ConnectObjects`), and
2. an entry in the column's `MaterialStreams` dictionary — a `StreamInformation` record
   carrying the stage and the behaviour (feed / distillate / bottoms / sidedraw).

DWSIM's own `ConnectFeed()` does both together, so it throws
*"The requested connection between the given objects cannot be done."* the moment the
ports are already wired — and it throws `IndexOutOfRange` when the stage list has not been
rebuilt. The server now does the two steps separately and idempotently.

On top of that, `NumberOfStages` is a plain auto-property in DWSIM: setting it does
**not** create the stage objects. Only `SetNumberOfStages(n)` does. Point 2 is invisible
until the solver complains about a pressure of `-498675 Pa`.

New tools:

| Tool | Purpose |
|---|---|
| `dwsim_column_set_stages` | Set the stage count (calls `SetNumberOfStages`) and write a pressure profile across every stage |
| `dwsim_column_get_streams` | Diagnostics: which streams the column actually knows about, with behaviour and stage index |
| `dwsim_column_set_spec` | Set the two operating specifications (`C` condenser/reflux, `R` reboiler/bottoms) |
| `dwsim_column_set_feed_stage` | Move an already connected feed to another stage |
| `dwsim_column_connect_vapor` | Connect the overhead **vapour** product. Required whenever the feed carries non-condensables; pair it with `CondenserType = Partial_Condenser` |

### Stream compositions

`dwsim_stream_add_material` and `dwsim_stream_set_conditions` fed every composition value
to `SetCompoundMassFlow`, whatever it meant. Passing mole fractions — the natural thing to
do — produced a stream with a sane mass flow and a molar flow of **zero**.

Both tools now take `composition_basis`:

| Value | Meaning |
|---|---|
| `mole_fraction` (default) | Mole fractions, normalised |
| `mass_fraction` | Mass fractions, normalised |
| `molar` | Per-compound molar flows in mol/s |
| `mass` | Per-compound mass flows in kg/s |

### Verified

`examples/rigorous_column.py` builds a 40-stage methanol/water column from nothing,
through MCP alone, and solves it:

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

DISTILLATE   T=337.76 K  n=50.000 mol/s     Methanol x=1.0000
BOTTOMS      T=373.13 K  n=50.000 mol/s     Water    x=1.0000
```

Before these changes the same script failed at the connection step with
*"One or more of the stream connections to the column is missing."*

---

## Install

### Option 1 — build it yourself

You need the .NET 10 SDK and a built DWSIM binary directory (an extracted DWSIM
installation, or a previously published `dwsim-mcp` folder — anything containing
`DWSIM.Automation.dll`).

```bash
git clone https://github.com/<you>/dwsim-mcp.git
cd dwsim-mcp

# Linux / macOS / Git Bash
./scripts/build.sh --dwsim-bin /path/to/dwsim/bin

# Windows PowerShell
.\scripts\build.ps1 -DwsimBinDir D:\DWSIM\dwsim-mcp
```

The result is a self-contained executable in `dist/` — no .NET runtime needed on the
target machine. Copy the whole `dist` directory somewhere and point your MCP client at
`dwsim-mcp.exe` (or `dwsim-mcp`).

### Option 2 — build inside a DWSIM source checkout

```bash
./scripts/build.sh --in-tree --dwsim-src ~/src/dwsim10
```

The script copies `src/` over `tools/DWSIM.MCPServer` in the checkout and builds it with
ordinary `ProjectReference`s, which is exactly how upstream builds it — the way to verify
that a change still compiles in tree before sending it upstream.

### Register with an MCP client

`~/.workbuddy/mcp.json`, `claude_desktop_config.json`, or the equivalent:

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

Then check it with the smoke test:

```bash
python scripts/smoke_test.py dist/dwsim-mcp.exe
```

It starts the server, lists the tools, builds a one-feed / one-heater flowsheet, solves it
and asserts the outlet temperature.

---

## Using it

### Configuring a rigorous column

Order matters — do the stages before the connections:

```
1. dwsim_column_set_stages  column=RD_COLUMN stages=40 top_pressure=500000 pressure_drop_per_stage=300
2. dwsim_unitop_connect     unitop=RD_COLUMN feed_stream=MEOH_FEED feed_port=6
3. dwsim_unitop_connect     unitop=RD_COLUMN product_stream=DISTILLATE product_port=0
4. dwsim_unitop_connect     unitop=RD_COLUMN product_stream=BOTTOMS    product_port=1
5. dwsim_column_set_spec    column=RD_COLUMN spec_id=C stype=Stream_Ratio             value=5
6. dwsim_column_set_spec    column=RD_COLUMN spec_id=R stype=Product_Molar_Flow_Rate  value=24.8 unit=mol/s
7. dwsim_column_get_streams column=RD_COLUMN      # verify before solving
8. dwsim_solve_run
```

Stage indices: `0` = condenser, `1 … n-2` = plates (1 is the top plate), `n-1` = reboiler.
A `product_port` of 2 or more registers a sidedraw on that stage.

See [docs/rigorous-column.md](docs/rigorous-column.md) for the troubleshooting table, and
[examples/](examples/) for a worked script.

### Error messages, decoded

| Message | Cause |
|---|---|
| `One or more of the stream connections to the column is missing` | Graphic connections exist but the `MaterialStreams` entries do not. Run `dwsim_column_get_streams`; an empty `streams` array confirms it |
| `Index was out of range` | The `Stages` list was never rebuilt — call `dwsim_column_set_stages` first |
| `The requested connection ... cannot be done` | Ports already wired. The current `dwsim_unitop_connect` skips the graphic step when it is, so this should not come back |
| `Tried to calculate equilibrium with invalid pressure: -xxx Pa` | `CondenserDeltaP` is absurd, or the top pressure is below `stages × pressure drop` |
| `Solver reached the maximum number of iterations` | A genuine convergence problem. The wiring is fine at this point — check the specs, non-condensables against the condenser type, and the property package |

---

## Contributing upstream

The changes belong in DWSIM itself. `patches/dwsim10-rigorous-column-mcp.patch` applies
them to a checkout:

```bash
git clone https://github.com/DanWBR/dwsim10.git
cd dwsim10
git am ../dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
# or: git apply ../dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
```

[UPSTREAM_PR.md](UPSTREAM_PR.md) is a ready-to-paste pull request description.

If you change the source here, regenerate the patch:

```bash
# from the DWSIM checkout, with the changes in the working tree
git diff -- tools/DWSIM.MCPServer/Tools/ > /path/to/dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
```

---

## License

**GNU General Public License v3.0.** This is not a choice: DWSIM is GPL-3.0 and this
project links against it, so it is a derivative work. See [LICENSE](LICENSE) and
[NOTICE](NOTICE).

Consequences worth knowing before you distribute it:

- You may use, modify and redistribute it freely, including commercially.
- If you distribute binaries, you must also offer the corresponding source — whether that
  is a link to this repository or a written offer.
- You may **not** relicense it as MIT, Apache-2.0, BSD or anything else.
- Recipients keep all GPL-3.0 rights, including the right to redistribute under the same
  terms.

The licensing obligation applies to this MCP server. It does **not** extend to your
simulation files (`.dwxmz`), your process data, or anything else your client sends
through it.

---

## Repository layout

```
src/                       the MCP server source (C#, mirrors tools/DWSIM.MCPServer)
scripts/build.ps1          Windows build, self-contained
scripts/build.sh           Linux / macOS / Git Bash build, self-contained
scripts/smoke_test.py      end-to-end check of a built executable
patches/                   git patch for upstreaming to DanWBR/dwsim10
docs/rigorous-column.md    column configuration and troubleshooting
examples/                  worked scripts
UPSTREAM_PR.md             pull request description
```

## Credits

DWSIM is by Daniel Medeiros and contributors — <https://github.com/DanWBR/dwsim>.
The rigorous-column and composition fixes in this fork are described in
[CHANGELOG.md](CHANGELOG.md).
