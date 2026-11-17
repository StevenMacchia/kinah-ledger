using System.Windows;
using System.Windows.Media;
using KinahLedger.Parser;

namespace KinahLedger.Ui;

/// <summary>
/// One ability on a member's skills card: its name, the specialties its hits named ("3·4", empty for none), its share of the
/// member's damage ("31.2%") and against their top ability (0 to 1: the bar's length), its damage, crit and back-attack rates
/// ("34%", "–" when this ability has none while others do, empty when the card has no such column) and its hover line.
/// </summary>
public sealed record CardAbility(string Name, string Picks, double Share, double OfTop, string ShareText, string Damage, string Crit, string Back, string Tip);

/// <summary>
/// A member's skills card (the pop-up on a meter row: Steven, 2026-11-16, "when u click on the extra DPS stats per player
/// [they] display classic standard meter"): every figure the Parser's card shows, worked out once, so the Classic card and each
/// theme's card draw the same numbers in their own style. Only what the record has: nothing made up.
/// </summary>
/// <param name="Dps">"4.1K" (empty when the DPS isn't known yet).</param>
/// <param name="Share">The share of the group's damage, "32.0%".</param>
/// <param name="Damage">The damage, "1.27M".</param>
/// <param name="Crit">The crit rate ("34%"), empty without one; <see cref="CritShare"/> the crits' share of the damage, <see cref="Back"/> the share from behind.</param>
/// <param name="CritCol">The abilities carry a crit column; <see cref="BackCol"/> a back-attack column.</param>
/// <param name="More">"3 more" when more abilities dealt damage than the card lists, else empty; <see cref="MoreDamage"/> their damage.</param>
/// <param name="Hits">"1,204 hits", empty without hits; <see cref="Biggest"/> "biggest 88.1K" (never in the boss-only view),
/// <see cref="Healed"/> "healed 1.2M (2.9K/s)", <see cref="DeathsText"/> "2 deaths".</param>
/// <param name="Cp">Combat Power ("38.2K"), shown beside the name; empty when unknown.</param>
public sealed record MemberCard(Fighter F, string Name, string Class, Color ClassColor, Color NameColor, bool You,
    string Dps, string Share, string Damage, string Crit, string CritShare, string Back, bool CritCol, bool BackCol,
    IReadOnlyList<CardAbility> Abilities, string More, string MoreDamage, string Hits, string Biggest, string Healed, int Deaths, string DeathsText, string Cp)
{
    /// <summary>"4.1K DPS  ·  32.0%  ·  1.27M damage": the card's figures line, as the Classic card says it.</summary>
    public string SumsLine => string.Join("  ·  ", new[] { Dps.Length > 0 ? L.F("{0} DPS", Dps) : "", Share, L.F("{0} damage", Damage) }.Where(s => s.Length > 0));
    /// <summary>"34% crit  ·  41% of the damage from crits  ·  12% from behind", empty without rates.</summary>
    public string RatesLine => string.Join("  ·  ", new[] { Crit.Length > 0 ? L.F("{0} crit", Crit) : "", CritShare.Length > 0 ? L.F("{0} of the damage from crits", CritShare) : "", Back.Length > 0 ? L.F("{0} from behind", Back) : "" }.Where(s => s.Length > 0));
    /// <summary>"1,204 hits  ·  biggest 88.1K  ·  healed 1.2M (2.9K/s)  ·  2 deaths", empty without any.</summary>
    public string FootLine => string.Join("  ·  ", new[] { Hits, Biggest, Healed, DeathsText }.Where(s => s.Length > 0));
    /// <summary>"3 more  ·  210K", empty when every ability is listed.</summary>
    public string MoreLine => More.Length > 0 ? L.F("{0} more  ·  {1}", More, MoreDamage) : "";

    /// <summary>The card's figures for a member, or null when there's nothing to show (no damage yet).</summary>
    public static MemberCard? Of(Encounter e, Fighter f, DateTime now)
    {
        if (!e.HasDamage || e.Figure(f) <= 0) return null;
        var v = FightView.Of(e, now, null);
        if (v.Members.FirstOrDefault(m => m.F.Id == f.Id) is not { } m) return null;
        static string Pct0(double x) => (Math.Clamp(x, 0, 1) * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        bool critCol = m.Abilities.Any(x => x.Damage > 0 && x.Crit != null), backCol = m.Abilities.Any(x => x.Damage > 0 && x.Back != null);
        // the abilities with damage on the figures' basis (boss only or all targets): one with none there is not listed
        var dealt = m.Abilities.Where(x => x.Damage > 0).OrderByDescending(x => x.Damage).ToList();
        var shown = dealt.Take(ParserRows.CardAbilities).ToList();
        double topShare = shown.Count > 0 ? Math.Max(1e-9, shown[0].Share) : 1;
        var abilities = shown.Select(a => new CardAbility(a.Name, a.Picks != 0 ? KinahLedger.Parser.Abilities.PicksText(a.Picks) : "", a.Share, Math.Clamp(a.Share / topShare, 0, 1), Encounter.Pct(a.Share), Amounts.Short(a.Damage),
            critCol ? (a.Crit is { } ac ? Pct0(ac) : "–") : "", backCol ? (a.Back is { } ab ? Pct0(ab) : "–") : "",
            a.Hits > 0 ? L.F("{0:N0} hits", a.Hits) + (a.Avg is { } avg ? L.F(", {0} a hit", Amounts.Short(avg)) : "") + (!e.BossView && a.Max is { } mx ? L.F(", biggest {0}", Amounts.Short(mx)) : "") : "")).ToList();
        int more = dealt.Count - ParserRows.CardAbilities;
        int hits = dealt.Sum(a => a.Hits);
        // the biggest hit is over every target: said only when the figures are too (boss only, it could be an add's)
        string biggest = !e.BossView && m.MaxHit is { } big && big > 0 ? L.F("biggest {0}", Amounts.Short(big)) : "";
        string healed = f.Healing > 0 ? L.F("healed {0}", Amounts.Meter(f.HealingEffective)) + (e.HpsOf(f, now) is { } hps ? L.F(" ({0}/s)", Amounts.Meter(hps)) : "") : "";
        return new MemberCard(f, ParserRows.NameOf(f), f.Class, ClassArt.ColorOf(f.Class), ClassArt.NameColor(f.Class), f.You,
            m.DpsOrNull is { } dps ? Amounts.Short(dps) : "", Encounter.Pct(m.Share), Amounts.Short(m.Damage),
            m.Crit is { } cr ? Pct0(cr) : "", m.CritShare is { } cs ? Pct0(cs) : "", m.Back is { } ba ? Pct0(ba) : "", critCol, backCol,
            abilities, more > 0 ? more.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", more > 0 ? Amounts.Short(dealt.Skip(ParserRows.CardAbilities).Sum(x => x.Damage)) : "",
            hits > 0 ? L.F("{0:N0} hits", hits) : "", biggest, healed, f.Deaths, f.Deaths > 0 ? L.F(f.Deaths == 1 ? "{0} death" : "{0} deaths", f.Deaths) : "",
            f.CombatPower > 0 ? Amounts.Meter(f.CombatPower) : "");
    }
}

/// <summary>A look that draws members' skills cards in its own style (every theme look); the rest open the Parser's.</summary>
public interface IThemeCard
{
    /// <summary>The card for a member, ready to show in a pop-up beside the meter (transparent around it; its own shadow).</summary>
    FrameworkElement DrawCard(MemberCard c);
}
