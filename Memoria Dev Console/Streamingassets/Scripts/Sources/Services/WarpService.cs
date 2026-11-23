using Memoria.Data;
using Memoria.Prime;
using System;
using System.Collections;
using System.Reflection;

namespace Memoria.DevConsole
{
    public sealed class WarpService
    {
        public String WarpToPreset(StoryWarpPreset preset)
        {
            if (preset == null)
                return "Warp rejected: no preset was supplied.";

            return WarpToField(preset.FieldId, preset.Scenario, preset.Party, preset.Name);
        }

        public String WarpToField(Int32 fieldId, Int32 scenario)
        {
            return WarpToField(fieldId, scenario, null, "Manual warp");
        }

        private String WarpToField(Int32 fieldId, Int32 scenario, CharacterId[] party, String label)
        {
            try
            {
                if (fieldId <= 0)
                    return "Warp rejected: Field ID must be greater than zero.";

                if (scenario < 0 || scenario > 65535)
                    return "Warp rejected: ScenarioCounter must be between 0 and 65535.";

                if (PersistenSingleton<FF9StateSystem>.Instance == null)
                    return "Warp rejected: FF9StateSystem is not ready. Enter a playable field first.";

                EventEngine engine = PersistenSingleton<EventEngine>.Instance;
                if (engine == null)
                    return "Warp rejected: EventEngine is not ready. Enter a playable field first.";

                Int32 locationId;
                String mappingError;
                if (!TryGetFieldLocationId(fieldId, out locationId, out mappingError))
                    return mappingError;

                // Normalize only the state Story Warper owns. Inventory and arbitrary
                // globals/custom quest flags are deliberately preserved so mod quest
                // progress survives sequential story warps.
                SceneTransitionAudio.Prepare();
                FF9StateSystem.EventState.ScenarioCounter = scenario;
                FF9StateSystem.EventState.FieldEntrance = 0;
                FF9StateSystem.Common.FF9.fldMapNo = (Int16)fieldId;
                FF9StateSystem.Common.FF9.fldLocNo = (Int16)locationId;

                if (party != null && party.Length > 0)
                    ApplyParty(party);

                PersistenSingleton<FF9StateSystem>.Instance.mode = (Byte)1;

                Log.Message("[Dev Console][Story Warp] " + (label ?? "Warp") +
                            " -> field " + fieldId + ", scenario " + scenario +
                            FormatPartyForLog(party));

                engine.ReplaceLoadMap();

                return (label ?? "Warp") + " -> Field " + fieldId +
                       " / Scenario " + scenario + ".";
            }
            catch (Exception ex)
            {
                Log.Error("[Dev Console][Story Warp] FAILED: " + ex);
                return "Warp failed: " + ex.Message;
            }
        }

        private static void ApplyParty(CharacterId[] party)
        {
            Int32 slot;
            for (slot = 0; slot < 4; slot++)
                ff9play.FF9Play_SetParty(slot, CharacterId.NONE);

            Int32 count = Math.Min(4, party.Length);
            for (slot = 0; slot < count; slot++)
                ff9play.FF9Play_SetParty(slot, party[slot]);
        }

        private static String FormatPartyForLog(CharacterId[] party)
        {
            if (party == null || party.Length == 0)
                return ", party preserved.";

            String text = ", party ";
            for (Int32 i = 0; i < party.Length && i < 4; i++)
            {
                if (i > 0)
                    text += "/";
                text += party[i].ToString();
            }
            return text + ".";
        }

        private static Boolean TryGetFieldLocationId(Int32 fieldId, out Int32 locationId, out String error)
        {
            locationId = 0;
            error = null;

            try
            {
                Assembly assembly = typeof(EventEngine).Assembly;
                Type utilsType = assembly.GetType("EventEngineUtils", false);

                if (utilsType == null)
                {
                    error = "Warp rejected: could not reflect EventEngineUtils.";
                    return false;
                }

                FieldInfo mapField = utilsType.GetField("eventIDToMESID", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (mapField == null)
                {
                    error = "Warp rejected: could not reflect EventEngineUtils.eventIDToMESID.";
                    return false;
                }

                IDictionary map = mapField.GetValue(null) as IDictionary;
                if (map == null)
                {
                    error = "Warp rejected: reflected eventIDToMESID is not an IDictionary.";
                    return false;
                }

                if (!map.Contains(fieldId))
                {
                    error = "Warp rejected: field " + fieldId + " is not mapped by EventEngineUtils.eventIDToMESID.";
                    return false;
                }

                locationId = Convert.ToInt32(map[fieldId]);
                return true;
            }
            catch (Exception ex)
            {
                error = "Warp rejected: field mapping reflection failed: " + ex.Message;
                Log.Error("[Dev Console][Story Warp] Mapping reflection failed: " + ex);
                return false;
            }
        }
    }
}
