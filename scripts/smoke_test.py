"""
Smoke test for a built dwsim-mcp: start it, list tools, assert the column tools are there,
build a tiny flowsheet (feed -> heater -> product), solve it and read the results.

Usage:  python scripts/smoke_test.py [path-to-dwsim-mcp.exe]
"""
import json, os, queue, subprocess, sys, threading, time


def main(exe):
    if not os.path.isfile(exe):
        print("FAIL: exe not found: " + exe)
        return 1

    proc = subprocess.Popen([exe, "--stdio"], stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                            cwd=os.path.dirname(exe))
    q = queue.Queue()
    threading.Thread(
        target=lambda: [q.put(l.decode("utf-8", "replace").strip()) for l in proc.stdout],
        daemon=True).start()

    _id = [0]

    def send(method, params=None, notify=False, timeout=60):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if notify:
            proc.stdin.write((json.dumps(msg) + "\n").encode())
            proc.stdin.flush()
            return None
        _id[0] += 1
        msg["id"] = _id[0]
        proc.stdin.write((json.dumps(msg) + "\n").encode())
        proc.stdin.flush()
        t0 = time.time()
        while time.time() - t0 < timeout:
            try:
                line = q.get(timeout=timeout)
            except queue.Empty:
                return {"TIMEOUT": True}
            try:
                d = json.loads(line)
            except Exception:
                continue
            if d.get("id") == _id[0]:
                return d
        return {"TIMEOUT": True}

    def call(tool, args, timeout=300):
        r = send("tools/call", {"name": tool, "arguments": args}, timeout=timeout)
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

    failures = []

    def check(label, ok, detail=""):
        print(("PASS  " if ok else "FAIL  ") + label + ("  " + detail if detail else ""))
        if not ok:
            failures.append(label)

    send("initialize", {"protocolVersion": "2024-11-05", "capabilities": {},
                        "clientInfo": {"name": "smoke", "version": "1"}}, timeout=60)
    send("notifications/initialized", notify=True)

    r = send("tools/list", {}, timeout=60)
    names = [t["name"] for t in r.get("result", {}).get("tools", [])]
    check("tools/list returns tools", len(names) > 0, "count=%d" % len(names))
    for t in ["dwsim_unitop_connect", "dwsim_column_set_stages",
              "dwsim_column_get_streams", "dwsim_column_set_spec",
              "dwsim_column_set_feed_stage"]:
        check("tool present: " + t, t in names)

    fs = call("dwsim_flowsheet_create", {"name": "smoke"}, timeout=120)
    fid = fs.get("flowsheet_id") if isinstance(fs, dict) else None
    check("flowsheet created", bool(fid), str(fid))
    if not fid:
        print(json.dumps(fs, ensure_ascii=False)[:400])
        proc.terminate()
        return 1

    call("dwsim_thermo_add_compounds", {"flowsheet_id": fid, "names": ["Water"]},
         timeout=120)
    call("dwsim_thermo_set_property_package", {"flowsheet_id": fid, "name": "NRTL"},
         timeout=120)

    call("dwsim_stream_add_material", {
        "flowsheet_id": fid, "name": "FEED",
        "temperature_K": 298.15, "pressure_Pa": 101325.0,
        "molar_flow_mol_s": 1.0,
        "composition": {"Water": 1.0},
        "composition_basis": "mole_fraction"}, timeout=120)

    res = call("dwsim_stream_get_results", {"flowsheet_id": fid, "name": "FEED"}, timeout=120)
    n = res.get("molar_flow_mol_s", 0.0) if isinstance(res, dict) else 0.0
    check("mole-fraction composition sets the flow", abs(n - 1.0) < 1e-6, "n=%.6f mol/s" % n)

    call("dwsim_stream_add_material", {"flowsheet_id": fid, "name": "PROD"}, timeout=120)
    call("dwsim_unitop_add", {"flowsheet_id": fid, "type": "Heater", "name": "H1"}, timeout=120)
    call("dwsim_unitop_connect", {"flowsheet_id": fid, "unitop": "H1",
                                  "feed_stream": "FEED", "feed_port": 0,
                                  "product_stream": "PROD", "product_port": 0}, timeout=120)
    call("dwsim_unitop_set", {"flowsheet_id": fid, "name": "H1",
                              "properties": {"CalcMode": "OutletTemperature"}}, timeout=120)
    call("dwsim_unitop_set", {"flowsheet_id": fid, "name": "H1",
                              "properties": {"OutletTemperature": 373.15}}, timeout=120)

    sol = call("dwsim_solve_run", {"flowsheet_id": fid}, timeout=600)
    ok = bool(sol.get("ok")) if isinstance(sol, dict) else False
    check("flowsheet solves", ok, "" if ok else json.dumps(sol, ensure_ascii=False)[:300])

    if ok:
        pr = call("dwsim_stream_get_results", {"flowsheet_id": fid, "name": "PROD"}, timeout=120)
        t = pr.get("temperature_K", 0.0) if isinstance(pr, dict) else 0.0
        check("heater outlet at 373.15 K", abs(t - 373.15) < 0.5, "T=%.2f K" % t)

    # --- rigorous column at a NON-atmospheric pressure ----------------------
    # Regression test. dwsim_column_set_stages used to initialise only the stages whose
    # pressure was still zero, leaving the pre-existing ones at the 101325 Pa DWSIM
    # default. At 1 atm the jump is invisible, so an atmospheric-only test passes while
    # every other pressure produces a profile that splits in half and diverges.
    fid2 = call("dwsim_flowsheet_create", {"name": "smoke_column"}, timeout=120).get("flowsheet_id")
    if fid2:
        call("dwsim_thermo_add_compounds", {"flowsheet_id": fid2,
                                            "names": ["Methanol", "Water"]}, timeout=120)
        call("dwsim_thermo_set_property_package", {"flowsheet_id": fid2, "name": "NRTL"},
             timeout=120)
        call("dwsim_unitop_add", {"flowsheet_id": fid2, "type": "DistillationColumn",
                                  "name": "COL"}, timeout=120)

        st = call("dwsim_column_set_stages", {
            "flowsheet_id": fid2, "column": "COL", "stages": 40,
            "top_pressure": 500000.0, "pressure_drop_per_stage": 300.0}, timeout=180)
        check("column stages created", st.get("stage_entries") == 40,
              "stage_entries=%s" % st.get("stage_entries"))
        check("whole pressure profile rewritten", st.get("pressures_rewritten") == 40,
              "pressures_rewritten=%s" % st.get("pressures_rewritten"))
        check("bottom pressure follows the profile",
              abs((st.get("bottom_pressure_Pa") or 0) - (500000.0 - 39 * 300.0)) < 1.0,
              "bottom=%.0f Pa" % (st.get("bottom_pressure_Pa") or 0))

        call("dwsim_unitop_set", {"flowsheet_id": fid2, "name": "COL",
                                  "properties": {"CondenserType": "Total_Condenser"}}, timeout=120)
        call("dwsim_stream_add_material", {
            "flowsheet_id": fid2, "name": "FEED",
            "temperature_K": 320.0, "pressure_Pa": 500000.0, "molar_flow_mol_s": 100.0,
            "composition": {"Methanol": 0.5, "Water": 0.5},
            "composition_basis": "mole_fraction"}, timeout=120)
        call("dwsim_stream_add_material", {"flowsheet_id": fid2, "name": "DIST"}, timeout=120)
        call("dwsim_stream_add_material", {"flowsheet_id": fid2, "name": "BOT"}, timeout=120)
        call("dwsim_unitop_connect", {"flowsheet_id": fid2, "unitop": "COL",
                                      "feed_stream": "FEED", "feed_port": 20}, timeout=120)
        call("dwsim_unitop_connect", {"flowsheet_id": fid2, "unitop": "COL",
                                      "product_stream": "DIST", "product_port": 0}, timeout=120)
        call("dwsim_unitop_connect", {"flowsheet_id": fid2, "unitop": "COL",
                                      "product_stream": "BOT", "product_port": 1}, timeout=120)
        call("dwsim_column_set_spec", {"flowsheet_id": fid2, "column": "COL", "spec_id": "C",
                                       "stype": "Stream_Ratio", "value": 3.0}, timeout=120)
        call("dwsim_column_set_spec", {"flowsheet_id": fid2, "column": "COL", "spec_id": "R",
                                       "stype": "Product_Molar_Flow_Rate", "value": 50.0,
                                       "unit": "mol/s"}, timeout=120)

        sol2 = call("dwsim_solve_run", {"flowsheet_id": fid2}, timeout=900)
        ok2 = bool(sol2.get("ok")) if isinstance(sol2, dict) else False
        check("5 bar column solves", ok2,
              "" if ok2 else json.dumps(sol2, ensure_ascii=False)[:200])

        if ok2:
            d = call("dwsim_stream_get_results", {"flowsheet_id": fid2, "name": "DIST"},
                     timeout=120)
            b = call("dwsim_stream_get_results", {"flowsheet_id": fid2, "name": "BOT"},
                     timeout=120)
            check("distillate and bottoms split 50/50",
                  abs((d.get("molar_flow_mol_s") or 0) - 50.0) < 0.5
                  and abs((b.get("molar_flow_mol_s") or 0) - 50.0) < 0.5,
                  "DIST=%.3f BOT=%.3f mol/s" % (d.get("molar_flow_mol_s") or 0,
                                                b.get("molar_flow_mol_s") or 0))
            # A broken pressure profile shows up as an inverted column: the condenser
            # ends up hotter than the reboiler.
            check("condenser is colder than reboiler",
                  (d.get("temperature_K") or 0) < (b.get("temperature_K") or 0),
                  "DIST=%.2fK BOT=%.2fK" % (d.get("temperature_K") or 0,
                                            b.get("temperature_K") or 0))
        call("dwsim_flowsheet_close", {"flowsheet_id": fid2}, timeout=60)

    try:
        proc.stdin.close()
    except Exception:
        pass
    proc.terminate()

    print("\n%d checks failed" % len(failures))
    for f in failures:
        print("  - " + f)
    return 1 if failures else 0


if __name__ == "__main__":
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "build", "Release", "net10.0", "dwsim-mcp.exe")
    sys.exit(main(exe))
