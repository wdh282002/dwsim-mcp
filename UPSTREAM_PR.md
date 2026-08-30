# Upstream pull request

Ready-to-paste description for a PR against
<https://github.com/DanWBR/dwsim10> touching `tools/DWSIM.MCPServer`.

---

**Title**

```
MCP: drive rigorous columns, and stop corrupting stream compositions
```

**Body**

```
Rigorous columns could not be driven through the MCP server at all, and stream
compositions were silently reinterpreted. Both are fixed here. Two files change;
no engine project is touched.

## 1. Rigorous columns need two things, not one

Wiring a stream to a DistillationColumn or AbsorptionColumn is two operations in
DWSIM:

  1. the graphic connection (Flowsheet.ConnectObjects), and
  2. a StreamInformation record in the column's MaterialStreams dictionary, which
     carries the stage and the behaviour (feed / distillate / bottoms / sidedraw).

Item 2 is what the solver reads. ConnectFeed() does both together, so:

  * it throws "The requested connection between the given objects cannot be done."
    the moment the ports are already wired - which is the normal case once a
    flowsheet has been saved and reloaded;

  * it throws IndexOutOfRange when the stage list has not been rebuilt, because
    NumberOfStages is a plain auto-property: setting it does not create the stage
    objects, only SetNumberOfStages(n) does.

dwsim_unitop_connect now performs the graphic step only when it is needed and
always writes the dictionary entry, which makes the call idempotent. It also
handles distillate (port 0), bottoms (port 1) and sidedraws (port >= 2).

The StreamInformation key is the simulation object's Name (MAT-...), not its
graphic tag; dwsim_column_set_feed_stage was matching on the tag and therefore
never found anything.

New tools:

  * dwsim_column_set_stages  - SetNumberOfStages(n) plus a pressure profile, since
                               freshly created stages sit at 0 Pa and make the
                               solver diverge with messages such as
                               "Tried to calculate equilibrium with invalid
                               pressure: -498675 Pa"
  * dwsim_column_get_streams - diagnostics: the records the column actually holds,
                               with behaviour and stage index
  * dwsim_column_set_spec    - the two operating specifications (C and R)

Tool count goes from 48 to 51.

## 2. Compositions were always treated as mass flows

dwsim_stream_add_material and dwsim_stream_set_conditions passed every
composition value to SetCompoundMassFlow, whatever it meant. Passing mole
fractions - the natural thing to do - produced a stream with a plausible mass
flow and a molar flow of exactly zero, which then propagated downstream as a
zero-flow feed. Both tools now take a composition_basis parameter:
mole_fraction (default), mass_fraction, molar (mol/s) or mass (kg/s).

## Verification

  * 40-stage DistillationColumn with two feeds, a distillate and a bottoms:
    all four streams register with the correct behaviour and stage index
    (dwsim_column_get_streams), and the solver moves past the connection error
    it used to raise.
  * scripts/smoke_test.py: feed -> heater -> product, water, 1 mol/s specified
    as a mole fraction, solved, outlet within 0.5 K of the setpoint.

## Compatibility

No public API changes, no engine changes, no new dependencies. The two changed
files are the only ones touched.
```

---

## How to open it

```bash
git clone https://github.com/DanWBR/dwsim10.git
cd dwsim10
git checkout -b mcp-rigorous-columns
git am /path/to/dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
git push origin mcp-rigorous-columns
# then open the PR on GitHub with the text above
```

If the patch no longer applies because upstream moved, apply it by hand — the two
files are `Tools/UnitOps/UnitOpTools.cs` and `Tools/Streams/StreamTools.cs`, and
every change is a self-contained block.

## Expectations

Upstream review can take a while, and the maintainer may ask for changes — in
particular, he may prefer the column logic to live in the fluent API rather than
in the MCP tool layer, which would be a fair request. Either way, apply the patch
to your own checkout in the meantime; that is what this repository's build
scripts are for.
