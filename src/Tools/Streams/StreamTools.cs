using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using DWSIM.Automation.FluentAPI;
using DWSIM.Automation.FluentAPI.Builders;
using DWSIM.MCPServer.Sessions;

using static DWSIM.Automation.FluentAPI.Q;

namespace DWSIM.MCPServer.Tools.Streams
{
    public class StreamTools
    {
        private readonly SessionManager _sessions;

        public StreamTools(SessionManager sessions) { _sessions = sessions; }

        private const string CompositionHelp =
            "Composition as a {compound: value} object. composition_basis selects what the numbers mean: " +
            "'mole_fraction' (default) interprets them as mole fractions and normalises them, " +
            "'mass_fraction' as mass fractions, 'molar' as per-compound molar flows in mol/s, " +
            "'mass' as per-compound mass flows in kg/s.";

        /// <summary>
        /// Applies a composition to a material stream builder. The old code always fed the numbers to
        /// SetCompoundMassFlow, so passing mole fractions - the natural thing to do - produced a stream
        /// whose total flow was essentially zero.
        /// </summary>
        private static void ApplyComposition(MaterialStreamBuilder builder, JObject composition, string basis)
        {
            if (composition == null) return;

            switch ((basis ?? "mole_fraction").Trim().ToLowerInvariant())
            {
                case "molar":
                case "molar_flow":
                    foreach (var p in composition.Properties())
                        builder.SetCompoundMolarFlow(p.Name, p.Value.Value<double>());
                    break;

                case "mass":
                case "mass_flow":
                    foreach (var p in composition.Properties())
                        builder.SetCompoundMassFlow(p.Name, p.Value.Value<double>());
                    break;

                case "mass_fraction":
                    builder.WithComposition(c =>
                    {
                        foreach (var p in composition.Properties())
                            c.Mass(p.Name, p.Value.Value<double>());
                    });
                    break;

                default: // mole fractions
                    builder.WithComposition(c =>
                    {
                        foreach (var p in composition.Properties())
                            c.Mole(p.Name, p.Value.Value<double>());
                    });
                    break;
            }
        }

        [McpTool("dwsim_stream_add_material", "Add a new material stream to the flowsheet. Optionally set temperature (K), pressure (Pa), mass flow (kg/s), molar flow (mol/s), vapor fraction, and composition.")]
        public JObject AddMaterial(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Tag/name for the stream")] string name,
            [McpParam("Temperature in Kelvin", Required = false)] double temperature_K = 0,
            [McpParam("Pressure in Pascal", Required = false)] double pressure_Pa = 0,
            [McpParam("Mass flow in kg/s", Required = false)] double mass_flow_kg_s = 0,
            [McpParam("Molar flow in mol/s", Required = false)] double molar_flow_mol_s = 0,
            [McpParam("Vapor fraction (0-1)", Required = false)] double vapor_fraction = -1,
            [McpParam(CompositionHelp, Required = false, JsonType = "object")] JObject composition = null,
            [McpParam("How to read the composition values: mole_fraction (default), mass_fraction, molar or mass", Required = false)] string composition_basis = "mole_fraction")
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var builder = fs.AddMaterialStream(name);

            if (temperature_K > 0) builder.WithTemperature(temperature_K.Kelvin());
            if (pressure_Pa > 0) builder.WithPressure(pressure_Pa.Pascal());
            if (mass_flow_kg_s > 0) builder.WithMassFlow(mass_flow_kg_s.KgPerSecond());
            if (molar_flow_mol_s > 0) builder.WithMolarFlow(molar_flow_mol_s.MolPerSecond());
            if (vapor_fraction >= 0) builder.WithVaporFraction(vapor_fraction);

            ApplyComposition(builder, composition, composition_basis);

            return new JObject { ["stream"] = name, ["type"] = "MaterialStream" };
        }

        [McpTool("dwsim_stream_add_energy", "Add a new energy stream to the flowsheet.")]
        public JObject AddEnergy(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Tag/name for the stream")] string name)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            fs.AddEnergyStream(name);
            return new JObject { ["stream"] = name, ["type"] = "EnergyStream" };
        }

        [McpTool("dwsim_stream_get_results", "Get calculated results for a material stream: phases, composition, temperature, pressure, flows.")]
        public JObject GetResults(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Stream tag/name")] string name)
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var ms = fs.MaterialStream(name);

            var result = new JObject
            {
                ["name"] = name,
                ["temperature_K"] = ms.TemperatureK,
                ["pressure_Pa"] = ms.PressurePa,
                ["mass_flow_kg_s"] = ms.MassFlowKgPerSecond,
                ["molar_flow_mol_s"] = ms.MolarFlowMolPerSecond
            };

            var obj = (DWSIM.Thermodynamics.Streams.MaterialStream)fs.Inner.SimulationObjects.Values
                .First(o => o.GraphicObject?.Tag == name);

            var phases = new JArray();
            foreach (var phase in obj.Phases)
            {
                var phaseObj = new JObject
                {
                    ["name"] = phase.Value.Name,
                    ["fraction"] = phase.Value.Properties.molarfraction.GetValueOrDefault(),
                    ["temperature_K"] = phase.Value.Properties.temperature.GetValueOrDefault(),
                    ["pressure_Pa"] = phase.Value.Properties.pressure.GetValueOrDefault(),
                    ["enthalpy_kJ_kg"] = phase.Value.Properties.enthalpy.GetValueOrDefault(),
                    ["entropy_kJ_kgK"] = phase.Value.Properties.entropy.GetValueOrDefault(),
                    ["density_kg_m3"] = phase.Value.Properties.density.GetValueOrDefault()
                };

                var comp = new JObject();
                foreach (var c in phase.Value.Compounds)
                {
                    comp[c.Key] = new JObject
                    {
                        ["mole_fraction"] = c.Value.MoleFraction.GetValueOrDefault(),
                        ["mass_fraction"] = c.Value.MassFraction.GetValueOrDefault()
                    };
                }
                phaseObj["compounds"] = comp;
                phases.Add(phaseObj);
            }

            result["phases"] = phases;
            return result;
        }

        [McpTool("dwsim_stream_set_conditions", "Update conditions on an existing material stream (temperature, pressure, flow, composition).")]
        public JObject SetConditions(
            [McpParam("Flowsheet handle")] string flowsheet_id,
            [McpParam("Stream tag/name")] string name,
            [McpParam("Temperature in Kelvin", Required = false)] double temperature_K = 0,
            [McpParam("Pressure in Pascal", Required = false)] double pressure_Pa = 0,
            [McpParam("Mass flow in kg/s", Required = false)] double mass_flow_kg_s = 0,
            [McpParam("Molar flow in mol/s", Required = false)] double molar_flow_mol_s = 0,
            [McpParam(CompositionHelp, Required = false, JsonType = "object")] JObject composition = null,
            [McpParam("How to read the composition values: mole_fraction (default), mass_fraction, molar or mass", Required = false)] string composition_basis = "mole_fraction")
        {
            var fs = _sessions.GetFlowsheet(flowsheet_id);
            var builder = fs.MaterialStream(name);

            if (temperature_K > 0) builder.WithTemperature(temperature_K.Kelvin());
            if (pressure_Pa > 0) builder.WithPressure(pressure_Pa.Pascal());
            if (mass_flow_kg_s > 0) builder.WithMassFlow(mass_flow_kg_s.KgPerSecond());
            if (molar_flow_mol_s > 0) builder.WithMolarFlow(molar_flow_mol_s.MolPerSecond());

            ApplyComposition(builder, composition, composition_basis);

            return new JObject { ["updated"] = name };
        }
    }
}
