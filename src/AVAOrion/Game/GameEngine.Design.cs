using AVAOrion.Rules;
using AVAOrion.World;

namespace AVAOrion.Game;

public sealed partial class GameEngine
{
    /// <summary>The design being drawn up in the design room.</summary>
    public ShipDesign? Draft { get; private set; }

    public void DesignsMenu()
    {
        Show(() =>
        {
            var p = new Panel { Title = "Ship designs", Kind = PanelKind.Big, Back = Pop };
            p.Say("Up to six designs can be built at once; a new one retires the oldest warship design. Ships already built fly on.");
            foreach (var d in Me.Active.ToList())
            {
                var design = d;
                int flying = World.Fleets.Where(f => f.Owner == World.Player).Sum(f => f.Ships.Where(s => s.Design == d.Id).Sum(s => s.Count));
                p.Add($"{d.Name}: {d.Cost} BC, {flying} in service", () => DesignActions(design), note: d.Describe());
            }
            p.Add("Draw up a new design", () => Edit(Designer.Warship(Me, Hull.Medium)), note: "Start from the best medium warship you can build, and change what you like.");
            p.Add("Close", Pop);
            return p;
        });
    }

    private void DesignActions(ShipDesign d)
    {
        var p = new Panel { Title = d.Name, Back = Pop };
        p.Say(d.Describe());
        p.Say($"Costs {d.Cost} BC. Speed {1.5 + Math.Max(1, d.Warp):0.0} parsecs a year.");
        p.Add("Start a new design from this one", () => { Pop(); var copy = d.Copy(); copy.Name = Designer.Fresh(Me, d.Name); copy.Warp = Sim.Warp(Me); Edit(copy); });
        p.Add("Retire it (no more will be built)", () =>
        {
            d.Retired = true;
            foreach (var s in World.Stars.Where(s => s.Owner == World.Player && s.Building == d.Id)) s.Building = Me.Active.FirstOrDefault(x => x.Armed)?.Id ?? -1;
            Pop();
        }, Me.Active.Count() > 1);
        p.Add("Back", Pop);
        Push(p);
    }

    /// <summary>The design room: every part of a design, changed with Left and Right.</summary>
    private void Edit(ShipDesign draft)
    {
        Draft = draft;
        var arsenal = Sim.Arsenal(Me).ToList();
        Show(() =>
        {
            var d = Draft!;
            var p = new Panel { Title = "The design room", Kind = PanelKind.Design, Back = () => { Draft = null; Pop(); } };
            p.AddAdjust($"Hull: {d.Info.Name} ({d.Info.Space} space)", n => d.Hull = (Hull)Math.Clamp((int)d.Hull + n, 0, 3));
            p.AddAdjust($"Armour: {Ships.ArmorNames[d.Armor]}", n => d.Armor = Math.Clamp(d.Armor + n, 0, Sim.ArmorTier(Me)));
            p.AddAdjust($"Shield: {(d.Shield == 0 ? "none" : "class " + d.Shield)}", n => d.Shield = Math.Clamp(d.Shield + n, 0, Sim.ShieldTier(Me)));
            p.AddAdjust($"Battle computer: {(d.Computer == 0 ? "none" : "Mk " + d.Computer)}", n => d.Computer = Math.Clamp(d.Computer + n, 0, Sim.ComputerTier(Me)));
            p.AddAdjust($"ECM jammer: {(d.Ecm == 0 ? "none" : "Mk " + d.Ecm)}", n => d.Ecm = Math.Clamp(d.Ecm + n, 0, Sim.EcmTier(Me)));
            for (int slot = 0; slot < 3; slot++)
            {
                int i = slot;
                var mount = i < d.Mounts.Count ? d.Mounts[i] : null;
                p.AddAdjust($"Weapon {i + 1}: {(mount is null ? "empty" : Ships.Weapons[mount.Weapon].Name)}", n =>
                {
                    int at = mount is null ? -1 : arsenal.FindIndex(w => w.Id == mount.Weapon);
                    int next = at + n;
                    if (next < 0 || next >= arsenal.Count)
                    {
                        if (mount is not null) d.Mounts.RemoveAt(i);
                        return;
                    }
                    var m = new Mount(arsenal[next].Id, Math.Max(1, mount?.Count ?? 1));
                    if (mount is null) d.Mounts.Add(m);
                    else d.Mounts[i] = m;
                }, note: mount is null ? "Left and Right choose a weapon." : Techs.Get(Ships.Weapons[mount.Weapon].Name).About);
                if (mount is not null)
                    p.AddAdjust($"   how many: {mount.Count}", n => d.Mounts[i] = mount with { Count = Math.Max(1, mount.Count + n) });
            }
            p.AddAdjust($"Colony pod: {(d.Colony ? "yes" : "no")}", _ => d.Colony = !d.Colony, note: "Carries colonists to found a new colony (80 space).");
            p.AddAdjust($"Reserve tanks: {(d.Tanks ? "yes" : "no")}", _ => d.Tanks = !d.Tanks, note: "Three parsecs more range (15 space).");
            p.Add("Fill the rest with weapons", () => Fill(d, arsenal));
            p.Add("Save the design", () =>
            {
                if (!d.Fits) { Say("It doesn't fit: take something out or choose a bigger hull.", 0xFFE0A040); Play(Sfx.Bump); return; }
                if (!d.Armed && !d.Colony && !d.Tanks) { Say("A ship with nothing aboard is no use to anyone.", 0xFFE0A040); return; }
                Name(d);
            });
            p.Add("Throw it away", () => { Draft = null; Pop(); });
            return p;
        });
    }

    /// <summary>Fills whatever room is left with the best weapon that fits.</summary>
    private static void Fill(ShipDesign d, List<Weapon> arsenal)
    {
        int room = d.Info.Space - d.SpaceUsed;
        var best = arsenal.Where(w => w.Space <= room).OrderByDescending(w => w.Average / w.Space).ThenByDescending(w => w.Average).FirstOrDefault();
        if (best is null) return;
        int n = room / best.Space;
        int at = d.Mounts.FindIndex(m => m.Weapon == best.Id);
        if (at >= 0) d.Mounts[at] = d.Mounts[at] with { Count = d.Mounts[at].Count + n };
        else if (d.Mounts.Count < 3) d.Mounts.Add(new Mount(best.Id, n));
    }

    private void Name(ShipDesign d)
    {
        string suggestion = d.Colony ? Designer.Fresh(Me, "Colony Ship") : !d.Armed ? Designer.Fresh(Me, "Scout") : Designer.Fresh(Me, Designer.HullNames[(int)d.Hull]);
        var p = new Panel { Title = "Name the design", Entry = suggestion, Prompt = "Name:", Back = Pop };
        p.Say("What should this design be called?");
        p.OnEntry = name =>
        {
            if (name.Length == 0) return;
            if (Me.Designs.Any(x => x.Name == name)) { Say("You already have a design of that name.", 0xFFE0A040); return; }
            d.Name = name;
            d.Warp = Sim.Warp(Me);
            Designer.Add(Me, d);
            Draft = null;
            CloseAll();
            Say($"The {d.Name} is ready for the shipyards. Choose it on a colony's screen.", 0xFFFFD860);
            DesignsMenu();
        };
        Push(p);
    }
}
