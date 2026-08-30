# Changelog

All notable changes to this fork of `tools/DWSIM.MCPServer` are recorded here.
The project follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — rigorous columns

- **`dwsim_column_connect_vapor`** — connect the overhead **vapour** product. Whenever the
  feed carries non-condensables (N₂, CO, O₂, NO …) a total condenser cannot condense them
  and the column cannot converge; pair this with `CondenserType = Partial_Condenser`.
  It uses DWSIM's output connector 9, the same one `ConnectVaporProduct()` does.

  A partial condenser needs **all three** outlets wired — overhead vapour, liquid
  distillate and bottoms. Connecting only the vapour one makes DWSIM report
  *"One or more of the stream connections to the column is missing."*

- **`dwsim_column_set_stages`** — set the stage count by calling DWSIM's
  `SetNumberOfStages(n)`, which is the only thing that actually creates the stage objects.
  Newly created stages have a pressure of 0 Pa, which makes the solver diverge with
  messages like `Tried to calculate equilibrium with invalid pressure: -498675 Pa`, so the
  tool also writes a pressure profile from `top_pressure` and `pressure_drop_per_stage`
  across every stage (see the fix below — writing only the zeroed ones was a bug).

- **`dwsim_column_get_streams`** — diagnostics. Lists the `StreamInformation` entries the
  column actually holds, with the resolved stream tag, behaviour, type and stage index,
  plus the stage table. This is the tool that turns
  *"One or more of the stream connections to the column is missing"* from a guess into a
  one-call answer.

- **`dwsim_column_set_spec`** — set either of the two operating specifications
  (`C` condenser/reflux, `R` reboiler/bottoms) with an explicit spec type
  (`Stream_Ratio`, `Product_Molar_Flow_Rate`, `Heat_Duty`, `Component_Recovery`,
  `Component_Fraction`, `Temperature`, `Product_Mass_Flow_Rate`, `Feed_Recovery`, …).

- **`dwsim_column_set_stages` / `dwsim_column_get_streams` / `dwsim_column_set_spec` /
  `dwsim_column_set_feed_stage`** are also exposed through the tool registry, taking the
  tool count from 48 to 52.

### Fixed — rigorous columns

- **`dwsim_unitop_connect` no longer fails on an already wired column.** Wiring a stream
  to a rigorous column is two separate operations in DWSIM: the graphic connection and a
  `StreamInformation` entry in the column's `MaterialStreams` dictionary that carries the
  stage and behaviour. DWSIM's own `ConnectFeed()` does both together and throws
  *"The requested connection between the given objects cannot be done."* when the ports are
  already attached. The server now performs the graphic step only when needed, and always
  writes the dictionary entry, which makes the call idempotent.

- **`dwsim_unitop_connect` now handles distillate, bottoms and sidedraws.** Product port
  `0` registers the distillate at the condenser, port `1` the bottoms at the reboiler, and
  port `n >= 2` a sidedraw on stage `n`.

- **`dwsim_column_set_feed_stage` matched on the wrong key.** It compared the stream tag
  against `StreamInformation.StreamID`, but DWSIM keys those records by the simulation
  object's `Name` (`MAT-…`), not its graphic tag, so the lookup never found anything and
  always raised *"Stream 'X' is not connected to column 'Y'"*. It now resolves the object
  first, and creates the record instead of failing when it is missing.

### Fixed — rigorous columns

- **`dwsim_column_set_stages` produced a broken pressure profile.** It only wrote a
  pressure onto stages still at 0 Pa. `SetNumberOfStages` appends stages, so the ones that
  already existed kept the 101325 Pa DWSIM default while the new ones got the requested
  pressure — a profile that jumps from 1 atm to, say, 5 bar partway down the column.

  The bug is invisible at atmospheric pressure (the jump is 101325 → 101125 Pa), so a
  1 atm column solves fine while a 5 bar one fails with
  `Failed to fulfill mass balance for Methanol: Relative Error = 0.27`. When it did
  converge, the column came out inverted — the condenser hotter than the reboiler.

  The tool now rewrites the pressure of **every** stage when `top_pressure` is given, and
  reports `pressures_rewritten`, `top_pressure_Pa` and `bottom_pressure_Pa` so the profile
  can be checked. Verified by bisection: with the fix, a 40-stage methanol/water column
  converges at 2, 3 and 5 bar; before it, only at 1 atm.

### Fixed — streams

- **Compositions are no longer silently reinterpreted as mass flows.**
  `dwsim_stream_add_material` and `dwsim_stream_set_conditions` passed every composition
  value to `SetCompoundMassFlow`, whatever it meant. Passing mole fractions — the natural
  thing to do — produced a stream with a plausible mass flow and a molar flow of zero,
  which then propagated into any downstream unit as a zero-flow feed. Both tools now take
  a `composition_basis` parameter: `mole_fraction` (default), `mass_fraction`, `molar`
  (mol/s) or `mass` (kg/s).

### Changed

- `src/DWSIM.MCPServer.csproj` builds in two modes: plain `ProjectReference`s when built
  inside a DWSIM source tree, or `Reference` hint paths against a prebuilt DWSIM binary
  directory when `DWSIM_BIN_DIR` is supplied. The upstream project file only supported the
  first.

## [Baseline]

The state of `tools/DWSIM.MCPServer` in `DanWBR/dwsim10` before these changes:
48 tools, no dedicated column tooling, stream compositions always treated as mass flows.
