# Contributing

## Ground rules

1. **Everything here is GPL-3.0.** DWSIM is GPL-3.0 and this server links against it, so
   it is a derivative work. Contributions are accepted under GPL-3.0 and nothing else.
   Do not add MIT-, Apache- or BSD-licensed source files.

2. **The real home of this code is DWSIM.** This repository exists to make the server easy
   to build and distribute. Fixes should go upstream to
   [DanWBR/dwsim10](https://github.com/DanWBR/dwsim10) (`tools/DWSIM.MCPServer`) so
   everyone gets them. Land them upstream first when you can; mirror them here.

3. **No absolute paths, no machine-specific settings.** The project must build from a clean
   clone on somebody else's machine.

4. **No WinForms, no licensing, no Patreon-only builders.** The server deliberately sits
   only on the automation and fluent APIs. Keep it that way.

## Layout

```
src/                  mirrors upstream tools/DWSIM.MCPServer file for file
scripts/              build + smoke test
patches/              git patch for upstreaming
docs/                 configuration and troubleshooting notes
examples/             worked scripts
```

`src/` must stay a drop-in replacement for `tools/DWSIM.MCPServer`. If you add a file
there, add it in the same relative position upstream.

## Before you send a pull request

```bash
# 1. build the distributable
./scripts/build.sh --dwsim-bin /path/to/dwsim/bin

# 2. check it end to end
python scripts/smoke_test.py dist/dwsim-mcp.exe

# 3. check it still compiles in tree, the way upstream builds it
./scripts/build.sh --in-tree --dwsim-src ~/src/dwsim10

# 4. regenerate the upstream patch
cd ~/src/dwsim10
git diff -- tools/DWSIM.MCPServer/ > /path/to/dwsim-mcp/patches/dwsim10-rigorous-column-mcp.patch
```

Add an entry to `CHANGELOG.md` under `Unreleased`.

## Adding a tool

Tools are plain methods decorated with `[McpTool]`; the registry finds them by
reflection. There is no registration list to update.

```csharp
[McpTool("dwsim_column_set_stages", "One-line description that a language model will read.")]
public JObject SetColumnStages(
    [McpParam("Flowsheet handle")] string flowsheet_id,
    [McpParam("Column tag/name")] string column,
    [McpParam("Number of stages")] int stages)
{
    ...
}
```

Conventions:

- Parameters default to required; mark optional ones `Required = false` and give them a
  default value.
- Return a `JObject`. Throw `ArgumentException` for a bad argument and
  `InvalidOperationException` for a bad state — the dispatcher turns them into a JSON-RPC
  error with the message intact, and that message is what the model reads. Make it
  actionable: say what to call next.
- Prefer a diagnostic tool over a longer error message. `dwsim_column_get_streams` is the
  model to copy.
- Describe units in the parameter description (`Pa`, `K`, `mol/s`), never bare numbers.

## Coding style

- Four spaces, no tabs, braces on their own line.
- `dynamic` is acceptable when crossing into DWSIM's VB world where the types are not
  visible from C#, but keep it inside a small helper and document why.
- Reflection is acceptable for the same reason; keep it in the helper next to the code
  that needs it.
- Tests live in `scripts/smoke_test.py`. It must stay runnable against any build with
  nothing but Python 3.

## Publishing a release

The build output is far too large to put in git (~280 MB of .NET runtime plus the DWSIM
engine), so it goes on a GitHub Release instead.

1. Build for each platform you want to ship:

   ```bash
   ./scripts/build.sh --dwsim-bin /path/to/win/dwsim  -r win-x64   -o dist-win
   ./scripts/build.sh --dwsim-bin /path/to/linux/dwsim -r linux-x64 -o dist-linux
   ./scripts/build.sh --dwsim-bin /path/to/osx/dwsim   -r osx-arm64 -o dist-osx
   ```

2. Smoke-test each one against its own output directory.

3. Zip each directory (`dist-win.zip`, `dist-linux.tar.gz`, `dist-osx.tar.gz`) and attach
   them to the release.

4. Tag it: `git tag -a v1.0.0 -m "v1.0.0"` and push the tag.

5. In the release notes, say which DWSIM build the binaries were linked against, and link
   to this repository — that is the corresponding-source offer the GPL-3.0 asks for.

Do **not** commit `dist/` or `artifacts/`; `.gitignore` already excludes them.

## Reporting a bug

Include the DWSIM version, the exact tool call and the exact error text. For column
problems, include the output of `dwsim_column_get_streams` — it answers most of them.
