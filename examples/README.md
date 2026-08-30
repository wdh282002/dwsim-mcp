# Examples

| Script | What it shows |
|---|---|
| `rigorous_column.py` | The complete sequence for a 40-stage distillation column: stages first, then connections, then the two operating specifications, then a diagnostics dump before solving |

Run any of them against a built executable:

```bash
./scripts/build.sh --dwsim-bin /path/to/dwsim/bin
python examples/rigorous_column.py dist/dwsim-mcp.exe
```

`rigorous_column.py` is self-contained — it opens the server as a subprocess and speaks
JSON-RPC over stdio, so it needs nothing but Python 3. It is also the shortest working
reference for embedding a client: the `Mcp` class at the top is about fifty lines.

If you want a minimal check rather than a worked example, use
`scripts/smoke_test.py` — it asserts rather than prints.
