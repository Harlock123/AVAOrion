using AVAOrion.Rules;

namespace AVAOrion.World;

public sealed partial class Sim
{
    // ───────────────────────────── the turn ─────────────────────────────

    /// <summary>Whether the Galactic Council sits at the end of this turn.</summary>
    public bool CouncilDue => !W.Over && W.CouncilTurn >= 0 && W.Turn >= W.CouncilTurn && W.Empires.Count(e => e.Alive && !e.Monster) >= 2;

    /// <summary>
    /// Everyone's orders are given: fleets fly, fights are fought, transports land, colonies make
    /// and grow, scientists discover, treaties pay, the galaxy throws up its surprises, the Council
    /// sits if it's time, and the year turns.
    /// </summary>
    public void EndTurn(int humanVote = -1)
    {
        if (W.Over) return;
        Battles.Clear();
        Fly();
        foreach (var e in W.Empires) Survey(e.Id);
        Fights();
        Landings();
        foreach (var e in W.Empires.Where(e => e.Alive && !e.Monster).ToList())
        {
            double rp = 0;
            foreach (var s in W.Stars.Where(s => s.Owner == e.Id).ToList()) rp += RunColony(s);
            Research(e, rp);
            foreach (var o in W.Empires)
                if (o.Id != e.Id && e.Trade[o.Id]) e.Reserve += Math.Min(TotalPop(e.Id), TotalPop(o.Id)) * 0.04 * (1 + e.Kind.Diplomacy / 100.0);
            for (int i = 0; i < e.Cooldown.Length; i++)
                if (e.Cooldown[i] > 0) e.Cooldown[i]--;
        }
        foreach (var s in W.Stars) Merge(s);
        Happenings();
        if (CouncilDue) Council(humanVote);
        foreach (var e in W.Empires) CheckFallen(e.Id);
        W.Turn++;
        if (!W.Over && W.Year >= WorldState.LastYear)
        {
            var best = W.Empires.Where(e => e.Alive && !e.Monster).OrderByDescending(Score).First();
            End(-1, $"The year {WorldState.LastYear} has come, and no one rules the stars. " +
                    (best.Id == W.Player ? "Yours is the greatest of the peoples." : $"The {best.Name} stand above the rest."));
        }
    }

    public double TotalPop(int empire) => W.Stars.Where(s => s.Owner == empire).Sum(s => s.Pop);

    public int Score(Empire e) =>
        (int)TotalPop(e.Id) + W.Stars.Count(s => s.Owner == e.Id) * 10 + e.TechCount * 3 + (W.Winner == e.Id ? 500 : 0);

    // ───────────────────────────── research ─────────────────────────────

    /// <summary>What a field could study next: anything not known up to six levels past its best.</summary>
    public static List<Tech> Available(Empire e, Field f)
    {
        int top = FieldLevel(e, f) + 6;
        return Techs.All.Where(t => t.Field == f && !e.Known[t.Id] && t.Level <= top).OrderBy(t => t.Level).ToList();
    }

    /// <summary>Shares the turn's research among the fields; each that has enough makes its discovery and moves on.</summary>
    private void Research(Empire e, double rp)
    {
        for (int f = 0; f < 6; f++)
        {
            e.Progress[f] += rp * e.Alloc[f] / 100.0;
            for (int guard = 0; guard < 3; guard++)
            {
                if (e.Target[f] < 0 || e.Known[e.Target[f]]) e.Target[f] = PickTech(e, (Field)f);
                if (e.Target[f] < 0) break;
                var t = Techs.All[e.Target[f]];
                int cost = TechCost(e, t);
                if (e.Progress[f] < cost) break;
                e.Progress[f] -= cost;
                Learn(e, t.Id, null);
            }
        }
    }

    public int TechCost(Empire e, Tech t)
    {
        double f = e.Human ? 1 + W.Level * 0.1 : 1.3 - W.Level * 0.1;
        return (int)(Techs.Cost(t.Level) * f);
    }

    /// <summary>The next study for a field: the cheapest, tilted towards what this people's nature likes.</summary>
    public int PickTech(Empire e, Field f)
    {
        var options = Available(e, f);
        if (options.Count == 0) return -1;
        // Ground weapons and the like are worth less than ship and colony advances.
        var best = options.OrderBy(t => t.Level + (t.Effect == Effect.Ground ? 6 : 0) + (t.Effect == Effect.Environment && !e.Kind.Hardy ? -2 : 0) + Rng.Next(3)).First();
        return best.Id;
    }

    public void Learn(Empire e, int tech, string? from)
    {
        if (e.Known[tech]) return;
        e.Known[tech] = true;
        var t = Techs.All[tech];
        if (IsPlayer(e.Id))
        {
            string how = from is null ? $"Our {Techs.FieldNames[(int)t.Field].ToLowerInvariant()} scientists have discovered" : $"From {from} we learn";
            Post(new Event(Happen.Tech, e.Id, $"{how} {t.Name}. {t.About}", tech));
            Tell(e.Id, $"Discovered {t.Name}.", 0xFF9AD0FF);
            W.Note($"Discovered {t.Name}.");
            Play(Sfx.Discovery);
        }
    }

    // ───────────────────────────── the Galactic Council ─────────────────────────────

    /// <summary>A people's votes: one for every twenty-five of its people, and one besides.</summary>
    public int Votes(int empire) => W.Empires[empire].Alive && !W.Empires[empire].Monster ? (int)(TotalPop(empire) / 25) + 1 : 0;

    /// <summary>The two peoples the Council chooses between: the two with the most votes.</summary>
    public (int A, int B) Candidates()
    {
        var top = W.Empires.Where(e => e.Alive && !e.Monster).OrderByDescending(e => Votes(e.Id)).ThenBy(e => e.Id).Take(2).ToList();
        return (top[0].Id, top[1].Id);
    }

    /// <summary>How a computer ruler votes: for itself, or for the candidate it likes better (never one it's at war with).</summary>
    public int VoteOf(Empire e, int a, int b)
    {
        if (e.Id == a) return a;
        if (e.Id == b) return b;
        bool warA = AtWar(e.Id, a), warB = AtWar(e.Id, b);
        if (warA && warB) return -1;
        if (warA) return b;
        if (warB) return a;
        int likeA = e.Attitude[a] + (e.Treaties[a] == Treaty.Alliance ? 30 : 0), likeB = e.Attitude[b] + (e.Treaties[b] == Treaty.Alliance ? 30 : 0);
        if (Math.Max(likeA, likeB) < 10) return -1;
        return likeA >= likeB ? a : b;
    }

    /// <summary>The Council sits: each people votes; two thirds of all the votes elects a High Sovereign of the stars.</summary>
    private void Council(int humanVote)
    {
        var (a, b) = Candidates();
        int total = 0, forA = 0, forB = 0;
        var lines = new List<string>();
        foreach (var e in W.Empires.Where(e => e.Alive && !e.Monster))
        {
            int v = Votes(e.Id);
            total += v;
            int choice = IsPlayer(e.Id) ? humanVote : VoteOf(e, a, b);
            if (choice == a) forA += v;
            else if (choice == b) forB += v;
            lines.Add($"> The {e.Name}: {v} vote{(v == 1 ? "" : "s")}, {(choice == a ? "for the " + W.Empires[a].Name : choice == b ? "for the " + W.Empires[b].Name : "abstaining")}.");
        }
        int winner = forA * 3 >= total * 2 ? a : forB * 3 >= total * 2 ? b : -1;
        string summary = $"The Galactic Council has sat. The {W.Empires[a].Name} had {forA} of {total} votes, the {W.Empires[b].Name} {forB}. Two thirds are needed.";
        W.Note(winner >= 0 ? $"The Council elects the {W.Empires[winner].Name}." : "The Council fails to choose.");
        W.CouncilTurn = W.Turn + 25;
        if (winner < 0)
        {
            Post(new Event(Happen.CouncilResult, -1, summary + " No one is chosen; the Council will sit again in twenty-five years.\n" + string.Join("\n", lines), -1));
            Play(Sfx.Council);
            return;
        }
        if (winner == W.Player && W.Human.Human)
        {
            End(winner, $"The Galactic Council has chosen you, {W.Human.Ruler}, as High Sovereign of the stars. Every people bows to the {W.Human.Name}.");
            return;
        }
        if (W.Human.Human && W.Human.Alive && !W.Flags.Contains("defied"))
        {
            Post(new Event(Happen.CouncilResult, W.Player, summary + $" The {W.Empires[winner].Name} are chosen to rule the stars. Will you accept them?\n" + string.Join("\n", lines), winner));
            Play(Sfx.Council);
            return;
        }
        End(winner, $"The Galactic Council has chosen the {W.Empires[winner].Name} to rule the stars.");
    }

    /// <summary>The player bows to the Council's choice: the game ends.</summary>
    public void Accept(int winner) =>
        End(winner, $"You bow to the Council's choice. The {W.Empires[winner].Name} rule the stars, and the {W.Human.Name} serve.");

    /// <summary>The player defies the Council: every other people declares war, and only conquest can decide it now.</summary>
    public void Defy()
    {
        W.Flags.Add("defied");
        W.CouncilTurn = -1;
        foreach (var e in W.Empires.Where(e => e.Alive && !e.Monster && e.Id != W.Player)) DeclareWar(e.Id, W.Player);
        W.Note("Defied the Galactic Council.");
    }

    // ───────────────────────────── surprises ─────────────────────────────

    private void Happenings()
    {
        // The monsters: the Leviathan wanders between colonies; the Warden never leaves Aeon.
        var m = W.Monsters;
        if (m is not null)
            foreach (var f in W.Fleets.Where(f => f.Owner == m.Id && f.At >= 0 && !W.Stars[f.At].Vault).ToList())
                if (W.Turn % 8 == 0)
                {
                    var prey = W.Stars.Where(s => s.Owner >= 0 && W.Distance(s, W.Stars[f.At]) < 14).OrderBy(_ => Rng.Next(100)).FirstOrDefault();
                    if (prey is not null) Send(f, prey, retreat: true);
                }
        if (W.Turn < 20 || !Rng.Chance(3)) return;
        var colonies = W.Stars.Where(s => s.Owner >= 0 && !W.Empires[s.Owner].Monster).ToList();
        if (colonies.Count == 0) return;
        var c = colonies[Rng.Next(colonies.Count)];
        var e = W.Empires[c.Owner];
        switch (Rng.Next(4))
        {
            case 0:
                c.Pop *= 0.7;
                Tell(-1, $"A plague sweeps {c.Name} ({e.Name}).", 0xFFE0A040);
                if (IsPlayer(e.Id)) Post(new Event(Happen.Event, e.Id, $"A plague has swept through {c.Name}. Three in ten of its people are dead.", Star: c.Id));
                break;
            case 1:
            {
                int t = PickTech(e, (Field)Rng.Next(6));
                if (t < 0) break;
                Learn(e, t, "a derelict ship drifting near " + c.Name);
                break;
            }
            case 2:
                if (c.Minerals >= Minerals.UltraRich) break;
                c.Minerals++;
                if (IsPlayer(e.Id)) Post(new Event(Happen.Event, e.Id, $"Prospectors on {c.Name} have found new seams: its minerals are richer now.", Star: c.Id));
                break;
            default:
            {
                if (m is null || W.Fleets.Count(f => f.Owner == m.Id) > 2) break;
                var lair = W.Stars.Where(s => s.Owner < 0 && !s.Vault && W.Distance(s, c) < 10 && W.Distance(s, c) > 3).OrderBy(_ => Rng.Next(100)).FirstOrDefault();
                if (lair is null) break;
                var d = m.Designs.FirstOrDefault(x => x.Name == "Void Leviathan");
                if (d is null)
                {
                    d = new ShipDesign { Id = m.NextDesign++, Name = "Void Leviathan", Hull = Hull.Huge, Warp = 1, Armor = 2, Shield = 3, Computer = 3, Mounts = [new Mount(Ships.Weapon("Ion Cannon").Id, 8)] };
                    m.Designs.Add(d);
                }
                AddShips(NewFleet(m.Id, lair), d.Id, 1);
                Tell(-1, $"A Void Leviathan has been sighted near {lair.Name}!", 0xFFE05040);
                if (Sees(W.Player, lair.X, lair.Y)) Post(new Event(Happen.Event, W.Player, $"A Void Leviathan, a living ship as big as a moon, has been sighted at {lair.Name}. It hunts colonies.", Star: lair.Id));
                break;
            }
        }
    }

    /// <summary>The Vault of Aeon, its guardian beaten and its world settled, gives up its secrets.</summary>
    private void VaultOpened(Empire e)
    {
        var gifts = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var options = Techs.All.Where(t => !e.Known[t.Id] && t.Level <= FieldLevel(e, t.Field) + 15 && t.Effect != Effect.Ground).ToList();
            if (options.Count == 0) break;
            var t = options.OrderByDescending(x => x.Level).ThenBy(_ => Rng.Next(100)).First();
            e.Known[t.Id] = true;
            gifts.Add(t.Name);
        }
        W.Note($"The {e.Name} open the Vault of Aeon.");
        Tell(-1, $"The {e.Name} have opened the Vault of Aeon!", 0xFFFFD860);
        if (IsPlayer(e.Id)) Post(new Event(Happen.Vault, e.Id, $"In the Vault of Aeon our scholars find the learning of a vanished people: {string.Join(", ", gifts)}."));
    }

    // ───────────────────────────── the end ─────────────────────────────

    /// <summary>A people with no colonies and no way to make one is gone.</summary>
    public void CheckFallen(int empire)
    {
        var e = W.Empires[empire];
        if (!e.Alive || e.Monster) return;
        if (W.Stars.Any(s => s.Owner == empire)) return;
        bool hope = W.Fleets.Any(f => f.Owner == empire && (f.IsTransport || f.Ships.Any(s => e.Design(s.Design)?.Colony == true)));
        if (hope) return;
        e.Alive = false;
        W.Fleets.RemoveAll(f => f.Owner == empire);
        W.Note($"The {e.Name} are no more.");
        Tell(-1, $"The {e.Name} have been wiped out.", 0xFFE05040);
        Post(new Event(Happen.Fallen, -1, IsPlayer(empire) ? "The last of your people is gone." : $"The {e.Name} are no more.", empire));
        CheckVictory();
    }

    public void CheckVictory()
    {
        if (W.Over) return;
        if (!W.Human.Human)
        {
            // A galaxy of computer rulers (the demo): it ends when one people is left.
            var left = W.Empires.Where(e => e.Alive && !e.Monster).ToList();
            if (left.Count == 1) End(left[0].Id, $"The {left[0].Name} have conquered every rival.");
            return;
        }
        if (!W.Human.Alive)
        {
            End(-1, "Your people are gone from the stars.");
            return;
        }
        if (W.Empires.All(e => e.Id == W.Player || e.Monster || !e.Alive))
            End(W.Player, $"Every rival people has fallen. The {W.Human.Name} rule the stars alone.");
    }

    public void End(int winner, string why)
    {
        if (W.Over) return;
        W.Winner = winner;
        W.Outcome = why;
        W.Note(why);
        Post(new Event(Happen.GameOver, -1, why, winner));
    }
}
