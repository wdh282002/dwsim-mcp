#!/usr/bin/env bash
#
# Builds dwsim-mcp (the DWSIM Model Context Protocol server) as a self-contained
# executable.
#
# Two modes:
#
#   A. Standalone (default) - build against an already-built DWSIM binary directory:
#
#          ./scripts/build.sh --dwsim-bin /opt/dwsim
#
#      The directory must contain DWSIM.Automation.dll and friends. An extracted
#      DWSIM installation or a previously published dwsim-mcp folder both work.
#
#   B. In-tree - run against a DWSIM source checkout:
#
#          ./scripts/build.sh --in-tree --dwsim-src ~/src/dwsim10
#
# Options:
#   -c, --configuration <cfg>   Release (default) or Debug
#   -r, --runtime <rid>         win-x64 (default), linux-x64, osx-arm64 ...
#   -o, --out <dir>             output directory, default ./dist
#   -h, --help
#
set -euo pipefail

# MSBuild only understands native Windows paths, so convert when running under
# MSYS2 / Git Bash on Windows.
winpath() {
    if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi
}

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/DWSIM.MCPServer.csproj"

DWSIM_BIN="${DWSIM_BIN_DIR:-}"
DWSIM_SRC="${DWSIM_SRC:-}"
IN_TREE=0
CONFIGURATION="Release"
RUNTIME="win-x64"
OUT_DIR="$REPO_ROOT/dist"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dwsim-bin)     DWSIM_BIN="$2"; shift 2 ;;
        --dwsim-src)     DWSIM_SRC="$2"; shift 2 ;;
        --in-tree)       IN_TREE=1; shift ;;
        -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
        -r|--runtime)    RUNTIME="$2"; shift 2 ;;
        -o|--out)        OUT_DIR="$2"; shift 2 ;;
        -h|--help)       sed -n '2,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

DOTNET="${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}"
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
if [[ -z "$DOTNET" ]]; then
    echo "error: dotnet SDK not found. Install the .NET 10 SDK or set DOTNET_ROOT." >&2
    exit 1
fi

[[ -f "$PROJECT" ]] || { echo "error: project not found: $PROJECT" >&2; exit 1; }

# Resolve to an absolute path: the runtime-data copy below runs from a different
# working directory, where a relative --out would point somewhere else entirely.
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

if [[ "$IN_TREE" -eq 1 ]]; then
    [[ -n "$DWSIM_SRC" ]] || { echo "error: --in-tree needs --dwsim-src" >&2; exit 1; }
    [[ -d "$DWSIM_SRC/tools" ]] || { echo "error: not a DWSIM source tree: $DWSIM_SRC" >&2; exit 1; }

    TARGET="$DWSIM_SRC/tools/DWSIM.MCPServer"
    echo "Building in-tree at $TARGET"
    mkdir -p "$TARGET"
    cp -r "$REPO_ROOT/src/." "$TARGET/"
    ( cd "$TARGET" && "$DOTNET" build -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true )
    cp -r "$TARGET/bin/$CONFIGURATION/net10.0/$RUNTIME/." "$OUT_DIR/"
else
    [[ -n "$DWSIM_BIN" ]] || { echo "error: pass --dwsim-bin or set DWSIM_BIN_DIR" >&2; exit 1; }
    [[ -f "$DWSIM_BIN/DWSIM.Automation.dll" ]] || {
        echo "error: DWSIM.Automation.dll not found in $DWSIM_BIN" >&2; exit 1; }

    # $1 = suffix for the scratch directories, so a retry can start from a clean slate.
    run_publish() {
        "$DOTNET" publish "$(winpath "$PROJECT")" \
            -c "$CONFIGURATION" -r "$RUNTIME" --self-contained true \
            -p:DWSIM_BIN_DIR="$(winpath "$DWSIM_BIN")" \
            -p:BaseIntermediateOutputPath="$(winpath "$REPO_ROOT/artifacts/obj$1")/" \
            -p:BaseOutputPath="$(winpath "$REPO_ROOT/artifacts/bin$1")/" \
            -p:PublishDir="$(winpath "$OUT_DIR")/"
    }

    echo "Publishing (self-contained $RUNTIME) ..."
    # Antivirus scanners and stale MSBuild nodes sometimes hold files in the scratch
    # directories, which fails the build. Retry once with pristine ones.
    if ! run_publish ""; then
        echo "publish failed - retrying with clean scratch directories"
        run_publish "-$(date +%s)" || { echo "error: publish failed" >&2; exit 1; }
    fi

    # The engine needs its data files next to the assemblies: compound databases
    # (addcomps), localised strings (de/en/es/it) and the CoolProp native library.
    echo "Copying DWSIM runtime data from $DWSIM_BIN"
    ( cd "$DWSIM_BIN" && find . -type f \
        ! -name '*.pdb' ! -name '*.xml' ! -name 'dwsim-mcp.*' \
        -exec cp --parents {} "$OUT_DIR"/ \; )
fi

EXE="$OUT_DIR/dwsim-mcp.exe"
[[ -f "$EXE" ]] || EXE="$OUT_DIR/dwsim-mcp"
[[ -f "$EXE" ]] || { echo "error: build produced no executable in $OUT_DIR" >&2; exit 1; }

# Show a native path in the instructions: on Windows a client cannot use an MSYS one.
SHOWN_EXE="$(winpath "$EXE")"

cat <<EOF

Built: $SHOWN_EXE

Register it with your MCP client, for example in ~/.workbuddy/mcp.json:

  {
    "mcpServers": {
      "dwsim": {
        "command": "$SHOWN_EXE",
        "args": ["--stdio"]
      }
    }
  }

Smoke test:
  python3 scripts/smoke_test.py "$SHOWN_EXE"
EOF
