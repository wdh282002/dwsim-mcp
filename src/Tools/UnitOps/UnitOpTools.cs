using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Automation.FluentAPI;
using DWSIM.MCPServer.Sessions;
using FluentFlowsheet = DWSIM.Automation.FluentAPI.Flowsheet;

namespace DWSIM.MCPServer.Tools.UnitOps
{
    public class UnitOpTools
    {
        private readonly SessionManager _sessions;

        private static readonly Dictionary<string, Action<FluentFlowsheet, string>> UnitOpFactory =
            new Dictionary<string, Action<FluentFlowsheet, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mixer"] = (fs, tag) => fs.AddMixer(tag),
                ["Splitter"] = (fs, tag) => fs.AddSplitter(tag),
                ["Heater"] = (fs, tag) => fs.AddHeater(tag),
                ["Cooler"] = (fs, tag) => fs.AddCooler(tag),
                ["Pump"] = (fs, tag) => fs.AddPump(tag),
                ["Compressor"] = (fs, tag) => fs.AddCompressor(tag),
                ["Expander"] = (fs, tag) => fs.AddExpander(tag),
                ["Valve"] = (fs, tag) => fs.AddValve(tag),
                ["Pipe"] = (fs, tag) => fs.AddPipe(tag),
                ["HeatExchanger"] = (fs, tag) => fs.AddHeatExchanger(tag),
                ["ComponentSeparator"] = (fs, tag) => fs.AddComponentSeparator(tag),
                ["Tank"] = (fs, tag) => fs.AddTank(tag),
                ["Vessel"] = (fs, tag) => fs.AddSeparator(tag),
                ["OrificePlate"] = (fs, tag) => fs.AddOrificePlate(tag),
                ["Filter"] = (fs, tag) => fs.AddFilter(tag),
                ["SolidsSeparator"] = (fs, tag) => fs.AddSolidsSeparator(tag),
                ["ShortcutColumn"] = (fs, tag) => fs.AddShortcutColumn(tag),
                ["DistillationColumn"] = (fs, tag) => fs.AddDistillationColumn(tag),
                ["AbsorptionColumn"] = (fs, tag) => fs.AddAbsorptionColumn(tag),
                ["ConversionReactor"] = (fs, tag) => fs.AddConversionReactor(tag),
                ["EquilibriumReactor"] = (fs, tag) => fs.AddEquilibriumReactor(tag),
                ["GibbsReactor"] = (fs, tag) => fs.AddGibbsReactor(tag),
                ["CSTR"] = (fs, tag) => fs.AddCSTR(tag),
                ["PFR"] = (fs, tag) => fs.AddPFR(tag),
                ["WindTurbine"] = (fs, tag) => fs.AddWindTurbine(tag),
                ["HydroelectricTurbine"] = (fs, tag) => fs.AddHydroelectricTurbine(tag),
                ["SolarPanel"] = (fs, tag) => fs.AddSolarPanel(tag),
                ["WaterElectrolyzer"] = (fs, tag) => fs.AddWaterElectrolyzer(tag),
                ["PEMFuelCell"] = (fs, tag) => fs.AddPEMFuelCell(tag),
                ["ReaktoroGibbsReactor"] = (fs, tag) => fs.AddReaktoroGibbsReactor(tag),
                ["BioReactor"] = (fs, tag) => fs.AddBioReactor(tag),
                ["AnaerobicDigester"] = (fs, tag) => fs.AddAnaerobicDigester(tag),
                ["CFBFastPyrolysis"] = (fs, tag) => fs.AddCFBFastPyrolysisReactor(tag),
                ["Pretreatment"] = (fs, tag) => fs.AddPretreatmentReactor(tag),
                ["BiogasUpgrader"] = (fs, tag) => fs.AddBiogasUpgrader(tag),
                ["CellLysis"] = (fs, tag) => fs.AddCellLysis(tag),
                ["Centrifuge"] = (fs, tag) => fs.AddCentrifuge(tag),
                ["Chromatography"] = (fs, tag) => fs.AddChromatographyColumn(tag),
                ["CrossflowUF"] = (fs, tag) => fs.AddCrossflowUF(tag),
                ["Crystallizer"] = (fs, tag) => fs.AddCrystallizer(tag),
                ["Recycle"] = (fs, tag) => fs.AddUnitOperation(ObjectType.OT_Recycle, tag),
                ["EnergyRecycle"] = (fs, tag) => fs.AddUnitOperation(ObjectType.OT_EnergyRecycle, tag),
                ["Spec"] = (fs, tag) => fs.AddUnitOperation(ObjectType.OT_Spec, tag),
                ["Adjust"] = (fs, tag) => fs.AddUnitOperation(ObjectType.OT_Adjust, tag),
            };

        public UnitOpTools(SessionManager sessions) { _sessions = sessions; }

        [McpTool("dwsim_unitop_add", "Add a unit operation to the flowsheet. Type can be: Mixer, Splitter, Heater, Cooler, Pump, Compressor, Expander, Valve, Pipe, HeatExchanger, ComponentSeparator, Tank, Vessel, OrificePlate, Filter, SolidsSeparator, ShortcutColumn, DistillationColumn, AbsorptionColumn, ConversionReactor, EquilibriumReactor, GibbsReactor, CSTR, PFR, WindTurbine, HydroelectricTurbine, SolarPanel, WaterElectrolyzer, PEMFuelCell, ReaktoroGibbsReactor, BioReactor, AnaerobicDigester, CFBFastPyrolysis, Pretreatment, BiogasUpgrader, CellLysis, Centrifuge, Chromatography, CrossflowUF, Crystallizer, and the logical blocks Recycle, EnergyRecycle, Spec and Adjust. A flowsheet with a loop needs a Recycle on one of its streams to tear it.")]
        public JObject Add(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Unit operation type name")] string type,
            [McpParam("Tag/name for the unit operation")] string name)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            if (!UnitOpFactory.TryGetValue(type, out var factory))
                throw new ArgumentException($"Unknown unit operation type: {type}. Use dwsim.unitop.list_types to see available types.");

            factory(fs, name);
            return new JObject { ["unitop"] = name, ["type"] = type };
        }

        [McpTool("dwsim_unitop_add_external", "Add an external unit operation by its display name (for Plus/extension unit operations).")]
        public JObject AddExternal(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Display name of the external unit operation")] string display_name,
            [McpParam("Tag/name for the unit operation")] string name)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            fs.AddExternalUnitOperation(display_name, name);
            return new JObject { ["unitop"] = name, ["type"] = display_name };
        }

        [McpTool("dwsim_unitop_connect", "Connect streams to a unit operation's ports. Specify feed and/or product material and energy streams by name.")]
        public JObject Connect(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Unit operation tag/name")] string unitop,
            [McpParam("Feed material stream tag", Required = false)] string feed_stream = null,
            [McpParam("Feed stream port index (default 0)", Required = false)] int feed_port = 0,
            [McpParam("Product material stream tag", Required = false)] string product_stream = null,
            [McpParam("Product stream port index (default 0)", Required = false)] int product_port = 0,
            [McpParam("Energy feed stream tag", Required = false)] string energy_feed = null,
            [McpParam("Energy feed port index (default 0)", Required = false)] int energy_feed_port = 0,
            [McpParam("Energy product stream tag", Required = false)] string energy_product = null,
            [McpParam("Energy product port index (default 0)", Required = false)] int energy_product_port = 0)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var uo = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == unitop);

            // Rigorous columns (DistillationColumn / AbsorptionColumn) need special handling.
            var isColumn = IsColumnObject(uo);

            var connections = new JArray();

            if (!string.IsNullOrEmpty(feed_stream))
            {
                var stream = fs.Inner.SimulationObjects.Values
                    .First(o => o.GraphicObject?.Tag == feed_stream);
                if (isColumn)
                {
                    // Rigorous columns need BOTH the graphic connection and an entry in the
                    // MaterialStreams dictionary (StreamInformation) that carries the feed stage.
                    // DWSIM's own ConnectFeed() re-does the graphic connection, which fails when
                    // the ports are already wired, so we do the two steps separately here.
                    var info = ColumnAttachMaterial(fs, uo, stream, ColumnBehavior.Feed, feed_port);
                    connections.Add($"feed:{feed_stream}->stage{feed_port}({info})");
                }
                else
                {
                    uo.ConnectFeedMaterialStream(stream, feed_port);
                    connections.Add($"feed:{feed_stream}->port{feed_port}");
                }
            }

            if (!string.IsNullOrEmpty(product_stream))
            {
                var stream = fs.Inner.SimulationObjects.Values
                    .First(o => o.GraphicObject?.Tag == product_stream);
                if (isColumn)
                {
                    // port 0 = distillate (top), port 1 = bottoms, port >= 2 = sidedraw on that stage
                    if (product_port == 0)
                    {
                        var info = ColumnAttachMaterial(fs, uo, stream, ColumnBehavior.Distillate, 0);
                        connections.Add($"product:{product_stream}->distillate({info})");
                    }
                    else if (product_port == 1)
                    {
                        var info = ColumnAttachMaterial(fs, uo, stream, ColumnBehavior.BottomsLiquid, -1);
                        connections.Add($"product:{product_stream}->bottoms({info})");
                    }
                    else
                    {
                        var info = ColumnAttachMaterial(fs, uo, stream, ColumnBehavior.Sidedraw, product_port);
                        connections.Add($"product:{product_stream}->sidedraw stage{product_port}({info})");
                    }
                }
                else
                {
                    uo.ConnectProductMaterialStream(stream, product_port);
                    connections.Add($"product:{product_stream}->port{product_port}");
                }
            }

            if (!string.IsNullOrEmpty(energy_feed))
            {
                var stream = fs.Inner.SimulationObjects.Values
                    .First(o => o.GraphicObject?.Tag == energy_feed);
                uo.ConnectFeedEnergyStream(stream, energy_feed_port);
                connections.Add($"energy_feed:{energy_feed}->port{energy_feed_port}");
            }

            if (!string.IsNullOrEmpty(energy_product))
            {
                var stream = fs.Inner.SimulationObjects.Values
                    .First(o => o.GraphicObject?.Tag == energy_product);
                uo.ConnectProductEnergyStream(stream, energy_product_port);
                connections.Add($"energy_product:{energy_product}->port{energy_product_port}");
            }

            return new JObject { ["unitop"] = unitop, ["connections"] = connections };
        }

        [McpTool("dwsim_unitop_set",
            "Configure a unit operation: outlet pressure, outlet temperature, efficiency, calculation " +
            "mode, and anything else the model exposes. Names are matched against the property system, " +
            "the dynamic properties and the model's own properties, so both 'PROP_CO_1' and " +
            "'OutletPressure' work; an enum is given by name. A name that matches nothing comes back " +
            "with the ones that would have. Call dwsim_unitop_get_results to see what a unit reports.")]
        public JObject Set(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Unit operation tag")] string name,
            [McpParam("Properties to set, as {name: value}. Values are in the flowsheet unit system.", JsonType = "object")]
            JObject properties)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);

            var obj = fs.Inner.SimulationObjects.Values.FirstOrDefault(
                o => o.GraphicObject != null && (o.GraphicObject.Tag == name || o.Name == name));

            if (obj == null)
                throw new ArgumentException($"No unit operation with tag or id '{name}'.");

            var applied = PropertySetter.Apply(obj, AsValues(properties),
                                               fs.Inner.FlowsheetOptions.SelectedUnitSystem);

            return new JObject
            {
                ["unitop"] = name,
                ["applied"] = new JArray(applied)
            };
        }

        [McpTool("dwsim_unitop_get_results", "Get calculated results for a unit operation.")]
        public JObject GetResults(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Unit operation tag/name")] string name)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var obj = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == name);

            var result = new JObject
            {
                ["name"] = name,
                ["type"] = obj.GraphicObject?.ObjectType.ToString(),
                ["calculated"] = obj.Calculated,
                ["error"] = obj.ErrorMessage ?? ""
            };

            var props = new JObject();

            // The key properties are what a unit chooses to report, when it reports any. Most
            // report none, so fall back to its calculated properties, named as the property
            // grid names them - a duty nobody can read is a duty nobody can check.
            if (obj is DWSIM.Interfaces.IUnitOperation uo)
            {
                try
                {
                    foreach (var propName in uo.GetKeyPropertyNames())
                    {
                        try
                        {
                            props[propName] = new JObject
                            {
                                ["value"] = uo.GetKeyPropertyValue(propName),
                                ["units"] = uo.GetKeyPropertyUnits(propName)
                            };
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (props.Count == 0)
            {
                var units = fs.Inner.FlowsheetOptions.SelectedUnitSystem;

                // GetProperties reports the dynamic-mode settings alongside the real ones, and
                // there are enough of them - hold-up volume, flow conductance - to crowd the duty
                // out of the list. The extra-property bag is the authority on which is which.
                var dynamicNames = new HashSet<string>(
                    ((IDictionary<string, object>)obj.ExtraPropertiesDescriptions).Keys,
                    StringComparer.Ordinal);

                foreach (var entry in PropertyCatalog.For(obj, units, PropertyType.RO))
                {
                    if (entry.Value == null) continue;
                    if (dynamicNames.Contains(entry.Id)) continue;

                    var label = string.IsNullOrEmpty(entry.Description) ? entry.Id : entry.Description;
                    props[label] = new JObject { ["value"] = entry.Value.ToString(), ["units"] = entry.Units };
                }
            }

            result["properties"] = props;

            // Which specifications the unit actually reads depends on its calculation mode, and
            // there is no way to guess the names. Listing them here is what stops a caller from
            // setting a target the unit will ignore.
            var modes = PropertySetter.CalculationModes(obj);
            if (modes.Count > 0)
            {
                result["calculation_modes"] = new JArray(modes.Keys);
                result["calculation_mode"] = modes
                    .Where(m => m.Value == CurrentMode(obj))
                    .Select(m => m.Key)
                    .FirstOrDefault() ?? "";
            }

            return result;
        }

        /// <summary>The JSON object as plain values, for the engine to apply.</summary>
        private static IEnumerable<KeyValuePair<string, object>> AsValues(JObject properties)
        {
            if (properties == null) yield break;

            foreach (var entry in properties)
            {
                var token = entry.Value;
                object value;

                switch (token.Type)
                {
                    case JTokenType.Boolean: value = token.Value<bool>(); break;
                    case JTokenType.Integer: value = token.Value<long>(); break;
                    case JTokenType.Float: value = token.Value<double>(); break;
                    default: value = token.ToString(); break;
                }

                yield return new KeyValuePair<string, object>(entry.Key, value);
            }
        }

        /// <summary>The unit's current calculation mode as an id, or -1 when it has none.</summary>
        private static int CurrentMode(ISimulationObject obj)
        {
            var property = obj.GetType().GetProperty("CalcMode");
            if (property == null) return -1;

            try { return Convert.ToInt32(property.GetValue(obj)); }
            catch (Exception) { return -1; }
        }

        // ---- rigorous column (DistillationColumn / AbsorptionColumn) helpers -----------

        /// <summary>Mirrors DWSIM's StreamInformation.Behavior enum.</summary>
        private enum ColumnBehavior
        {
            Distillate = 0,
            BottomsLiquid = 1,
            Feed = 2,
            Sidedraw = 3,
            OverheadVapor = 4
        }

        private static bool IsColumnObject(object uo) =>
            uo is DWSIM.UnitOperations.UnitOperations.DistillationColumn
            || uo is DWSIM.UnitOperations.UnitOperations.AbsorptionColumn;

        /// <summary>
        /// Wires a material stream to a rigorous column. Two things are required and DWSIM's
        /// own ConnectFeed() does them together, which fails when the graphic ports are
        /// already wired: (1) the graphic connection, (2) an entry in the column's
        /// MaterialStreams dictionary (StreamInformation) carrying the stage.
        /// stageIndex: 0 = condenser, 1 = top plate, -1 = last entry (reboiler).
        /// </summary>
        private static string ColumnAttachMaterial(FluentFlowsheet fs, object colObj,
            ISimulationObject stream, ColumnBehavior behavior, int stageIndex)
        {
            dynamic col = colObj;
            IList stages = (IList)col.Stages;
            int n = stages.Count;
            if (n == 0)
                throw new InvalidOperationException(
                    "The column has no stage objects. Call dwsim_column_set_stages first.");

            if (stageIndex < 0) stageIndex = n - 1;
            if (stageIndex > n - 1)
                throw new ArgumentException(
                    $"Stage {stageIndex} is out of range: the column has {n} stage entries " +
                    $"(0 = condenser, 1..{n - 2} = plates, {n - 1} = reboiler). " +
                    "Call dwsim_column_set_stages to add more stages.");

            string stageName = (string)((dynamic)stages[stageIndex]).Name;

            var colGo = ((ISimulationObject)colObj).GraphicObject;
            var streamGo = stream.GraphicObject;

            // 1) graphic connection - only if not already wired
            if (behavior == ColumnBehavior.Feed)
            {
                if (!AlreadyConnected(streamGo, 0, colGo))
                {
                    int inIdx = FirstFreeConnector(colGo.InputConnectors, ConType.ConIn);
                    if (inIdx < 0)
                        throw new InvalidOperationException("No free feed port available on the column.");
                    fs.Inner.ConnectObjects(streamGo, colGo, 0, inIdx);
                }
            }
            else
            {
                // DWSIM's own connectors: 0 = distillate, 1 = bottoms, 9 = overhead vapour.
                int outIdx = 0;
                if (behavior == ColumnBehavior.BottomsLiquid) outIdx = 1;
                else if (behavior == ColumnBehavior.OverheadVapor) outIdx = 9;

                if (!AlreadyConnected(colGo, outIdx, streamGo))
                    fs.Inner.ConnectObjects(colGo, streamGo, outIdx, 0);
            }

            // 2) StreamInformation entry - this is what the column solver actually reads
            IDictionary dict = (IDictionary)col.MaterialStreams;
            Type siType = dict.GetType().GetGenericArguments()[1];
            string key = stream.Name;

            object existing = null;
            foreach (DictionaryEntry de in dict)
            {
                dynamic si = de.Value;
                string sid = (string)((dynamic)si).StreamID;
                string id = (string)((dynamic)si).ID;
                if (sid == key || id == key) { existing = de.Value; break; }
            }

            object entry = existing ?? Activator.CreateInstance(siType);
            var t = entry.GetType();
            t.GetProperty("ID").SetValue(entry, key);
            t.GetProperty("StreamID").SetValue(entry, key);
            t.GetProperty("AssociatedStage").SetValue(entry, stageName);
            SetEnumProperty(t, entry, "StreamBehavior", (int)behavior);
            SetEnumProperty(t, entry, "StreamType", 0); // StreamInformation.Type.Material

            if (existing == null)
                dict[key] = entry;

            return stageName;
        }

        private static bool AlreadyConnected(IGraphicObject from, int outIdx, IGraphicObject to)
        {
            if (from == null || to == null) return false;
            if (outIdx < 0 || outIdx >= from.OutputConnectors.Count) return false;
            var con = from.OutputConnectors[outIdx];
            if (con == null || !con.IsAttached || con.AttachedConnector == null) return false;
            return ReferenceEquals(con.AttachedConnector.AttachedTo, to);
        }

        private static int FirstFreeConnector(List<IConnectionPoint> connectors, ConType type)
        {
            for (int i = 0; i < connectors.Count; i++)
            {
                var c = connectors[i];
                if (c != null && !c.IsAttached && c.Type == type) return i;
            }
            return -1;
        }

        private static void SetEnumProperty(Type t, object target, string name, int value)
        {
            var p = t.GetProperty(name);
            if (p == null) return;
            p.SetValue(target, Enum.ToObject(p.PropertyType, value));
        }

        [McpTool("dwsim_column_set_stages",
            "Set the number of stages of a DistillationColumn or AbsorptionColumn. This rebuilds the column's " +
            "internal stage list (condenser + n plates + reboiler). Setting the NumberOfStages property with " +
            "dwsim_unitop_set only changes the counter and leaves the column uninitializable - always use this tool.")]
        public JObject SetColumnStages(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Column tag/name")] string column,
            [McpParam("Number of stages (condenser + plates + reboiler; must be > 3)")] int stages,
            [McpParam("Top stage pressure in Pa. This rewrites the pressure profile of EVERY stage, because a stage left at 0 Pa makes the solver diverge and a stage left at the 101325 Pa DWSIM default breaks the profile in half the moment the column runs at any other pressure. Pass 0 to leave the pressures alone.", Required = false)] double top_pressure = 101325.0,
            [McpParam("Pressure drop per stage in Pa, subtracted stage by stage from the top pressure (default 0).", Required = false)] double pressure_drop_per_stage = 0.0)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var col = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == column);

            if (!IsColumnObject(col))
                throw new ArgumentException($"'{column}' is not a DistillationColumn or AbsorptionColumn.");

            ((dynamic)col).SetNumberOfStages(stages);

            dynamic dcol = col;
            IList stageList = (IList)dcol.Stages;
            int count = stageList.Count;
            int rewritten = 0;

            // SetNumberOfStages only appends stages; the stages that already existed keep whatever
            // pressure they had, which is the 101325 Pa DWSIM default for a freshly added column.
            // Initialising only the new ones therefore produces a profile that jumps from 101325 Pa
            // straight to the requested pressure partway down, and every flash below the jump blows
            // up. Rewrite the whole profile when a pressure is asked for.
            if (top_pressure > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    dynamic st = stageList[i];
                    st.P = top_pressure - i * pressure_drop_per_stage;
                    rewritten++;
                }
            }

            return new JObject
            {
                ["column"] = column,
                ["number_of_stages"] = stages,
                ["stage_entries"] = count,
                ["pressures_rewritten"] = rewritten,
                ["top_pressure_Pa"] = top_pressure,
                ["bottom_pressure_Pa"] = top_pressure > 0
                    ? top_pressure - (count - 1) * pressure_drop_per_stage
                    : 0.0,
                ["note"] = $"index 0 = condenser, 1..{count - 2} = plates, {count - 1} = reboiler"
            };
        }

        [McpTool("dwsim_column_connect_vapor",
            "Connect a material stream as the overhead VAPOUR product of a rigorous column. Needed whenever the " +
            "feed carries non-condensables (N2, CO, O2, NO, ...): a total condenser cannot condense them, and the " +
            "column will never converge. Set CondenserType to Partial_Condenser with dwsim_unitop_set, connect the " +
            "vapour outlet with this tool, and keep the liquid distillate on product_port 0 of dwsim_unitop_connect.")]
        public JObject ConnectColumnVapor(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Column tag/name")] string column,
            [McpParam("Stream tag/name to receive the overhead vapour")] string stream)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var col = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == column);
            var streamObj = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == stream);

            if (!IsColumnObject(col))
                throw new ArgumentException($"'{column}' is not a DistillationColumn or AbsorptionColumn.");

            var stage = ColumnAttachMaterial(fs, col, streamObj, ColumnBehavior.OverheadVapor, 0);

            return new JObject
            {
                ["column"] = column,
                ["stream"] = stream,
                ["behavior"] = "OverheadVapor",
                ["stage"] = stage
            };
        }

        [McpTool("dwsim_column_get_streams",
            "Inspect the StreamInformation entries a DistillationColumn or AbsorptionColumn actually knows about, " +
            "i.e. which connected streams it treats as feeds, distillate or bottoms and on which stage. Use this to " +
            "diagnose 'One or more of the stream connections to the column is missing'.")]
        public JObject GetColumnStreams(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Column tag/name")] string column)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var col = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == column);

            if (!IsColumnObject(col))
                throw new ArgumentException($"'{column}' is not a DistillationColumn or AbsorptionColumn.");

            dynamic dcol = col;
            IList stageList = (IList)dcol.Stages;

            var stageNames = new JArray();
            for (int i = 0; i < stageList.Count; i++)
            {
                dynamic st = stageList[i];
                stageNames.Add(new JObject
                {
                    ["index"] = i,
                    ["name"] = (string)st.Name,
                    ["id"] = (string)st.ID
                });
            }

            string TagOf(string objectName) =>
                fs.Inner.SimulationObjects.TryGetValue(objectName, out var so)
                    ? (so.GraphicObject?.Tag ?? objectName)
                    : objectName;

            var arr = new JArray();
            IDictionary dict = (IDictionary)dcol.MaterialStreams;
            foreach (DictionaryEntry de in dict)
            {
                dynamic si = de.Value;
                string streamId = (string)si.StreamID;
                int stageIdx = -1;
                try { stageIdx = (int)dcol.StageIndex((string)si.AssociatedStage); }
                catch { }
                arr.Add(new JObject
                {
                    ["key"] = (string)de.Key,
                    ["stream"] = TagOf(streamId),
                    ["behavior"] = si.StreamBehavior.ToString(),
                    ["type"] = si.StreamType.ToString(),
                    ["stage_index"] = stageIdx,
                    ["stage"] = (string)si.AssociatedStage
                });
            }

            return new JObject
            {
                ["column"] = column,
                ["number_of_stages"] = (int)dcol.NumberOfStages,
                ["stage_entries"] = stageList.Count,
                ["streams"] = arr,
                ["stages"] = stageNames
            };
        }

        [McpTool("dwsim_column_set_spec",
            "Set one of the two operating specifications of a rigorous column. DistillationColumn uses spec ids " +
            "'C' (condenser/reflux spec) and 'R' (reboiler/bottoms spec). stype is one of: Heat_Duty, " +
            "Product_Molar_Flow_Rate, Component_Molar_Flow_Rate, Product_Mass_Flow_Rate, Component_Mass_Flow_Rate, " +
            "Component_Fraction, Component_Recovery, Stream_Ratio, Temperature, Feed_Recovery. " +
            "A rigorous column will not converge until both specs hold sensible values.")]
        public JObject SetColumnSpec(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Column tag/name")] string column,
            [McpParam("Spec id: 'C' (condenser/reflux) or 'R' (reboiler/bottoms)")] string spec_id,
            [McpParam("Spec type, e.g. Stream_Ratio, Product_Molar_Flow_Rate, Heat_Duty, Component_Recovery")] string stype,
            [McpParam("Spec value")] double value,
            [McpParam("Unit for the value, e.g. 'mol/s', 'kW', ''. Ignored for Stream_Ratio.", Required = false)] string unit = "",
            [McpParam("Stage number the spec applies to (-1 = default)", Required = false)] int stage = -1,
            [McpParam("Compound name for component-based specs", Required = false)] string component = "")
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var col = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == column);

            if (!IsColumnObject(col))
                throw new ArgumentException($"'{column}' is not a DistillationColumn or AbsorptionColumn.");

            dynamic dcol = col;
            IDictionary specs = (IDictionary)dcol.Specs;
            Type specType = specs.GetType().GetGenericArguments()[1];

            object entry = specs[spec_id];
            bool created = entry == null;
            if (created)
            {
                entry = Activator.CreateInstance(specType);
                specs[spec_id] = entry;
            }

            var t = entry.GetType();
            var stProp = t.GetProperty("SType");
            stProp.SetValue(entry, Enum.Parse(stProp.PropertyType, stype, true));
            t.GetProperty("SpecValue").SetValue(entry, value);
            t.GetProperty("SpecUnit").SetValue(entry, unit ?? "");
            t.GetProperty("StageNumber").SetValue(entry, stage);

            if (!string.IsNullOrEmpty(component))
            {
                var comp = fs.Inner.SelectedCompounds.Values.FirstOrDefault(
                    c => string.Equals(c.Name, component, StringComparison.OrdinalIgnoreCase));
                t.GetProperty("ComponentID").SetValue(entry, comp?.Name ?? component);
                if (comp != null)
                {
                    int idx = 0;
                    foreach (var c in fs.Inner.SelectedCompounds.Values)
                    {
                        if (c.Name == comp.Name) break;
                        idx++;
                    }
                    t.GetProperty("ComponentIndex").SetValue(entry, idx);
                }
            }

            return new JObject
            {
                ["column"] = column,
                ["spec"] = spec_id,
                ["stype"] = stProp.GetValue(entry).ToString(),
                ["value"] = value,
                ["unit"] = unit ?? "",
                ["created"] = created
            };
        }

        [McpTool("dwsim_column_set_feed_stage", "Set the feed stage for a material stream already connected to a DistillationColumn or AbsorptionColumn. Pass the stream tag and the stage index (0 = condenser, 1 = top plate, n = reboiler).")]
        public JObject SetColumnFeedStage(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Column tag/name")] string column,
            [McpParam("Feed stream tag/name")] string stream,
            [McpParam("Stage number (1 = first plate below condenser, 40 = reboiler for 40-stage column)")] int stage)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var col = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == column);
            var streamObj = fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == stream);

            if (!IsColumnObject(col))
                throw new ArgumentException($"'{column}' is not a DistillationColumn or AbsorptionColumn.");

            // The StreamInformation entry is keyed by the simulation object's Name,
            // which is not necessarily the same as its graphic tag.
            string key = streamObj.Name;

            dynamic dcol = col;
            IList stages = (IList)dcol.Stages;
            if (stages.Count == 0)
                throw new InvalidOperationException(
                    "The column has no stage objects. Call dwsim_column_set_stages first.");
            if (stage < 0 || stage > stages.Count - 1)
                throw new ArgumentException(
                    $"Stage must be between 0 and {stages.Count - 1} (0 = condenser, {stages.Count - 1} = reboiler).");

            string stageName = (string)((dynamic)stages[stage]).Name;

            IDictionary dict = (IDictionary)dcol.MaterialStreams;
            object existing = null;
            foreach (DictionaryEntry de in dict)
            {
                dynamic s = de.Value;
                if ((string)s.StreamID == key || (string)s.ID == key) { existing = de.Value; break; }
                // also accept a match on the graphic tag
                if (fs.Inner.SimulationObjects.TryGetValue((string)s.StreamID, out var so)
                    && so.GraphicObject?.Tag == stream) { existing = de.Value; break; }
            }

            object entry = existing ?? Activator.CreateInstance(dict.GetType().GetGenericArguments()[1]);
            var t = entry.GetType();
            t.GetProperty("ID").SetValue(entry, key);
            t.GetProperty("StreamID").SetValue(entry, key);
            t.GetProperty("AssociatedStage").SetValue(entry, stageName);
            SetEnumProperty(t, entry, "StreamBehavior", (int)ColumnBehavior.Feed);
            SetEnumProperty(t, entry, "StreamType", 0);

            if (existing == null)
                dict[key] = entry;

            return new JObject
            {
                ["column"] = column,
                ["stream"] = stream,
                ["stage"] = stage,
                ["stage_name"] = stageName
            };
        }

        [McpTool("dwsim_unitop_list_types", "List all available unit operation types that can be used with dwsim_unitop_add.")]
        public JObject ListTypes()
        {
            var arr = new JArray();
            foreach (var key in UnitOpFactory.Keys.OrderBy(k => k))
                arr.Add(key);
            return new JObject { ["types"] = arr, ["count"] = arr.Count };
        }
    }
}
