using Memoria.Data;
using System;

namespace Memoria.DevConsole
{
    public sealed class StoryWarpPreset
    {
        public readonly String Name;
        public readonly Int32 Disc;
        public readonly Int32 FieldId;
        public readonly Int32 Scenario;
        public readonly CharacterId[] Party;

        public StoryWarpPreset(String name, Int32 disc, Int32 fieldId, Int32 scenario, params CharacterId[] party)
        {
            Name = name;
            Disc = disc;
            FieldId = fieldId;
            Scenario = scenario;
            Party = party ?? new CharacterId[0];
        }
    }

    public static class StoryWarpCatalog
    {
        // One clean first-visit target per major story section. Scenario values are
        // taken from the vanilla field-script progression points in the supplied FS dump.
        // We deliberately do NOT touch inventory or arbitrary global/custom quest flags.
        public static readonly StoryWarpPreset[][] Discs =
        {
            new[]
            {
                new StoryWarpPreset("Alexandria / Opening", 1, 50, 1000, CharacterId.Zidane),
                new StoryWarpPreset("Evil Forest", 1, 250, 2010, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Steiner),
                new StoryWarpPreset("Ice Cavern", 1, 300, 2500, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Steiner),
                new StoryWarpPreset("Dali", 1, 350, 2600, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Steiner),
                new StoryWarpPreset("Cargo Ship", 1, 500, 2940, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Steiner),
                new StoryWarpPreset("Lindblum", 1, 614, 3000, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Steiner),
                new StoryWarpPreset("Gizamaluke's Grotto", 1, 701, 3710, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Freya, CharacterId.Quina),
                new StoryWarpPreset("Burmecia", 1, 750, 3800, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Freya, CharacterId.Quina),
            },
            new[]
            {
                new StoryWarpPreset("South Gate / Summit Station", 2, 812, 4150, CharacterId.Garnet, CharacterId.Steiner, CharacterId.Marcus),
                new StoryWarpPreset("Treno", 2, 908, 4400, CharacterId.Garnet, CharacterId.Steiner, CharacterId.Marcus),
                new StoryWarpPreset("Gargan Roo", 2, 950, 4500, CharacterId.Garnet, CharacterId.Steiner, CharacterId.Marcus),
                new StoryWarpPreset("Cleyra", 2, 1000, 4650, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Freya, CharacterId.Quina),
                new StoryWarpPreset("Alexandria Rescue", 2, 1208, 5030, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Freya, CharacterId.Quina),
                new StoryWarpPreset("Pinnacle Rocks", 2, 1251, 5510, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet),
                new StoryWarpPreset("Lindblum in Ruins", 2, 1300, 5600, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet),
                new StoryWarpPreset("Fossil Roo", 2, 1422, 5900, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Quina),
                new StoryWarpPreset("Conde Petie", 2, 1500, 5990, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Quina),
                new StoryWarpPreset("Black Mage Village", 2, 1450, 6212, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Quina),
                new StoryWarpPreset("Conde Petie Mountain Path", 2, 1550, 6300, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Quina),
                new StoryWarpPreset("Madain Sari", 2, 1600, 6600, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Quina),
                new StoryWarpPreset("Iifa Tree", 2, 1650, 6700, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Iifa Tree - Return", 2, 1757, 6900, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
            },
            new[]
            {
                new StoryWarpPreset("Alexandria - Return", 3, 1861, 7010, CharacterId.Zidane),
                new StoryWarpPreset("Treno Card Tournament", 3, 1903, 7600, CharacterId.Zidane),
                new StoryWarpPreset("Battle of Alexandria", 3, 2050, 8340, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Freya, CharacterId.Eiko),
                new StoryWarpPreset("Lindblum", 3, 2100, 9000, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Black Mage Village - Return", 3, 1450, 9410, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Desert Palace / Party Split", 3, 2200, 9510, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Oeilvert", 3, 2250, 9700, CharacterId.Zidane, CharacterId.Steiner, CharacterId.Freya, CharacterId.Amarant),
                new StoryWarpPreset("Desert Palace - Return", 3, 2200, 9800, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko, CharacterId.Quina),
                new StoryWarpPreset("Esto Gaza", 3, 2300, 9890, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Mount Gulug", 3, 2350, 9990, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Lindblum - After Gulug", 3, 2150, 10000, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Ipsen's Castle", 3, 2500, 10500, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Four Shrines", 3, 2850, 10600, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Terra", 3, 2600, 10800, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Bran Bal", 3, 2651, 10900, CharacterId.Zidane, CharacterId.Garnet),
                new StoryWarpPreset("Pandemonium", 3, 2700, 10930, CharacterId.Zidane),
            },
            new[]
            {
                new StoryWarpPreset("Black Mage Village / Disc 4", 4, 3050, 11100, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Memoria", 4, 2900, 11610, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
                new StoryWarpPreset("Crystal World", 4, 2922, 11765, CharacterId.Zidane, CharacterId.Vivi, CharacterId.Garnet, CharacterId.Eiko),
            }
        };

        public static StoryWarpPreset[] GetDisc(Int32 disc)
        {
            if (disc < 1 || disc > Discs.Length)
                return new StoryWarpPreset[0];
            return Discs[disc - 1];
        }
    }
}
