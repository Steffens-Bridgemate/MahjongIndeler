using System.Diagnostics;
using System.Text;
using System.Text.Json;

// ═══════════════════════════════════════════════════════════════════════════
// Offline analysis of the table-assignment algorithm (TableAssignmentService).
//
// Walk-forward replay: every historical session is regenerated from only the
// history that existed at that moment, RUNS times per algorithm variant.
// Variants are option sets over one parameterized algorithm, so the historical
// iterations (OLD/FIXED/TIERED), the current live algorithm and candidate
// improvements can be compared on identical data.
//
// Usage: dotnet run [export.json]   (plain or base64-encoded app export)
// ═══════════════════════════════════════════════════════════════════════════

var path = args.Length > 0 ? args[0] : "testdata.json";
var raw = File.ReadAllText(path);
var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
string json;
try
{
    JsonDocument.Parse(raw);
    json = raw;
}
catch (JsonException)
{
    // The app's export download can be base64-encoded JSON
    json = Encoding.UTF8.GetString(Convert.FromBase64String(raw.Trim()));
}
var data = JsonSerializer.Deserialize<ExportData>(json, jsonOpts)!;

var members = data.Members;
var nameMap = members.ToDictionary(m => m.Id, m => m.Name);
var extraCounts = members.ToDictionary(m => m.Id, m => m.ExtraThreePlayerTableCount);
var ordered = data.Sessions
    .OrderBy(s => s.Date.Date)
    .ThenBy(s => s.StartTime ?? TimeSpan.Zero)
    .ToList();

Console.WriteLine($"Loaded {members.Count} members, {ordered.Count} sessions " +
                  $"({ordered.First().Date:yyyy-MM-dd} .. {ordered.Last().Date:yyyy-MM-dd}) from {path}");

// ─── Regen mode: `<input.json> --regen <output.json>` or `--regen2 <output.json>` ───
// Walk-forward rewrite of every non-excluded session's tables with the LIVE algorithm
// (post-ratio + tiered selection + swap local search + same-day penalty), each regenerated
// session feeding the next one's history. Only the Tables arrays change in the output file
// (scores are cleared for regenerated sessions); everything else is preserved verbatim,
// so the result imports straight back into the app.
// --regen2 additionally simulates the September format: every regenerated evening gets a
// SECOND hanchan (same attendance, +90 min, new Guid) generated against the first one.
var regen2Flag = Array.IndexOf(args, "--regen2");
var twoHanchans = regen2Flag >= 0;
var regenFlag = twoHanchans ? regen2Flag : Array.IndexOf(args, "--regen");
if (regenFlag >= 0)
{
    var outPath = args[regenFlag + 1];
    // Mirrors the live service; pass --linear to A/B against the pre-convex cost
    var convex = Array.IndexOf(args, "--linear") < 0;
    var live = new Options(PostRatio: true, Sel: Selection.Tiered, Attempts: 5, TrueCoAtt: false, Swap: true, SameDayPenalty: 10, ConvexCost: convex);
    var doc = System.Text.Json.Nodes.JsonNode.Parse(json)!;
    var sessionsNode = (System.Text.Json.Nodes.JsonArray)doc["Sessions"]!;

    var working = new List<Session>();
    var regeneratedCount = 0;

    static System.Text.Json.Nodes.JsonArray BuildTablesArray(List<List<Guid>> tables)
    {
        var tablesArr = new System.Text.Json.Nodes.JsonArray();
        var tableNumber = 1;
        foreach (var g in tables)
        {
            var ids = new System.Text.Json.Nodes.JsonArray();
            foreach (var id in g) ids.Add(id.ToString());
            tablesArr.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["TableNumber"] = tableNumber++,
                ["PlayerIds"] = ids,
                ["PlayerCount"] = g.Count,
                ["Score"] = null,
                ["Scanned"] = false,
            });
        }
        return tablesArr;
    }

    foreach (var s in ordered)
    {
        if (s.ExcludeFromOptimization || s.PresentMemberIds.Count < 3)
        {
            working.Add(s);
            continue;
        }

        List<Session> HistoryFor() => working
            .Where(h => h.Date.Date == s.Date.Date || !h.ExcludeFromOptimization)
            .ToList();

        var sim = new Sim(s.PresentMemberIds, HistoryFor(), extraCounts, s.Date.Date);
        var tables = sim.Run(live);
        working.Add(s with { Tables = tables.Select((g, i) => new Table(i + 1, g)).ToList() });
        regeneratedCount++;

        var node = sessionsNode.First(n => Guid.Parse((string)n!["Id"]!) == s.Id)!;
        node["Tables"] = BuildTablesArray(tables);

        if (twoHanchans)
        {
            // Second hanchan of the evening: same attendance, generated with hanchan 1 in history
            var sim2 = new Sim(s.PresentMemberIds, HistoryFor(), extraCounts, s.Date.Date);
            var tables2 = sim2.Run(live);
            var h2Id = Guid.NewGuid();
            var h2Start = (s.StartTime ?? TimeSpan.Zero) + TimeSpan.FromMinutes(90);
            working.Add(s with
            {
                Id = h2Id,
                StartTime = h2Start,
                Tables = tables2.Select((g, i) => new Table(i + 1, g)).ToList(),
            });

            var node2 = node.DeepClone();
            node2["Id"] = h2Id.ToString();
            node2["StartTime"] = h2Start.ToString(@"hh\:mm\:ss");
            node2["Tables"] = BuildTablesArray(tables2);
            sessionsNode.Insert(sessionsNode.IndexOf(node) + 1, node2);
        }
    }

    doc["ExportDate"] = DateTime.Now.ToString("o");
    File.WriteAllText(outPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    (int met2Plus, int maxMeet, int sameDayRepeats, int met3, int met4Plus) SeasonStats(List<Session> sessions)
    {
        var m = new Dictionary<string, int>();
        var sameDay = 0;
        foreach (var byDate in sessions.GroupBy(s => s.Date.Date))
        {
            var dayPairs = new Dictionary<string, int>();
            foreach (var s in byDate)
                foreach (var t in s.Tables)
                    for (int i = 0; i < t.PlayerIds.Count; i++)
                        for (int j = i + 1; j < t.PlayerIds.Count; j++)
                        {
                            var a = t.PlayerIds[i].ToString();
                            var b = t.PlayerIds[j].ToString();
                            var k = string.CompareOrdinal(a, b) < 0 ? $"{a}_{b}" : $"{b}_{a}";
                            m[k] = m.GetValueOrDefault(k, 0) + 1;
                            dayPairs[k] = dayPairs.GetValueOrDefault(k, 0) + 1;
                        }
            sameDay += dayPairs.Count(kv => kv.Value >= 2);
        }
        return (m.Count(kv => kv.Value >= 2), m.Count == 0 ? 0 : m.Values.Max(), sameDay, m.Count(kv => kv.Value == 3), m.Count(kv => kv.Value >= 4));
    }

    var (origRepeats, origMax, origSameDay, origMet3, origMet4) = SeasonStats(ordered);
    var (newRepeats, newMax, newSameDay, newMet3, newMet4) = SeasonStats(working);
    Console.WriteLine($"Regenerated {regeneratedCount} of {ordered.Count} sessions" +
                      $"{(twoHanchans ? $" (+{regeneratedCount} second hanchans)" : "")}" +
                      $"{(convex ? "" : " [linear cost]")} -> {outPath}");
    Console.WriteLine($"  pairs that met 2+ times over the season: {origRepeats} -> {newRepeats}");
    Console.WriteLine($"  pairs at exactly 3 / at 4+ meetings:     {origMet3} / {origMet4} -> {newMet3} / {newMet4}");
    Console.WriteLine($"  max meetings of any pair:                {origMax} -> {newMax}");
    Console.WriteLine($"  same-day repeat pairs (whole season):    {origSameDay} -> {newSameDay}");
    return;
}

const int Runs = 100;

var variants = new (string Name, Options O)[]
{
    ("OLD       pre-ratio, simple take, 1 attempt",   new Options(PostRatio: false, Sel: Selection.SimpleTake,        Attempts: 1,  TrueCoAtt: false, Swap: false, SameDayPenalty: 0)),
    ("FIXED     pre-ratio, att-tiebreak, 5 attempts", new Options(PostRatio: false, Sel: Selection.AttendanceTiebreak, Attempts: 5,  TrueCoAtt: false, Swap: false, SameDayPenalty: 0)),
    ("TIERED    pre-ratio, tiers (live until today)", new Options(PostRatio: false, Sel: Selection.Tiered,            Attempts: 5,  TrueCoAtt: false, Swap: false, SameDayPenalty: 0)),
    ("CURRENT   post-ratio, tiers (live now)",        new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 5,  TrueCoAtt: false, Swap: false, SameDayPenalty: 0, ConvexCost: true)),
    ("C+COATT   + true co-attendance denominator",    new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 5,  TrueCoAtt: true,  Swap: false, SameDayPenalty: 0)),
    ("C+SWAP    + swap local search",                 new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 5,  TrueCoAtt: false, Swap: true,  SameDayPenalty: 0)),
    ("C+SAMEDAY + same-day repeat penalty",           new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 5,  TrueCoAtt: false, Swap: false, SameDayPenalty: 10)),
    ("C+20      + 20 attempts",                       new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 20, TrueCoAtt: false, Swap: false, SameDayPenalty: 0)),
    ("BEST      co-att + swap + same-day + 20",       new Options(PostRatio: true,  Sel: Selection.Tiered,            Attempts: 20, TrueCoAtt: true,  Swap: true,  SameDayPenalty: 10, ConvexCost: true)),
};

var agg = variants.ToDictionary(v => v.Name, _ => new Agg());

for (int idx = 0; idx < ordered.Count; idx++)
{
    var target = ordered[idx];
    if (target.PresentMemberIds.Count < 3) continue;

    // History = strictly earlier sessions, filtered by the live exclusion rules:
    // same-date always counts; other dates only when both sides are non-excluded.
    var history = ordered.Take(idx)
        .Where(s => s.Date.Date == target.Date.Date
                 || (!target.ExcludeFromOptimization && !s.ExcludeFromOptimization))
        .ToList();

    var sim = new Sim(target.PresentMemberIds, history, extraCounts, target.Date.Date);

    Console.WriteLine($"\n=== Session {idx + 1}/{ordered.Count}: {target.Date:yyyy-MM-dd} " +
                      $"{(target.StartTime.HasValue ? target.StartTime.Value.ToString(@"hh\:mm") : "--:--")} — " +
                      $"{target.PresentMemberIds.Count} players → {sim.FourTables}x4 + {sim.ThreeTables}x3, " +
                      $"history {history.Count}, 3-eligible {sim.EligibleCount}/{target.PresentMemberIds.Count} ===");

    foreach (var (name, o) in variants)
    {
        var results = new List<SimResult>(Runs);
        var sw = Stopwatch.StartNew();
        for (int r = 0; r < Runs; r++)
            results.Add(sim.Score(sim.Run(o)));
        sw.Stop();

        agg[name].Add(results, sw.ElapsedMilliseconds);

        Console.WriteLine($"  {name,-48} opt {results.Count(r => r.ThreePlayerOptimal),3}/{Runs}  " +
                          $"var {results.Average(r => r.ThreeVariance):F4}  " +
                          $"meet {results.Average(r => r.MeetingScore):F2}  " +
                          $"maxPair {results.Average(r => r.MaxPairMeetings):F2}  " +
                          $"newc {results.Count(r => r.NewcomersAtThree > 0),2}  " +
                          $"viol {results.Sum(r => r.HardViolations),2}  " +
                          $"sameday {results.Sum(r => r.SameDayRepeats),3}  " +
                          $"unseated {results.Max(r => r.Unseated)}");
    }
}

// ─── Overall summary ───
Console.WriteLine($"\n{new string('═', 118)}");
Console.WriteLine($" OVERALL — all sessions x {Runs} runs   " +
                  "(opt%: post-ratio order correct | var: 3-ratio variance | meet: repeat-meeting score | " +
                  "newc%: runs with a newcomer at a 3-table | viol: hard-constraint violations | sameday: same-day repeat pairs)");
Console.WriteLine(new string('═', 118));
Console.WriteLine($"  {"variant",-48} {"opt%",6} {"var",8} {"meet",8} {"maxPair",8} {"newc%",6} {"viol",5} {"sameday",8} {"ms",6}");
foreach (var (name, _) in variants)
{
    var a = agg[name];
    Console.WriteLine($"  {name,-48} {100.0 * a.Optimal / a.Runs,5:F1}% {a.SumVariance / a.Runs,8:F4} " +
                      $"{a.SumMeeting / a.Runs,8:F2} {a.SumMaxPair / a.Runs,8:F2} " +
                      $"{100.0 * a.NewcomerRuns / a.Runs,5:F1}% {a.HardViolations,5} {a.SameDayRepeats,8} {a.Ms,6}");
}

// ─── PHASE 2 — September scenario: two hanchans per evening, same attendance ───
// Hanchan 1 is generated, appended to history as a finalized same-date session,
// then hanchan 2 is generated from that. Key metrics: same-day repeat pairs and
// double 3-duty within the evening (the "viol" column: 3-duty in both hanchans).
const int Runs2 = 50;
var agg2 = variants.ToDictionary(v => v.Name, _ => new Agg());

for (int idx = 0; idx < ordered.Count; idx++)
{
    var target = ordered[idx];
    if (target.PresentMemberIds.Count < 6) continue; // need at least 2 tables for hanchan 2 to differ
    var history = ordered.Take(idx)
        .Where(s => s.Date.Date == target.Date.Date
                 || (!target.ExcludeFromOptimization && !s.ExcludeFromOptimization))
        .ToList();
    var sim1 = new Sim(target.PresentMemberIds, history, extraCounts, target.Date.Date);

    foreach (var (name, o) in variants)
    {
        for (int r = 0; r < Runs2; r++)
        {
            var h1 = sim1.Run(o);
            var h1Session = new Session(
                Guid.NewGuid(), target.Date,
                (target.StartTime ?? TimeSpan.Zero) + TimeSpan.FromMinutes(90),
                target.PresentMemberIds,
                h1.Select((g, i) => new Table(i + 1, g)).ToList(),
                IsFinalized: true, target.ExcludeFromOptimization);
            var sim2 = new Sim(target.PresentMemberIds, history.Append(h1Session).ToList(),
                extraCounts, target.Date.Date);
            agg2[name].AddOne(sim2.Score(sim2.Run(o)));
        }
    }
}

Console.WriteLine($"\n{new string('═', 118)}");
Console.WriteLine($" SEPTEMBER SCENARIO — hanchan 2 generated after hanchan 1, per evening x {Runs2} runs");
Console.WriteLine(" (sameday/run: avg pairs meeting AGAIN in hanchan 2 | zero%: evenings without any repeat | viol: double 3-duty in one evening)");
Console.WriteLine(new string('═', 118));
Console.WriteLine($"  {"variant",-48} {"opt%",6} {"sameday/run",12} {"zero%",6} {"viol",5} {"meet",8}");
foreach (var (name, _) in variants)
{
    var a = agg2[name];
    Console.WriteLine($"  {name,-48} {100.0 * a.Optimal / a.Runs,5:F1}% {(double)a.SameDayRepeats / a.Runs,12:F2} " +
                      $"{100.0 * a.ZeroSameDayRuns / a.Runs,5:F1}% {a.HardViolations,5} {a.SumMeeting / a.Runs,8:F2}");
}

// ─── Detail: newest session, per-player 3-table duty frequency ───
var newest = ordered.Last(s => s.PresentMemberIds.Count >= 3);
var newestIdx = ordered.IndexOf(newest);
var newestHistory = ordered.Take(newestIdx)
    .Where(s => s.Date.Date == newest.Date.Date
             || (!newest.ExcludeFromOptimization && !s.ExcludeFromOptimization))
    .ToList();
var detailSim = new Sim(newest.PresentMemberIds, newestHistory, extraCounts, newest.Date.Date);

foreach (var name in new[] { variants[3].Name, variants[^1].Name })
{
    var o = variants.First(v => v.Name == name).O;
    var freq = new Dictionary<Guid, int>();
    for (int r = 0; r < Runs; r++)
        foreach (var id in detailSim.Score(detailSim.Run(o)).ThreeAssigned)
            freq[id] = freq.GetValueOrDefault(id, 0) + 1;

    Console.WriteLine($"\n--- Newest session ({newest.Date:yyyy-MM-dd}) 3-table duty frequency over {Runs} runs — {name.Split(' ')[0]} ---");
    foreach (var kv in freq.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"    {nameMap.GetValueOrDefault(kv.Key, "?"),-18} {kv.Value,4}x   {detailSim.Describe(kv.Key)}");
}

// ═══════════════════════════════════════════════════════════════════════════

enum Selection { SimpleTake, AttendanceTiebreak, Tiered }

/// <param name="PostRatio">rank 3-table candidates on (three+1)/(att+1) instead of the pre-assignment ratio</param>
/// <param name="TrueCoAtt">meeting ratio denominator = sessions both present, instead of min(attendance)</param>
/// <param name="Swap">hill-climb pairwise swaps between same-size tables after greedy seating</param>
/// <param name="SameDayPenalty">score penalty per pair that already met earlier the same day</param>
/// <param name="ConvexCost">pair cost = met * ratio instead of ratio — a 3rd meeting costs 9/denom, not 3/denom, so repeats can't concentrate on the cheap high-attendance pairs</param>
record Options(bool PostRatio, Selection Sel, int Attempts, bool TrueCoAtt, bool Swap, double SameDayPenalty, bool ConvexCost = false);

record SimResult(double ThreeVariance, bool ThreePlayerOptimal, int MaxPairMeetings, double MeetingScore,
    int NewcomersAtThree, int HardViolations, int SameDayRepeats, int Unseated, List<Guid> ThreeAssigned);

class Agg
{
    public int Runs, Optimal, NewcomerRuns, HardViolations, SameDayRepeats, ZeroSameDayRuns;
    public double SumVariance, SumMeeting, SumMaxPair;
    public long Ms;

    public void AddOne(SimResult r)
    {
        Runs++;
        if (r.ThreePlayerOptimal) Optimal++;
        if (r.NewcomersAtThree > 0) NewcomerRuns++;
        if (r.SameDayRepeats == 0) ZeroSameDayRuns++;
        HardViolations += r.HardViolations;
        SameDayRepeats += r.SameDayRepeats;
        SumVariance += r.ThreeVariance;
        SumMeeting += r.MeetingScore;
        SumMaxPair += r.MaxPairMeetings;
    }

    public void Add(List<SimResult> results, long ms)
    {
        foreach (var r in results) AddOne(r);
        Ms += ms;
    }
}

/// <summary>One target session's replay context: all lookups the algorithm needs, plus the parameterized algorithm itself.</summary>
class Sim
{
    readonly List<Guid> present;
    readonly Dictionary<Guid, int> att;
    readonly Dictionary<Guid, int> threeCounts;
    readonly Dictionary<string, int> meetings;
    readonly Dictionary<string, int> coAtt;
    readonly Dictionary<string, int> sameDayMeetings;
    readonly HashSet<Guid> playedThreeLast;
    readonly List<Guid> eligible;
    readonly List<Guid> excluded;
    readonly int slots;

    public readonly int FourTables;
    public readonly int ThreeTables;
    public int EligibleCount => eligible.Count;

    public Sim(List<Guid> present, List<Session> history, Dictionary<Guid, int> extraCounts, DateTime day)
    {
        this.present = present;
        att = CountAttendance(history, present);
        threeCounts = CountThreePlayerAssignments(history, present, extraCounts);
        meetings = BuildMeetingMatrix(history, present);
        coAtt = BuildCoAttendance(history, present);
        sameDayMeetings = BuildMeetingMatrix(history.Where(s => s.Date.Date == day).ToList(), present);
        playedThreeLast = FindPlayersAtThreeTableLastSession(history, present);
        (FourTables, ThreeTables) = CalculateTableLayout(present.Count);
        slots = ThreeTables * 3;
        eligible = present.Where(id => !playedThreeLast.Contains(id)).ToList();
        excluded = present.Where(id => playedThreeLast.Contains(id)).ToList();
    }

    public string Describe(Guid id)
    {
        var a = att.GetValueOrDefault(id, 0);
        var t = threeCounts.GetValueOrDefault(id, 0);
        var marker = playedThreeLast.Contains(id) ? "  [played 3 last time]" : "";
        return $"att={a} three={t} post-ratio={(double)(t + 1) / (a + 1):F3}{marker}";
    }

    // ─── The parameterized algorithm (mirrors TableAssignmentService.AssignTables) ───

    public List<List<Guid>> Run(Options o)
    {
        var idealThree = ComputeIdealThreeFairnessScore(o);

        List<List<Guid>>? best = null;
        var bestCombined = double.MaxValue;

        for (int attempt = 0; attempt < o.Attempts; attempt++)
        {
            var sortedEligible = SortByRatio(eligible, o);

            List<Guid> threePool, fourPool;
            if (slots == 0)
            {
                threePool = new List<Guid>();
                fourPool = sortedEligible.Concat(excluded).ToList();
            }
            else if (sortedEligible.Count >= slots)
            {
                threePool = o.Sel switch
                {
                    Selection.SimpleTake => sortedEligible.Take(slots).ToList(),
                    Selection.AttendanceTiebreak => SelectAttendanceTiebreak(sortedEligible, o),
                    _ => SelectTiered(sortedEligible, o),
                };
                var threeSet = new HashSet<Guid>(threePool);
                fourPool = sortedEligible.Where(id => !threeSet.Contains(id)).Concat(excluded).ToList();
            }
            else
            {
                var sortedExcluded = SortByRatio(excluded, o);
                var rem = slots - sortedEligible.Count;
                threePool = sortedEligible.Concat(sortedExcluded.Take(rem)).ToList();
                fourPool = sortedExcluded.Skip(rem).ToList();
            }

            var threeTables = FormTablesGreedy(threePool, 3, o);
            var fourTables = FormTablesGreedy(fourPool, 4, o);
            if (o.Swap)
            {
                ImproveBySwaps(threeTables, o);
                ImproveBySwaps(fourTables, o);
            }
            var tables = threeTables.Concat(fourTables).ToList();

            var threeScore = ScoreThreePlayerFairness(threePool);
            var meetScore = ScoreAssignment(tables, o);
            var combined = 10.0 * threeScore + 1.0 * meetScore;

            if (combined < bestCombined)
            {
                bestCombined = combined;
                best = tables;
            }

            if (threeScore <= idealThree + 1e-9)
                break;
        }

        return best!;
    }

    double Ratio(Guid id, bool post)
    {
        var a = att.GetValueOrDefault(id, 0);
        var t = threeCounts.GetValueOrDefault(id, 0);
        return post ? (double)(t + 1) / (a + 1)
                    : a == 0 ? 0.0 : (double)t / a;
    }

    List<Guid> SortByRatio(List<Guid> ids, Options o)
    {
        var q = ids.OrderBy(id => Ratio(id, o.PostRatio));
        // The original OLD algorithm had no attendance tiebreak
        if (o.Sel != Selection.SimpleTake)
            q = q.ThenByDescending(id => att.GetValueOrDefault(id, 0));
        return q.ThenBy(_ => Random.Shared.Next()).ToList();
    }

    (List<Guid> mustInclude, List<Guid> borderline, int remaining) SplitAtCutoff(List<Guid> sortedElig, int wanted, Options o)
    {
        var cutoff = Ratio(sortedElig[wanted - 1], o.PostRatio);
        const double eps = 1e-9;
        var mustInclude = new List<Guid>();
        var borderline = new List<Guid>();
        foreach (var id in sortedElig)
        {
            var r = Ratio(id, o.PostRatio);
            if (r < cutoff - eps) mustInclude.Add(id);
            else if (Math.Abs(r - cutoff) <= eps) borderline.Add(id);
        }
        return (mustInclude, borderline, wanted - mustInclude.Count);
    }

    List<Guid> SelectAttendanceTiebreak(List<Guid> sortedElig, Options o)
    {
        if (sortedElig.Count <= slots) return sortedElig.ToList();
        var (mustInclude, borderline, remaining) = SplitAtCutoff(sortedElig, slots, o);
        if (remaining <= 0) return mustInclude.Take(slots).ToList();
        if (borderline.Count <= remaining) return mustInclude.Concat(borderline).Take(slots).ToList();

        var sorted = borderline
            .OrderByDescending(id => att.GetValueOrDefault(id, 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();
        return mustInclude.Concat(sorted.Take(remaining)).ToList();
    }

    List<Guid> SelectTiered(List<Guid> sortedElig, Options o)
    {
        if (sortedElig.Count <= slots) return sortedElig.ToList();
        var (mustInclude, borderline, remaining) = SplitAtCutoff(sortedElig, slots, o);
        if (remaining <= 0) return mustInclude.Take(slots).ToList();
        if (borderline.Count <= remaining) return mustInclude.Concat(borderline).Take(slots).ToList();

        var tiers = borderline
            .GroupBy(id => att.GetValueOrDefault(id, 0))
            .OrderByDescending(g => g.Key)
            .ToList();

        var selected = new List<Guid>(mustInclude);
        var slotsLeft = remaining;

        foreach (var tier in tiers)
        {
            if (slotsLeft <= 0) break;
            var tierPlayers = tier.ToList();

            if (tierPlayers.Count <= slotsLeft)
            {
                selected.AddRange(tierPlayers);
                slotsLeft -= tierPlayers.Count;
            }
            else
            {
                var pool = new List<Guid>(tierPlayers);
                for (int i = 0; i < slotsLeft; i++)
                {
                    var best = pool[0];
                    var bestScore = MeetingScoreAgainstGroup(selected, pool[0], o);
                    for (int c = 1; c < pool.Count; c++)
                    {
                        var s = MeetingScoreAgainstGroup(selected, pool[c], o);
                        if (s < bestScore || (Math.Abs(s - bestScore) < 1e-9 && Random.Shared.Next(2) == 0))
                        { bestScore = s; best = pool[c]; }
                    }
                    selected.Add(best);
                    pool.Remove(best);
                }
                slotsLeft = 0;
            }
        }

        return selected;
    }

    double ComputeIdealThreeFairnessScore(Options o)
    {
        if (slots == 0) return 0;
        var sorted = eligible
            .OrderBy(id => Ratio(id, o.PostRatio))
            .ThenByDescending(id => att.GetValueOrDefault(id, 0))
            .ToList();

        List<Guid> idealPool;
        if (sorted.Count >= slots)
            idealPool = sorted.Take(slots).ToList();
        else
        {
            var sortedExcl = excluded
                .OrderBy(id => Ratio(id, o.PostRatio))
                .ThenByDescending(id => att.GetValueOrDefault(id, 0))
                .ToList();
            idealPool = sorted.Concat(sortedExcl.Take(slots - sorted.Count)).ToList();
        }
        return ScoreThreePlayerFairness(idealPool);
    }

    double ScoreThreePlayerFairness(List<Guid> threePool)
    {
        if (threePool.Count == 0) return 0;
        var ratios = threePool.Select(id => Ratio(id, post: true)).ToList();
        var mean = ratios.Average();
        return ratios.Sum(r => (r - mean) * (r - mean));
    }

    // ─── Seating (greedy + optional swap refinement) ───

    List<List<Guid>> FormTablesGreedy(List<Guid> players, int tableSize, Options o)
    {
        if (players.Count == 0) return new();
        var numTables = players.Count / tableSize;
        if (numTables == 0) return new();

        List<List<Guid>>? best = null;
        var bestScore = double.MaxValue;
        var attempts = Math.Min(20, Math.Max(5, players.Count));

        for (int a = 0; a < attempts; a++)
        {
            var rem = new List<Guid>(players);
            Shuffle(rem);
            var tables = new List<List<Guid>>();
            for (int t = 0; t < numTables; t++) tables.Add(new());
            for (int t = 0; t < numTables; t++) { tables[t].Add(rem[0]); rem.RemoveAt(0); }

            for (int seat = 1; seat < tableSize; seat++)
            {
                for (int t = 0; t < numTables; t++)
                {
                    if (rem.Count == 0) break;
                    var bp = rem[0];
                    var bs = PairScore(tables[t], rem[0], o);
                    for (int p = 1; p < rem.Count; p++)
                    {
                        var s = PairScore(tables[t], rem[p], o);
                        if (s < bs) { bs = s; bp = rem[p]; }
                    }
                    tables[t].Add(bp);
                    rem.Remove(bp);
                }
            }

            var score = ScoreAssignment(tables, o);
            if (score < bestScore) { bestScore = score; best = tables; }
        }
        return best!;
    }

    /// <summary>Hill-climb: swap players between same-size tables while the total score improves.
    /// Pools stay intact, so 3-table duty fairness is untouched — only WHO sits WITH whom changes.</summary>
    void ImproveBySwaps(List<List<Guid>> tables, Options o)
    {
        if (tables.Count < 2) return;
        var improved = true;
        var guard = 0;
        while (improved && guard++ < 100)
        {
            improved = false;
            for (int t1 = 0; t1 < tables.Count; t1++)
                for (int t2 = t1 + 1; t2 < tables.Count; t2++)
                    for (int i = 0; i < tables[t1].Count; i++)
                        for (int j = 0; j < tables[t2].Count; j++)
                        {
                            var before = TableScore(tables[t1], o) + TableScore(tables[t2], o);
                            (tables[t1][i], tables[t2][j]) = (tables[t2][j], tables[t1][i]);
                            var after = TableScore(tables[t1], o) + TableScore(tables[t2], o);
                            if (after < before - 1e-12)
                                improved = true;
                            else
                                (tables[t1][i], tables[t2][j]) = (tables[t2][j], tables[t1][i]);
                        }
        }
    }

    int Denominator(Guid a, Guid b, Options o) => o.TrueCoAtt
        ? coAtt.GetValueOrDefault(MakeKey(a, b), 0)
        : Math.Min(att.GetValueOrDefault(a, 0), att.GetValueOrDefault(b, 0));

    double PairCost(Guid a, Guid b, Options o)
    {
        var key = MakeKey(a, b);
        var met = meetings.GetValueOrDefault(key, 0);
        var denom = Denominator(a, b, o);
        var cost = denom > 0 ? (double)met / denom : 0;
        if (o.ConvexCost)
            cost *= met;
        if (o.SameDayPenalty > 0 && sameDayMeetings.GetValueOrDefault(key, 0) > 0)
            cost += o.SameDayPenalty;
        return cost;
    }

    double PairScore(List<Guid> table, Guid candidate, Options o)
    {
        double s = 0;
        foreach (var m in table) s += PairCost(m, candidate, o);
        return s;
    }

    double MeetingScoreAgainstGroup(List<Guid> group, Guid candidate, Options o) => PairScore(group, candidate, o);

    double TableScore(List<Guid> table, Options o)
    {
        double s = 0;
        for (int i = 0; i < table.Count; i++)
            for (int j = i + 1; j < table.Count; j++)
                s += PairCost(table[i], table[j], o);
        return s;
    }

    double ScoreAssignment(List<List<Guid>> tables, Options o)
    {
        double total = 0;
        foreach (var t in tables) total += TableScore(t, o);
        return total;
    }

    // ─── Metrics (always the same baseline scoring, regardless of variant options) ───

    static readonly Options Baseline = new(PostRatio: true, Sel: Selection.Tiered, Attempts: 1, TrueCoAtt: false, Swap: false, SameDayPenalty: 0);

    public SimResult Score(List<List<Guid>> tables)
    {
        var threeAssigned = tables.Where(t => t.Count == 3).SelectMany(t => t).ToList();
        var seated = tables.SelectMany(t => t).ToHashSet();
        var fourAssigned = present.Where(id => seated.Contains(id) && !threeAssigned.Contains(id)).ToList();

        double PostRatioOf(Guid id, bool atThree)
        {
            var a = att.GetValueOrDefault(id, 0) + 1;
            var t = threeCounts.GetValueOrDefault(id, 0) + (atThree ? 1 : 0);
            return (double)t / a;
        }

        double threeVariance = 0;
        double maxThreeRatio = 0;
        if (threeAssigned.Count > 0)
        {
            var ratios = threeAssigned.Select(id => PostRatioOf(id, true)).ToList();
            var mean = ratios.Average();
            threeVariance = ratios.Sum(r => (r - mean) * (r - mean));
            maxThreeRatio = ratios.Max();
        }
        // Optimal selection = no ELIGIBLE player at a 4-table had a strictly lower would-be
        // 3-duty ratio than someone who got 3-duty (hard-constraint-excluded players are not candidates).
        var eligibleAtFour = fourAssigned.Where(id => !playedThreeLast.Contains(id)).ToList();
        var minFourRatio = eligibleAtFour.Count > 0
            ? eligibleAtFour.Min(id => PostRatioOf(id, true))
            : double.MaxValue;
        var optimal = threeAssigned.Count == 0 || maxThreeRatio <= minFourRatio + 1e-9;

        var maxPairMeetings = 0;
        var sameDayRepeats = 0;
        foreach (var t in tables)
            for (int i = 0; i < t.Count; i++)
                for (int j = i + 1; j < t.Count; j++)
                {
                    var key = MakeKey(t[i], t[j]);
                    var met = meetings.GetValueOrDefault(key, 0) + 1; // +1 for this assignment
                    if (met > maxPairMeetings) maxPairMeetings = met;
                    if (sameDayMeetings.GetValueOrDefault(key, 0) > 0) sameDayRepeats++;
                }

        var meetingScore = ScoreAssignment(tables, Baseline);

        // Newcomer wrongly at a 3-table: attendance 0 while an experienced player sits at a 4-table.
        // (At a club's very first session everyone is a newcomer — no experienced alternative, count 0.)
        var newcomers = fourAssigned.Any(id => att.GetValueOrDefault(id, 0) > 0)
            ? threeAssigned.Count(id => att.GetValueOrDefault(id, 0) == 0)
            : 0;

        // Hard-constraint violations beyond what a too-small eligible pool forces.
        var forced = Math.Max(0, slots - eligible.Count);
        var violations = Math.Max(0, threeAssigned.Count(id => playedThreeLast.Contains(id)) - forced);

        var unseated = present.Count - seated.Count;

        return new SimResult(threeVariance, optimal, maxPairMeetings, meetingScore,
            newcomers, violations, sameDayRepeats, unseated, threeAssigned);
    }

    // ─── History digests ───

    static void Shuffle(List<Guid> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static string MakeKey(Guid a, Guid b) =>
        string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) < 0
            ? $"{a}_{b}" : $"{b}_{a}";

    static Dictionary<Guid, int> CountAttendance(List<Session> history, List<Guid> relevant)
    {
        var c = relevant.ToDictionary(id => id, _ => 0);
        foreach (var s in history)
            foreach (var pid in s.PresentMemberIds)
                if (c.ContainsKey(pid)) c[pid]++;
        return c;
    }

    static Dictionary<Guid, int> CountThreePlayerAssignments(List<Session> history, List<Guid> relevant, Dictionary<Guid, int> extra)
    {
        var c = relevant.ToDictionary(id => id, id => extra.GetValueOrDefault(id, 0));
        foreach (var s in history)
            foreach (var t in s.Tables)
                if (t.PlayerIds.Count == 3)
                    foreach (var pid in t.PlayerIds)
                        if (c.ContainsKey(pid)) c[pid]++;
        return c;
    }

    static Dictionary<string, int> BuildMeetingMatrix(List<Session> history, List<Guid> relevant)
    {
        var m = new Dictionary<string, int>();
        var set = new HashSet<Guid>(relevant);
        foreach (var s in history)
            foreach (var t in s.Tables)
                for (int i = 0; i < t.PlayerIds.Count; i++)
                {
                    if (!set.Contains(t.PlayerIds[i])) continue;
                    for (int j = i + 1; j < t.PlayerIds.Count; j++)
                    {
                        if (!set.Contains(t.PlayerIds[j])) continue;
                        var k = MakeKey(t.PlayerIds[i], t.PlayerIds[j]);
                        m[k] = m.GetValueOrDefault(k, 0) + 1;
                    }
                }
        return m;
    }

    /// <summary>Sessions where BOTH players were present — the true number of times a pair could have met.</summary>
    static Dictionary<string, int> BuildCoAttendance(List<Session> history, List<Guid> relevant)
    {
        var m = new Dictionary<string, int>();
        var set = new HashSet<Guid>(relevant);
        foreach (var s in history)
        {
            var here = s.PresentMemberIds.Where(set.Contains).ToList();
            for (int i = 0; i < here.Count; i++)
                for (int j = i + 1; j < here.Count; j++)
                {
                    var k = MakeKey(here[i], here[j]);
                    m[k] = m.GetValueOrDefault(k, 0) + 1;
                }
        }
        return m;
    }

    static HashSet<Guid> FindPlayersAtThreeTableLastSession(List<Session> history, List<Guid> present)
    {
        var result = new HashSet<Guid>();
        var desc = history.Where(s => s.IsFinalized)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.StartTime ?? TimeSpan.Zero)
            .ToList();
        foreach (var pid in present)
        {
            foreach (var s in desc)
            {
                if (!s.PresentMemberIds.Contains(pid)) continue;
                foreach (var t in s.Tables)
                    if (t.PlayerIds.Count == 3 && t.PlayerIds.Contains(pid))
                        result.Add(pid);
                break;
            }
        }
        return result;
    }

    static (int four, int three) CalculateTableLayout(int n)
    {
        var rem = n % 4;
        return rem switch
        {
            0 => (n / 4, 0),
            1 when n >= 9 => ((n - 9) / 4, 3),
            1 when n == 5 => (1, 0),
            2 when n >= 6 => ((n - 6) / 4, 2),
            3 => ((n - 3) / 4, 1),
            _ => (0, 0)
        };
    }
}

// ─── Export records (field-subsets of the app's models, deserialized case-insensitively) ───

record ExportData(List<Member> Members, List<Session> Sessions);
record Member(Guid Id, string Name, string Email, int ExtraThreePlayerTableCount);
record Session(Guid Id, DateTime Date, TimeSpan? StartTime, List<Guid> PresentMemberIds, List<Table> Tables, bool IsFinalized, bool ExcludeFromOptimization);
record Table(int TableNumber, List<Guid> PlayerIds);
