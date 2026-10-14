namespace AVAOrion.Game;

/// <summary>How a panel is drawn beyond its title, text and options.</summary>
public enum PanelKind { Plain, Big, Colony, Battle, Wide, Design }

/// <summary>A choice on a panel. <see cref="Adjust"/> handles Left and Right on the option, if anything.</summary>
public sealed record Opt(string Label, Action? Act, Action<int>? Adjust = null, bool Enabled = true, string? Note = null);

/// <summary>
/// A menu or message over the game: a title, some paragraphs, and options to pick with the arrows and
/// Enter (or their number). Panels stack; Escape runs <see cref="Back"/>, if the panel has one. A panel
/// with an <see cref="Entry"/> takes typed words instead (a ruler's name, a design's).
/// </summary>
public sealed class Panel
{
    public string Title { get; set; } = "";
    public List<string> Text { get; } = [];
    public List<Opt> Options { get; } = [];
    public int Selected { get; set; }
    public Action? Back { get; set; }
    public PanelKind Kind { get; set; }
    /// <summary>What the panel is about (a star's id, say), for drawing it.</summary>
    public int Tag { get; set; }
    /// <summary>When set, typed letters go into this buffer; Enter hands it to <see cref="OnEntry"/>.</summary>
    public string? Entry { get; set; }
    public string Prompt { get; set; } = "";
    public Action<string>? OnEntry { get; set; }
    /// <summary>Builds the panel afresh, so a menu can show up-to-date numbers after a choice.</summary>
    public Func<Panel>? Make { get; set; }
    /// <summary>Left and Right on the panel as a whole (when the option has no adjuster): the next colony, say.</summary>
    public Action<int>? Turn { get; set; }

    public Panel Say(string text)
    {
        Text.Add(text);
        return this;
    }

    public Panel Add(string label, Action? act, bool enabled = true, string? note = null)
    {
        Options.Add(new Opt(label, act, null, enabled, note));
        return this;
    }

    public Panel AddAdjust(string label, Action<int> adjust, Action? act = null, string? note = null)
    {
        Options.Add(new Opt(label, act, adjust, true, note));
        return this;
    }
}
