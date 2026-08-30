"""
Worked example: build and solve a 40-stage rigorous distillation column through MCP.

This is the sequence that works. Two things in it are not obvious and both bite:

  1. dwsim_column_set_stages must run BEFORE any connection. Setting NumberOfStages
     through dwsim_unitop_set only moves a counter; it does not create the stage
     objects, and every later call then fails with IndexOutOfRange.

  2. A rigorous column needs two operating specifications. Without them it cannot
     converge, no matter how clean the wiring is.

Run it against a built executable:

    python examples/rigorous_column.py dist/dwsim-mcp.exe

It writes the result to ./column_example.dwxmz.
"""
import json
import os
import queue
import subprocess
import sys
import threading
import time


class Mcp:
    """Minimal stdio JSON-RPC client for a dwsim-mcp subprocess."""

    def __init__(self, exe):
        self.proc = subprocess.Popen(
            [exe, "--stdio"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, cwd=os.path.dirname(os.path.abspath(exe)))
        self.q = queue.Queue()
        threading.Thread(
            target=lambda: [self.q.put(l.decode("utf-8", "replace").strip())
                            for l in self.proc.stdout],
            daemon=True).start()
        self._id = 0
        self._send("initialize",
                   {"protocolVersion": "2024-11-05", "capabilities": {},
                    "clientInfo": {"name": "example", "version": "1"}})
        self._send("notifications/initialized", notify=True)

    def _send(self, method, params=None, notify=False, timeout=120):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if notify:
            self.proc.stdin.write((json.dumps(msg) + "\n").encode())
            self.proc.stdin.flush()
            return None
        self._id += 1
        msg["id"] = self._id
        self.proc.stdin.write((json.dumps(msg) + "\n").encode())
        self.proc.stdin.flush()
        t0 = time.time()
        while time.time() - t0 < timeout:
            try:
                line = self.q.get(timeout=timeout)
            except queue.Empty:
                return {"TIMEOUT": True}
            try:
                d = json.loads(line)
            except Exception:
                continue
            if d.get("id") == self._id:
                return d
        return {"TIMEOUT": True}

    def call(self, tool, args=None, timeout=900):
        r = self._send("tools/call", {"name": tool, "arguments": args or {}},
                       timeout=timeout)
        if isinstance(r, dict) and r.get("TIMEOUT"):
            return {"error": "TIMEOUT", "tool": tool}
        if "error" in r:
            return {"error": r["error"]}
        c = r.get("result", {}).get("content", [])
        txt = c[0]["text"] if c else json.dumps(r)
        try:
            return json.loads(txt)
        except Exception:
            return txt

    def close(self):
        try:
            self.proc.stdin.close()
        except Exception:
            pass
        self.proc.terminate()


def show(label, value):
    print("%-28s %s" % (label + ":", json.dumps(value, ensure_ascii=False)[:300]))


def main(exe):
    mcp = Mcp(exe)

    fid = mcp.call("dwsim_flowsheet_create", {"name": "column_example"})["flowsheet_id"]
    print("flowsheet: " + fid)

    mcp.call("dwsim_thermo_add_compounds",
             {"flowsheet_id": fid, "names": ["Methanol", "Water"]})
    mcp.call("dwsim_thermo_set_property_package", {"flowsheet_id": fid, "name": "NRTL"})

    # --- feeds -----------------------------------------------------------------
    # composition_basis defaults to mole_fraction; without it these values used to
    # be read as mass flows and the stream ended up with zero molar flow.
    mcp.call("dwsim_stream_add_material", {
        "flowsheet_id": fid, "name": "FEED",
        "temperature_K": 320.0, "pressure_Pa": 101325.0,
        "molar_flow_mol_s": 100.0,
        "composition": {"Methanol": 0.5, "Water": 0.5},
        "composition_basis": "mole_fraction"})
    mcp.call("dwsim_stream_add_material", {"flowsheet_id": fid, "name": "DISTILLATE"})
    mcp.call("dwsim_stream_add_material", {"flowsheet_id": fid, "name": "BOTTOMS"})
    mcp.call("dwsim_unitop_add",
             {"flowsheet_id": fid, "type": "DistillationColumn", "name": "COLUMN"})

    # --- 1. stages FIRST -------------------------------------------------------
    show("stages", mcp.call("dwsim_column_set_stages", {
        "flowsheet_id": fid, "column": "COLUMN", "stages": 40,
        "top_pressure": 101325.0, "pressure_drop_per_stage": 200.0}))

    # --- 2. connections --------------------------------------------------------
    # feed_port is a stage index: 20 is the middle of a 40-stage column.
    show("feed", mcp.call("dwsim_unitop_connect", {
        "flowsheet_id": fid, "unitop": "COLUMN",
        "feed_stream": "FEED", "feed_port": 20}))
    show("distillate", mcp.call("dwsim_unitop_connect", {
        "flowsheet_id": fid, "unitop": "COLUMN",
        "product_stream": "DISTILLATE", "product_port": 0}))
    show("bottoms", mcp.call("dwsim_unitop_connect", {
        "flowsheet_id": fid, "unitop": "COLUMN",
        "product_stream": "BOTTOMS", "product_port": 1}))

    # --- 3. two operating specifications --------------------------------------
    show("spec C", mcp.call("dwsim_column_set_spec", {
        "flowsheet_id": fid, "column": "COLUMN", "spec_id": "C",
        "stype": "Stream_Ratio", "value": 3.0}))
    show("spec R", mcp.call("dwsim_column_set_spec", {
        "flowsheet_id": fid, "column": "COLUMN", "spec_id": "R",
        "stype": "Product_Molar_Flow_Rate", "value": 50.0, "unit": "mol/s"}))

    # --- 4. verify before solving ---------------------------------------------
    diag = mcp.call("dwsim_column_get_streams", {"flowsheet_id": fid, "column": "COLUMN"})
    print("\ncolumn sees:")
    for s in diag.get("streams", []):
        print("  %-12s %-14s stage %s" % (s["stream"], s["behavior"], s["stage_index"]))

    # --- 5. solve --------------------------------------------------------------
    sol = mcp.call("dwsim_solve_run", {"flowsheet_id": fid})
    print("\nsolved: " + str(sol.get("ok")))
    if not sol.get("ok"):
        print(json.dumps(sol, ensure_ascii=False)[:1200])
        mcp.close()
        return 1

    for name in ["DISTILLATE", "BOTTOMS"]:
        r = mcp.call("dwsim_stream_get_results", {"flowsheet_id": fid, "name": name})
        phases = r.get("phases") or [{}]
        comps = phases[0].get("compounds", {})
        top = sorted(comps.items(), key=lambda kv: -kv[1].get("mole_fraction", 0))[:3]
        print("\n%-12s T=%.2f K  n=%.3f mol/s" % (
            name, r.get("temperature_K", 0), r.get("molar_flow_mol_s", 0)))
        for c, v in top:
            print("   %-12s x=%.4f" % (c, v.get("mole_fraction", 0)))

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "column_example.dwxmz")
    mcp.call("dwsim_flowsheet_save",
             {"flowsheet_id": fid, "filepath": out, "compressed": True})
    print("\nsaved: " + out)
    mcp.close()
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1]))
