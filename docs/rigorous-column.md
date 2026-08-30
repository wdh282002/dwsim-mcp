# Driving a rigorous column through MCP

Applies to `DistillationColumn` and `AbsorptionColumn`. Both derive from DWSIM's
`RigorousColumn`, and both are wired up differently from every other unit operation.

## Why columns are special

For an ordinary unit operation, `uo.ConnectFeedMaterialStream(stream, port)` is enough.
For a rigorous column it is not, because the column reads its connections from a
dictionary, not from the graphic ports. Two separate things have to happen:

1. **Graphic connection** — `Flowsheet.ConnectObjects(stream.GraphicObject,
   column.GraphicObject, 0, portIndex)`. This is what you see on the canvas.

2. **`StreamInformation` record** — an entry in the column's `MaterialStreams`
   dictionary, keyed by the stream object's `Name` (`MAT-…`, *not* its tag), carrying:
   - `StreamBehavior` — `Feed`, `Distillate`, `BottomsLiquid`, `Sidedraw`, `OverheadVapor`
   - `AssociatedStage` — the stage name or ID
   - `StreamType` — `Material` or `Energy`

Symptoms of getting this wrong:

- Only step 1 done → the solver says *"One or more of the stream connections to the column
  is missing"* even though the canvas looks perfectly wired.
- Only step 2 done → the column solves but the streams never update.

DWSIM's own `ConnectFeed()` does both, which is why the GUI never shows the problem. But
it does them as one atomic operation and throws if the ports are already attached — so it
cannot be used to repair or re-specify an existing column. The MCP server does the two
steps separately and skips step 1 when it is already done, which makes the call idempotent.

## Stage numbering

`Stages` is a list whose length equals `NumberOfStages`. For a distillation column:

| Index | Meaning |
|---|---|
| `0` | condenser |
| `1` | top plate |
| `2 … n-3` | intermediate plates |
| `n-2` | bottom plate |
| `n-1` | reboiler |

`dwsim_column_get_streams` prints this table with names and IDs, which is the safest way
to confirm what you are aiming at.

## The sequence

```
1. dwsim_column_set_stages
       column, stages, top_pressure, pressure_drop_per_stage
   Must come first. NumberOfStages is a plain auto-property in DWSIM - setting it with
   dwsim_unitop_set does NOT create the stage objects. Only SetNumberOfStages does, which
   is what this tool calls. Newly created stages sit at 0 Pa.

2. dwsim_unitop_connect  feed_stream=<tag>  feed_port=<stage index>
   feed_port is the stage index, not a port number: 1 is the top plate.

3. dwsim_unitop_connect  product_stream=DISTILLATE  product_port=0
   dwsim_unitop_connect  product_stream=BOTTOMS     product_port=1
   product_port >= 2 registers a sidedraw on that stage.

4. dwsim_column_set_spec  spec_id=C  stype=Stream_Ratio             value=<reflux ratio>
   dwsim_column_set_spec  spec_id=R  stype=Product_Molar_Flow_Rate  value=<bottoms> unit=mol/s
   A rigorous column needs exactly two specifications; it will not converge without them.

5. dwsim_column_get_streams   verify before solving

6. dwsim_solve_run
```

### Spec types

`Heat_Duty`, `Product_Molar_Flow_Rate`, `Component_Molar_Flow_Rate`,
`Product_Mass_Flow_Rate`, `Component_Mass_Flow_Rate`, `Component_Fraction`,
`Component_Recovery`, `Stream_Ratio`, `Temperature`, `Feed_Recovery`.

For `Component_Recovery` and `Component_Fraction`, also pass `component` (the compound
name) and, where it applies, `stage`.

## Troubleshooting

| Message | Cause | Fix |
|---|---|---|
| `One or more of the stream connections to the column is missing` | Graphic connections exist, `MaterialStreams` does not | Call `dwsim_column_get_streams`. An empty `streams` array confirms it. Re-run the `dwsim_unitop_connect` calls |
| `Index was out of range. Must be non-negative and less than the size of the collection` | The stage list was never rebuilt | `dwsim_column_set_stages` first |
| `The requested connection between the given objects cannot be done.` | A connection helper tried to redo an existing graphic connection | Use the current `dwsim_unitop_connect`, which skips it |
| `Column needs to be (re)initialized` | `Stages` contains entries with no accumulation stream — a symptom of the same rebuild problem | `dwsim_column_set_stages` |
| `Tried to calculate equilibrium with invalid pressure: -498675 Pa` | New stages at 0 Pa, or `CondenserDeltaP` set absurdly high (6 bar shows up as exactly this) | `dwsim_column_set_stages` with a real `top_pressure`; check `CondenserDeltaP` |
| `Solver reached the maximum number of iterations without converging` | Not a wiring problem | Check the two specs, the property package, and whether non-condensables are compatible with the condenser type |

## Physical sanity checks

These are not MCP problems, but they are what usually blocks a column after the wiring is
right:

- **Non-condensables plus a total condenser.** A feed carrying N₂, CO or O₂ will not
  condense. Use a partial condenser or a vapour distillate, or remove the inerts.
- **A `DistillationColumn` has no reaction kinetics.** "Reactive distillation" needs
  reaction sets on the stages (or a different unit operation); a plain rigorous column
  will only separate what you feed it.
- **Top pressure versus total pressure drop.** `top_pressure` must exceed
  `stages × pressure_drop_per_stage`, or the bottom of the column ends up under vacuum.
