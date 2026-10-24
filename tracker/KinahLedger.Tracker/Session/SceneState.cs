using System.Drawing;
using System.Drawing.Imaging;

namespace KinahLedger.Session;

/// <summary>What the game window shows, one of a small set, decided once per pass (Prompt 9).</summary>
public enum Scene { World, Bag, Crafting, Market, Reward, EventCards, Loading, Death, Cutscene, ChatExpanded, Unknown }

/// <summary>What the gate says to one reader this pass: read and count, don't read, or read and hold the read.</summary>
public enum Gate { Run, Skip, Hold }

/// <summary>
/// What the readers already know this pass, handed to the classifier beside the picture: the crafting panel's labels
/// (CraftPanel's own locator), the crafting window's signs, the Market reader, the reward screens, and the two free HUD
/// anchors (Ocr/Anchors.cs: the Kinah counter's numbers, the chat box showing lines).
/// </summary>
public sealed record SceneSignals
{
    /// <summary>The crafting panel's Success and Fail labels are up and its title says crafting (not a gathering bar).</summary>
    public bool CraftPanel { get; init; }
    /// <summary>The crafting window's skill tabs or its "acquired designs" box, seen within <see cref="WindowSeconds"/>.</summary>
    public bool CraftWindow { get; init; }
    /// <summary>The Market reader has its table.</summary>
    public bool Market { get; init; }
    /// <summary>A pass, shop or reward window's title in the top band (TargetReader.RewardOpen).</summary>
    public bool RewardTitle { get; init; }
    /// <summary>"Reward collected." over an expedition's reward table, within <see cref="WindowSeconds"/>.</summary>
    public bool RewardTable { get; init; }
    /// <summary>An event's reward screen (its cards), within <see cref="WindowSeconds"/>.</summary>
    public bool EventCards { get; init; }
    /// <summary>The Kinah counter strip's numbers were read lately (anchor kinah-strip).</summary>
    public bool CounterStrip { get; init; }
    /// <summary>The chat box showed lines lately (anchor chat-input).</summary>
    public bool ChatBox { get; init; }

    /// <summary>How long a reader's sign of a screen stands for "it is on screen now".</summary>
    public const double WindowSeconds = 5;

    /// <summary>Any sign that names a screen by itself (the reader-side evidence; the picture's rules come after).</summary>
    public bool Any => CraftPanel || CraftWindow || Market || RewardTitle || RewardTable || EventCards;

    /// <summary>
    /// The signs a whole-window picture gives by itself: today only the crafting panel's labels (CraftPanel.IsUp), with
    /// "is it crafting or gathering" taken as crafting. For tests and saved screenshots; live, the readers fill these.
    /// </summary>
    public static SceneSignals FromFrame(Bitmap frame) => new() { CraftPanel = KinahLedger.Ocr.CraftPanel.IsUp(frame) };
}

/// <summary>
/// Cheap measures of a game window thumbnail (FrameThumb, 256 px wide), computed in one go and reused by every rule:
/// brightness, how much is near-black, red tint, black bands top and bottom, edge density where the HUD sits, and how
/// flat (panel-like) the left and right of the picture are. The thumbnail's pixels are kept for a learned model (the
/// hook in <see cref="SceneState.Model"/>).
/// </summary>
public sealed class FrameStats
{
    public int W { get; private init; }
    public int H { get; private init; }
    /// <summary>The thumbnail as R, G, B bytes, row by row.</summary>
    public byte[] Rgb { get; private init; } = Array.Empty<byte>();
    /// <summary>Each pixel's brightness, 0 to 1.</summary>
    public float[] Luma { get; private init; } = Array.Empty<float>();
    public double Mean { get; private init; }
    /// <summary>Share of pixels darker than <see cref="Dark"/>.</summary>
    public double DarkShare { get; private init; }
    /// <summary>Share of pixels clearly red (red over 1.5x green and blue, not black).</summary>
    public double RedShare { get; private init; }
    /// <summary>Share of near-black pixels in the top and bottom bands (letterbox bars), and the middle's brightness.</summary>
    public double TopBlack { get; private init; }
    public double BottomBlack { get; private init; }
    public double MiddleMean { get; private init; }
    /// <summary>Edge density where the HUD sits: the Kinah counter strip (top-left), the minimap (top-right), the skill bar (bottom centre), the chat corner.</summary>
    public double StripEdges { get; private init; }
    public double MapEdges { get; private init; }
    public double BarEdges { get; private init; }
    public double ChatEdges { get; private init; }
    /// <summary>The right side (where the bag opens): share of pixels within a step of its median, the median, its edge density.</summary>
    public double RightFlat { get; private init; }
    public double RightMedian { get; private init; }
    public double RightEdges { get; private init; }
    /// <summary>The upper left (above the usual chat box, where an expanded chat reaches): flatness, median, rows of text.</summary>
    public double LeftFlat { get; private init; }
    public double LeftMedian { get; private init; }
    public int LeftTextRows { get; private init; }
    /// <summary>
    /// The three places an expanded chat must cover that nothing else covers together (2026-10-24: the rule fired 31 times
    /// in 21 minutes on dark full-screen windows and dark scenes, with no chat expanded): the top of the left column
    /// (0.15 to 0.35 of the height, where the normal chat never reaches), the normal chat's own area (0.60 to 0.92: the
    /// expanded panel continues down to its input row, text in it), and beside the panel (0.40 to 0.62 across: a panel
    /// ends, so the world beside it is brighter; a dark full-screen window is dark there too).
    /// </summary>
    public double UpperFlat { get; private init; }
    public double UpperMedian { get; private init; }
    public int UpperTextRows { get; private init; }
    public double LowFlat { get; private init; }
    public double LowMedian { get; private init; }
    public int LowTextRows { get; private init; }
    public double BesideMedian { get; private init; }

    public const float Dark = 0.08f, Black = 0.04f, EdgeStep = 0.10f, FlatStep = 0.06f;
    /// <summary>The HUD regions with at least this edge density count as drawn.</summary>
    public const double HudEdge = 0.03;
    public int HudRegions => (StripEdges >= HudEdge ? 1 : 0) + (MapEdges >= HudEdge ? 1 : 0) + (BarEdges >= HudEdge ? 1 : 0);

    // where things sit, as fractions of the window (x0, y0, x1, y1)
    static readonly RectangleF StripBox = RectangleF.FromLTRB(0f, 0f, 0.28f, 0.06f), MapBox = RectangleF.FromLTRB(0.76f, 0f, 1f, 0.14f),
        BarBox = RectangleF.FromLTRB(0.30f, 0.86f, 0.70f, 1f), ChatBox = RectangleF.FromLTRB(0f, 0.55f, 0.35f, 0.98f),
        RightBox = RectangleF.FromLTRB(0.62f, 0.12f, 0.96f, 0.85f), LeftBox = RectangleF.FromLTRB(0.02f, 0.15f, 0.36f, 0.55f),
        UpperBox = RectangleF.FromLTRB(0.02f, 0.15f, 0.36f, 0.35f), LowBox = RectangleF.FromLTRB(0.02f, 0.60f, 0.36f, 0.92f),
        BesideBox = RectangleF.FromLTRB(0.40f, 0.15f, 0.62f, 0.55f);
    const double Band = 0.11;

    /// <summary>The measures of one thumbnail.</summary>
    public static unsafe FrameStats Of(Bitmap thumb)
    {
        int w = thumb.Width, h = thumb.Height, n = w * h;
        var rgb = new byte[n * 3];
        var luma = new float[n];
        var data = thumb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        double sum = 0; int dark = 0, red = 0;
        try
        {
            for (int y = 0; y < h; y++)
            {
                byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                    rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
                    float l = (r * 0.299f + g * 0.587f + b * 0.114f) / 255f;
                    luma[i] = l; sum += l;
                    if (l < Dark) dark++;
                    if (r >= 40 && r > 1.5 * g && r > 1.5 * b) red++;
                }
            }
        }
        finally { thumb.UnlockBits(data); }

        (int x0, int y0, int x1, int y1) Box(RectangleF f) => ((int)(f.Left * w), (int)(f.Top * h), Math.Max((int)(f.Left * w) + 2, (int)Math.Ceiling(f.Right * w)), Math.Max((int)(f.Top * h) + 2, (int)Math.Ceiling(f.Bottom * h)));
        // edge density: share of pixels whose step to the right plus the step down is over EdgeStep
        double Edges(RectangleF f)
        {
            var (x0, y0, x1, y1) = Box(f);
            x1 = Math.Min(x1, w - 1); y1 = Math.Min(y1, h - 1);
            int all = 0, e = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    float l = luma[y * w + x];
                    if (Math.Abs(luma[y * w + x + 1] - l) + Math.Abs(luma[(y + 1) * w + x] - l) > EdgeStep) e++;
                    all++;
                }
            return all == 0 ? 0 : e / (double)all;
        }
        double BlackShare(int ya, int yb)
        {
            int all = 0, k = 0;
            for (int y = Math.Max(0, ya); y < Math.Min(h, yb); y++) for (int x = 0; x < w; x++) { all++; if (luma[y * w + x] < Black) k++; }
            return all == 0 ? 0 : k / (double)all;
        }
        double MeanOf(int ya, int yb)
        {
            int all = 0; double s = 0;
            for (int y = Math.Max(0, ya); y < Math.Min(h, yb); y++) for (int x = 0; x < w; x++) { all++; s += luma[y * w + x]; }
            return all == 0 ? 0 : s / all;
        }
        // flatness: the median brightness of a region (a 64-step histogram) and the share of pixels within FlatStep of it
        (double Flat, double Median) Flatness(RectangleF f)
        {
            var (x0, y0, x1, y1) = Box(f);
            x1 = Math.Min(x1, w); y1 = Math.Min(y1, h);
            var hist = new int[64]; int all = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { hist[Math.Min(63, (int)(luma[y * w + x] * 64))]++; all++; }
            if (all == 0) return (0, 0);
            int acc = 0, mb = 0;
            for (; mb < 63; mb++) { acc += hist[mb]; if (acc * 2 >= all) break; }
            double med = (mb + 0.5) / 64;
            int near = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (Math.Abs(luma[y * w + x] - med) <= FlatStep) near++;
            return (near / (double)all, med);
        }
        // rows of text in a region: rows where at least a tenth of the pixels step sideways by more than EdgeStep
        int TextRows(RectangleF f)
        {
            var (x0, y0, x1, y1) = Box(f);
            x1 = Math.Min(x1, w - 1); y1 = Math.Min(y1, h);
            int rows = 0;
            for (int y = y0; y < y1; y++)
            {
                int e = 0;
                for (int x = x0; x < x1; x++) if (Math.Abs(luma[y * w + x + 1] - luma[y * w + x]) > EdgeStep) e++;
                if (x1 > x0 && e >= (x1 - x0) / 10.0) rows++;
            }
            return rows;
        }

        int band = Math.Max(1, (int)Math.Round(h * Band));
        var (rf, rm) = Flatness(RightBox);
        var (lf, lm) = Flatness(LeftBox);
        var (uf, um) = Flatness(UpperBox);
        var (wf, wm) = Flatness(LowBox);
        var (_, bm) = Flatness(BesideBox);
        return new FrameStats
        {
            W = w, H = h, Rgb = rgb, Luma = luma,
            Mean = sum / n, DarkShare = dark / (double)n, RedShare = red / (double)n,
            TopBlack = BlackShare(0, band), BottomBlack = BlackShare(h - band, h), MiddleMean = MeanOf((int)(h * 0.2), (int)(h * 0.8)),
            StripEdges = Edges(StripBox), MapEdges = Edges(MapBox), BarEdges = Edges(BarBox), ChatEdges = Edges(ChatBox),
            RightFlat = rf, RightMedian = rm, RightEdges = Edges(RightBox),
            LeftFlat = lf, LeftMedian = lm, LeftTextRows = TextRows(LeftBox),
            UpperFlat = uf, UpperMedian = um, UpperTextRows = TextRows(UpperBox),
            LowFlat = wf, LowMedian = wm, LowTextRows = TextRows(LowBox), BesideMedian = bm,
        };
    }

    public override string ToString() =>
        $"mean {Mean:0.00}, dark {DarkShare:P0}, red {RedShare:P0}, bands {TopBlack:P0}/{BottomBlack:P0} middle {MiddleMean:0.00}, " +
        $"HUD edges strip {StripEdges:0.00} map {MapEdges:0.00} bar {BarEdges:0.00} chat {ChatEdges:0.00}, " +
        $"right flat {RightFlat:P0} at {RightMedian:0.00}, left flat {LeftFlat:P0} at {LeftMedian:0.00} with {LeftTextRows} text rows, " +
        $"upper flat {UpperFlat:P0} at {UpperMedian:0.00} with {UpperTextRows} rows, low flat {LowFlat:P0} at {LowMedian:0.00} with {LowTextRows} rows, beside {BesideMedian:0.00}";

    /// <summary>
    /// An expanded chat: one flat dark panel, darker than the frame, with text, that reaches from the top of the left
    /// column (where the normal chat never is) down through the normal chat's own area, with brighter world beside it.
    /// The rule before 2026-10-24 asked only the left column (0.15 to 0.55 of the height) and fired on dark full-screen
    /// windows (the event's card screen, the arena) and dark scenes; <see cref="ExpandedChatOld"/> keeps it for the tests.
    /// </summary>
    public bool ExpandedChat =>
        ExpandedChatOld
        && UpperFlat >= 0.5 && UpperMedian < 0.25 && UpperTextRows >= 2
        && LowFlat >= 0.5 && Math.Abs(LowMedian - LeftMedian) <= FlatStep && LowTextRows >= 4
        && BesideMedian >= LeftMedian + FlatStep;

    /// <summary>The rule as it was (the left column alone): kept so the tests show which frames it misfired on.</summary>
    public bool ExpandedChatOld => LeftFlat >= 0.5 && LeftMedian < 0.25 && LeftMedian <= 0.6 * Mean && LeftTextRows >= 4;
}

/// <summary>
/// One classifier per captured frame (Prompt 9, 2026-10-24): what the game window shows, as one <see cref="Scene"/>,
/// from cheap features only: the readers' own signs (<see cref="SceneSignals"/>: the crafting panel's locator, the
/// crafting window, the Market reader, the reward screens, the HUD anchors) and a 256 px thumbnail's measures
/// (<see cref="FrameStats"/>: brightness, black bands, red tint, HUD edges, panel flatness). Rules, in order: a reader's
/// sign names its screen; then black bands are a cutscene, a near-black frame with no HUD a loading screen, a mostly red
/// frame death; with the HUD up, a flat dark panel with text rows top-left is the chat expanded, a flat dark panel on
/// the right the bag; the HUD alone is the world; nothing explained is Unknown.
///
/// Hysteresis: a state takes over only when seen on two passes in a row; a one-pass flicker is ignored. The gate is the
/// stricter of the settled state and this pass's own verdict, so a reader is never let through by a state the picture
/// has just left. Each reader declares the states it is valid in (<see cref="Table"/>); TrackerSession.Loop asks before
/// each one. Reads taken while Unknown are held and released only when the next settled state would allow them.
///
/// Until a first frame is seen (replays, the replay bank's text cases, a capture that never works) nothing is gated:
/// every reader runs exactly as before the scene existed, under its own exclusion rules, which all stay in place.
/// </summary>
public sealed class SceneState
{
    /// <summary>The settled state (seen on two passes in a row).</summary>
    public Scene Current { get; private set; } = Scene.Unknown;
    /// <summary>When <see cref="Current"/> took over.</summary>
    public DateTime Since { get; private set; } = DateTime.MinValue;
    /// <summary>This pass's own verdict, before hysteresis.</summary>
    public Scene Raw { get; private set; } = Scene.Unknown;
    /// <summary>False until the first frame: until then nothing is gated.</summary>
    public bool HasFrame { get; private set; }
    /// <summary>The last frame's measures (null when the last pass had none).</summary>
    public FrameStats? LastStats { get; private set; }
    string _why = "no frame yet", _rawWhy = "no frame yet";
    Scene? _pending;
    bool _hudSeen;

    /// <summary>Gating on: remote config scene.gate (default on). Off: the state is still worked out and logged.</summary>
    public bool Enabled => Update.RemoteConfig.Current.SceneGate;

    // ---- the hook for a learned classifier ----
    /// <summary>
    /// HOOK (Prompt 9): a small image classifier can replace the picture rules here. Given the thumbnail's measures (its
    /// pixels in <see cref="FrameStats.Rgb"/>, <see cref="FrameStats.W"/> by <see cref="FrameStats.H"/>) and the readers'
    /// signs, return a state and a confidence 0 to 1, or null to leave it to the rules. Taken only at
    /// <see cref="ModelSure"/> or above, and never over a reader's own sign (crafting panel, Market, reward screens), which
    /// stays first. Null today: the rules decide.
    /// </summary>
    public static Func<FrameStats, SceneSignals, (Scene State, double Confidence)?>? Model { get; set; }
    public const double ModelSure = 0.9;

    // ---- the readers' validity table ----
    /// <summary>The columns of <see cref="Table"/>, in order.</summary>
    public static readonly Scene[] Columns = (Scene[])Enum.GetValues(typeof(Scene));

    /// <summary>
    /// Which states each reader is valid in: one character per state, in the order of <see cref="Scene"/>. A column
    /// applies only while its state may gate (<see cref="Gates"/>, remote config scene.gate.states); a log-only state
    /// runs every reader.
    /// '+' runs, '-' is skipped, 'h' reads and holds (Unknown only, for reads that can wait). Readers not listed always
    /// run (anchors, reward pictures; in the watcher, the Market reader and the Kinah counter, which are themselves the
    /// Market's and the HUD's witnesses). Detectors (target bar, zone) run everywhere: they are where the signs come from.
    /// </summary>
    public static readonly (string Reader, string Cells, string Why)[] Table =
    {
        //                World Bag Craft Market Reward Event Load Death Cut ChatX Unknown
        ("chat",          "++++++-+-+h", "the chat box; hidden on loading screens and in cutscenes"),
        ("loot_list",     "++--------h", "rows beside the character: only in the world (a panel, a shop, the cards or an expanded chat sit over its band)"),
        ("craft_panel",   "--+--------", "crafts count only from the crafting panel's own race"),
        ("gather_bar",    "++-------++", "the same bars on a gathering node: never while crafting"),
        ("nameplates",    "++-------+-", "enemies' health bars around the character: the world only"),
        ("reward_grid",   "++--+++++++", "its own \"Reward collected.\" title is its evidence: off only in Crafting and Market, which a run never shows"),
        ("header",        "++---+---++", "the event header under the minimap: a witness for the run"),
        ("target",        "+++++++++++", "a detector (reward titles, crafting tabs, the enemy on the bar): always"),
        ("zone",          "+++++++++++", "a detector (the minimap): always"),
    };

    /// <summary>What <paramref name="reader"/> may do in state <paramref name="s"/>.</summary>
    public static Gate Allowed(string reader, Scene s)
    {
        foreach (var (r, cells, _) in Table)
            if (r == reader) return cells[(int)s] switch { '+' => Gate.Run, 'h' => Gate.Hold, _ => Gate.Skip };
        return Gate.Run;
    }

    /// <summary>
    /// The gate for <paramref name="reader"/> this pass: the stricter of the settled state and this pass's own verdict
    /// (skip beats hold beats run). Everything runs before the first frame and while scene.gate is off.
    /// </summary>
    public Gate GateFor(string reader)
    {
        if (!HasFrame || !Enabled) return Gate.Run;
        var a = Effective(reader, Current, _beforeUnknown); var b = Effective(reader, Raw, Current);
        if (a == Gate.Skip || b == Gate.Skip) return Gate.Skip;
        if (a == Gate.Hold || b == Gate.Hold) return Gate.Hold;
        return Gate.Run;
    }

    /// <summary>
    /// Whether state <paramref name="s"/> may gate (remote config scene.gate.states; World always). A state that may not
    /// is still classified and logged, and every reader runs in it as it does without the scene.
    /// </summary>
    public static bool Gates(Scene s) => s == Scene.World || (s != Scene.Unknown && Update.RemoteConfig.Current.SceneGateStates.Contains(s.ToString()));

    /// <summary>
    /// What <paramref name="s"/> says to a reader: its table cell when it may gate, Run when it may not. Unknown holds
    /// only when the settled state before it (<paramref name="before"/>) was one of the listed gating states.
    /// </summary>
    static Gate Effective(string reader, Scene s, Scene before) =>
        s == Scene.Unknown ? (before is not (Scene.Unknown or Scene.World) && Gates(before) ? Allowed(reader, s) : Gate.Run)
        : Gates(s) ? Allowed(reader, s) : Gate.Run;

    /// <summary>Whether the settled state gates anything now (false: log-only).</summary>
    public bool CurrentGates => Current == Scene.Unknown ? _beforeUnknown is not (Scene.Unknown or Scene.World) && Gates(_beforeUnknown) : Gates(Current);
    Scene _beforeUnknown = Scene.Unknown;

    /// <summary>Settled: the state and this pass agree on something other than Unknown (held reads are judged then).</summary>
    public bool Settled => HasFrame && Current != Scene.Unknown && Raw != Scene.Unknown;

    /// <summary>Which features decided the settled state, and this pass's verdict when it differs.</summary>
    public string Explain() =>
        $"{Current}: {_why}" + (Raw != Current ? $" · this pass {Raw}: {_rawWhy}" + (_pending == Raw ? " (once; takes over if seen again)" : "") : "");

    // ---- the rules ----
    /// <summary>A reader's own sign names its screen: these come before any picture rule.</summary>
    static (Scene, string)? BySign(SceneSignals s) =>
        s.EventCards ? (Scene.EventCards, "the event reward screen's title")
        : s.RewardTable ? (Scene.Reward, "\"Reward collected.\" over the reward table")
        : s.RewardTitle ? (Scene.Reward, "a pass, shop or reward window's title in the top band")
        : s.Market ? (Scene.Market, "the Market reader has its table")
        : s.CraftPanel ? (Scene.Crafting, "the crafting panel's Success and Fail labels, titled crafting")
        : s.CraftWindow ? (Scene.Crafting, "the crafting window's tabs or its acquired-designs box")
        : null;

    /// <summary>The rules on one frame's measures and signs, without hysteresis. Uses only whether the HUD was seen before.</summary>
    public (Scene State, string Why) Classify(FrameStats? f, SceneSignals s)
    {
        if (BySign(s) is { } sign) return sign;
        if (f == null) return (Scene.Unknown, "no frame");
        if (Model is { } m)
            try { if (m(f, s) is { } g && g.Confidence >= ModelSure) return (g.State, $"the model, {g.Confidence:0.00}"); }
            catch (Exception ex) { CrashLog.Write("scene model", ex); }
        bool quietTop = f.StripEdges < 0.01 && f.MapEdges < 0.01;
        if (f.TopBlack >= 0.97 && f.BottomBlack >= 0.97 && f.MiddleMean >= 0.08)
            return (Scene.Cutscene, $"black bands top and bottom ({f.TopBlack:P0}, {f.BottomBlack:P0} black), the middle lit ({f.MiddleMean:0.00})");
        if (f.DarkShare >= 0.85 && f.Mean < 0.08 && quietTop)
            return (Scene.Loading, $"near-black ({f.DarkShare:P0} dark, mean {f.Mean:0.00}) and no HUD where the counter and minimap sit");
        if (f.RedShare >= 0.5 && f.Mean < 0.3)
            return (Scene.Death, $"dark and red ({f.RedShare:P0} red, mean {f.Mean:0.00})");
        bool hud = s.CounterStrip || s.ChatBox || f.HudRegions >= 2;
        string hudWhy = s.CounterStrip ? "the Kinah counter read" : s.ChatBox ? "the chat box showing lines" : $"the HUD drawn in {f.HudRegions} of 3 places";
        // a panel is flat, dark, and darker than the rest of the picture (a dark scene is dark everywhere, not in one panel)
        // the expanded chat: the panel reaches the top of the left column and down through the chat's own area, world beside it
        if (hud && f.ExpandedChat)
            return (Scene.ChatExpanded, $"{hudWhy}, and a flat dark panel with {f.LeftTextRows} text rows from the top of the left column down to the chat box ({f.LeftFlat:P0} flat at {f.LeftMedian:0.00}, its top {f.UpperTextRows} rows, the chat's area {f.LowTextRows} rows at {f.LowMedian:0.00}, beside it {f.BesideMedian:0.00}, the frame {f.Mean:0.00})");
        if (hud && f.RightFlat >= 0.6 && f.RightMedian < 0.25 && f.RightMedian <= 0.6 * f.Mean && f.RightEdges >= 0.01)
            return (Scene.Bag, $"{hudWhy}, and a flat dark panel on the right ({f.RightFlat:P0} flat at {f.RightMedian:0.00}, the frame {f.Mean:0.00})");
        if (hud) return (Scene.World, hudWhy);
        if (!_hudSeen) return (Scene.World, "no HUD seen yet this session, so taken as the world (as before the scene existed)");
        return (Scene.Unknown, $"no HUD and no rule fits ({f.HudRegions} of 3 HUD places drawn, mean {f.Mean:0.00})");
    }

    // ---- one pass ----
    /// <summary>Passes, flickers and cost, for the minute line.</summary>
    int _passes, _flickers; double _classifyMs, _pictureMs; int _pictures; DateTime _costAt = DateTime.MinValue;
    public double LastClassifyMs { get; private set; }

    /// <summary>The picture's capture time, for the minute line (the classifier's own cost is measured in Observe).</summary>
    public void AddPictureMs(double ms) { _pictureMs += ms; _pictures++; }

    /// <summary>One pass from a thumbnail (null: none this pass). Returns true when the settled state changed.</summary>
    public bool Observe(Bitmap? thumb, SceneSignals s, DateTime now)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        FrameStats? f = null;
        if (thumb != null) try { f = FrameStats.Of(thumb); } catch (Exception ex) { CrashLog.Write("scene stats", ex); }
        return Observe(f, s, now, sw);
    }

    /// <summary>One pass from measures already taken (tests, replays of pictures).</summary>
    public bool Observe(FrameStats? f, SceneSignals s, DateTime now) => Observe(f, s, now, System.Diagnostics.Stopwatch.StartNew());

    bool Observe(FrameStats? f, SceneSignals s, DateTime now, System.Diagnostics.Stopwatch sw)
    {
        LastStats = f;
        Scene raw; string why;
        if (f != null)
        {
            HasFrame = true;
            (raw, why) = Classify(f, s);
            if (s.CounterStrip || s.ChatBox || f.HudRegions >= 2) _hudSeen = true;
        }
        else if (!HasFrame) { sw.Stop(); return false; }   // never a frame: nothing to say, nothing gated
        else if (BySign(s) is { } sign) (raw, why) = sign;
        else (raw, why) = (Current, "no frame this pass, so unchanged");
        Raw = raw; _rawWhy = why;
        bool changed = false;
        if (raw == Current) { if (_pending != null) _flickers++; _pending = null; _why = why; }
        else if (_pending == raw)
        {
            if (raw == Scene.Unknown) _beforeUnknown = Current;
            Current = raw; Since = now; _why = why; _pending = null; changed = true;
        }
        else { if (_pending != null) _flickers++; _pending = raw; }
        sw.Stop();
        LastClassifyMs = sw.Elapsed.TotalMilliseconds;
        _classifyMs += LastClassifyMs; _passes++;
        if (changed) CrashLog.Note(ChangeLine());
        if (_costAt == DateTime.MinValue) _costAt = now;
        else if ((now - _costAt).TotalSeconds >= 60)
        {
            CrashLog.Note($"scene: {_passes} passes, classifier {(_passes > 0 ? _classifyMs / _passes : 0):0.00} ms each, picture {(_pictures > 0 ? _pictureMs / _pictures : 0):0.0} ms each, {_flickers} one-pass flicker(s) ignored; {Current} since {Since.ToLocalTime():HH:mm:ss}{(Enabled ? "" : " (scene.gate off: nothing gated)")}");
            _costAt = now; _passes = 0; _classifyMs = 0; _pictureMs = 0; _pictures = 0; _flickers = 0;
        }
        return changed;
    }

    /// <summary>Readers the state <paramref name="s"/> skips and holds, from the table.</summary>
    public static (List<string> Skipped, List<string> Held) Effect(Scene s) =>
        (Table.Where(t => Allowed(t.Reader, s) == Gate.Skip).Select(t => t.Reader).ToList(), Table.Where(t => Allowed(t.Reader, s) == Gate.Hold).Select(t => t.Reader).ToList());

    /// <summary>The line logged once per state change: "scene &lt;state&gt; since &lt;t&gt;; skipped &lt;reader,...&gt;".</summary>
    public string ChangeLine()
    {
        if (!CurrentGates) return $"scene {Current} (log-only) since {Since.ToLocalTime():HH:mm:ss}; gates nothing, not in scene.gate.states ({_why})";
        var (skipped, held) = Effect(Current);
        string list = skipped.Count > 0 ? string.Join(",", skipped) : "none";
        string gate = Enabled ? $"skipped {list}" + (held.Count > 0 ? $"; held {string.Join(",", held)}" : "")
                              : $"skipped none (scene.gate off; would skip {list})";
        return $"scene {Current} since {Since.ToLocalTime():HH:mm:ss}; {gate} ({_why})";
    }
}
