using System.Text.Json;

// ─── Load data ───
var json = File.ReadAllText(args.Length > 0 ? args[0] : "testdata.json");
var data = JsonSerializer.Deserialize<ExportData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

var members = data.Members;
var allSessions = data.Sessions;

// Use the April 10 session's attendees as the "next" session to generate
var lastSession = allSessions.OrderByDescending(s => s.Date).First();
var presentPlayerIds = lastSession.PresentMemberIds;

// Exclude the target session from history (simulating a regeneration)
var history = allSessions.Where(s => !s.ExcludeFromOptimization && s.Id != lastSession.Id).ToList();

Console.WriteLine($"Simulating assignment for {presentPlayerIds.Count} players (session {lastSession.Date:yyyy-MM-dd})");
Console.WriteLine($"History: {history.Count} sessions (excluding target session)");
Console.WriteLine();

// ─── Build shared data structures ───
var extraCounts = members.ToDictionary(m => m.Id, m => m.ExtraThreePlayerTableCount);
var attendance = CountAttendance(history, presentPlayerIds);
var threePlayerCounts = CountThreePlayerAssignments(history, presentPlayerIds, extraCounts);
var meetingCounts = BuildMeetingMatrix(history, presentPlayerIds);
var playedThreeLastTime = FindPlayersAtThreeTableLastSession(history, presentPlayerIds);
double idealMeetingRatio = ComputeIdealMeetingRatio(presentPlayerIds, meetingCounts, attendance);

var (fourTables, threeTables) = CalculateTableLayout(presentPlayerIds.Count);
var threePlayerSlots = threeTables * 3;
Console.WriteLine($"Table layout: {fourTables} x 4-player, {threeTables} x 3-player ({threePlayerSlots} slots)");

var eligible = presentPlayerIds.Where(id => !playedThreeLastTime.Contains(id)).ToList();
var excluded = presentPlayerIds.Where(id => playedThreeLastTime.Contains(id)).ToList();
Console.WriteLine($"Eligible for 3-player: {eligible.Count}, Excluded (played 3 last time): {excluded.Count}");

// Print current 3-player ratios
Console.WriteLine("\n--- Current 3-player ratios (pre-assignment) ---");
var nameMap = members.ToDictionary(m => m.Id, m => m.Name);
foreach (var id in presentPlayerIds.OrderBy(id => {
    var att = attendance.GetValueOrDefault(id, 0);
    var tc = threePlayerCounts.GetValueOrDefault(id, 0);
    return att == 0 ? 0.0 : (double)tc / att;
}))
{
    var att = attendance.GetValueOrDefault(id, 0);
    var tc = threePlayerCounts.GetValueOrDefault(id, 0);
    var ratio = att == 0 ? 0.0 : (double)tc / att;
    var marker = playedThreeLastTime.Contains(id) ? " [EXCLUDED]" : "";
    Console.WriteLine($"  {nameMap.GetValueOrDefault(id, "?"),-15} att={att} three={tc} ratio={ratio:F3}{marker}");
}

// ─── Run simulations ───
const int RUNS = 100;

Console.WriteLine($"\n{'=',-60}");
Console.WriteLine($"Running {RUNS} simulations for EACH algorithm...");
Console.WriteLine($"{'=',-60}");

var oldResults = RunSimulation("OLD (simple take)", RUNS, "old");
var newResults = RunSimulation("NEW (multi-attempt)", RUNS, "new");
var fixedResults = RunSimulation("FIXED (attendance tiebreak)", RUNS, "fixed");
var tieredResults = RunSimulation("TIERED (attendance tiers + meetings)", RUNS, "tiered");

PrintResults("OLD (simple take)", oldResults);
PrintResults("NEW (multi-attempt)", newResults);
PrintResults("FIXED (attendance tiebreak)", fixedResults);
PrintResults("TIERED (attendance tiers + meetings)", tieredResults);

// ─── Algorithm implementations ───

List<SimResult> RunSimulation(string label, int runs, string algo)
{
    var results = new List<SimResult>();
    for (int i = 0; i < runs; i++)
    {
        List<TableResult> tables = algo switch
        {
            "old" => RunOldAlgorithm(),
            "new" => RunNewAlgorithm(),
            "fixed" => RunFixedAlgorithm(),
            "tiered" => RunTieredAlgorithm(),
            _ => throw new ArgumentException($"Unknown algo: {algo}")
        };
        results.Add(ScoreResult(tables));
    }
    return results;
}

List<TableResult> RunOldAlgorithm()
{
    // Original: sort by ratio, random tie-break, take first N
    var sortedEligible = eligible
        .OrderBy(id => {
            var att = attendance.GetValueOrDefault(id, 0);
            var tc = threePlayerCounts.GetValueOrDefault(id, 0);
            return att == 0 ? 0.0 : (double)tc / att;
        })
        .ThenBy(_ => Random.Shared.Next())
        .ToList();

    List<Guid> threePool, fourPool;
    if (sortedEligible.Count >= threePlayerSlots)
    {
        threePool = sortedEligible.Take(threePlayerSlots).ToList();
        fourPool = sortedEligible.Skip(threePlayerSlots).Concat(excluded).ToList();
    }
    else
    {
        var sortedExcluded = excluded
            .OrderBy(id => {
                var att = attendance.GetValueOrDefault(id, 0);
                var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                return att == 0 ? 0.0 : (double)tc / att;
            })
            .ThenBy(_ => Random.Shared.Next())
            .ToList();
        var rem = threePlayerSlots - sortedEligible.Count;
        threePool = sortedEligible.Concat(sortedExcluded.Take(rem)).ToList();
        fourPool = sortedExcluded.Skip(rem).ToList();
    }

    var tables = new List<TableResult>();
    int num = 1;
    foreach (var g in FormTablesGreedy(threePool, 3))
        tables.Add(new TableResult(num++, g));
    foreach (var g in FormTablesGreedy(fourPool, 4))
        tables.Add(new TableResult(num++, g));
    return tables;
}

List<TableResult> RunNewAlgorithm()
{
    // New: multi-attempt with quality scoring
    var idealThreeScore = ComputeIdealThreeFairnessScore();

    List<TableResult>? bestResult = null;
    double bestCombined = double.MaxValue;

    for (int attempt = 0; attempt < 5; attempt++)
    {
        var sortedEligible = eligible
            .OrderBy(id => {
                var att = attendance.GetValueOrDefault(id, 0);
                var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                return att == 0 ? 0.0 : (double)tc / att;
            })
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        List<Guid> threePool, fourPool;
        if (sortedEligible.Count >= threePlayerSlots)
        {
            threePool = SelectThreePlayerCandidates(sortedEligible, threePlayerSlots);
            var threeSet = new HashSet<Guid>(threePool);
            fourPool = sortedEligible.Where(id => !threeSet.Contains(id)).Concat(excluded).ToList();
        }
        else
        {
            var sortedExcluded = excluded
                .OrderBy(id => {
                    var att = attendance.GetValueOrDefault(id, 0);
                    var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                    return att == 0 ? 0.0 : (double)tc / att;
                })
                .ThenBy(_ => Random.Shared.Next())
                .ToList();
            var rem = threePlayerSlots - sortedEligible.Count;
            threePool = sortedEligible.Concat(sortedExcluded.Take(rem)).ToList();
            fourPool = sortedExcluded.Skip(rem).ToList();
        }

        var tables = new List<TableResult>();
        int num = 1;
        foreach (var g in FormTablesGreedy(threePool, 3))
            tables.Add(new TableResult(num++, g));
        foreach (var g in FormTablesGreedy(fourPool, 4))
            tables.Add(new TableResult(num++, g));

        double threeScore = ScoreThreePlayerFairness(threePool);
        double meetScore = ScoreAssignment(tables.Select(t => t.Players).ToList());
        double combined = 10.0 * threeScore + 1.0 * meetScore;

        if (combined < bestCombined)
        {
            bestCombined = combined;
            bestResult = tables;
        }

        if (threeScore <= idealThreeScore + 1e-9)
            break;
    }

    return bestResult!;
}

List<TableResult> RunFixedAlgorithm()
{
    // Fixed: multi-attempt, but 3-player selection uses attendance as tiebreaker
    // instead of meeting deviation (which biased toward low-attendance players)
    var idealThreeScore = ComputeIdealThreeFairnessScore();

    List<TableResult>? bestResult = null;
    double bestCombined = double.MaxValue;

    for (int attempt = 0; attempt < 5; attempt++)
    {
        // Sort by pre-ratio ascending, then attendance descending (higher att = smaller impact)
        var sortedEligible = eligible
            .OrderBy(id => {
                var att = attendance.GetValueOrDefault(id, 0);
                var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                return att == 0 ? 0.0 : (double)tc / att;
            })
            .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        List<Guid> threePool, fourPool;
        if (sortedEligible.Count >= threePlayerSlots)
        {
            threePool = SelectThreePlayerCandidatesFixed(sortedEligible, threePlayerSlots);
            var threeSet = new HashSet<Guid>(threePool);
            fourPool = sortedEligible.Where(id => !threeSet.Contains(id)).Concat(excluded).ToList();
        }
        else
        {
            var sortedExcluded = excluded
                .OrderBy(id => {
                    var att = attendance.GetValueOrDefault(id, 0);
                    var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                    return att == 0 ? 0.0 : (double)tc / att;
                })
                .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
                .ThenBy(_ => Random.Shared.Next())
                .ToList();
            var rem = threePlayerSlots - sortedEligible.Count;
            threePool = sortedEligible.Concat(sortedExcluded.Take(rem)).ToList();
            fourPool = sortedExcluded.Skip(rem).ToList();
        }

        var tables = new List<TableResult>();
        int num = 1;
        foreach (var g in FormTablesGreedy(threePool, 3))
            tables.Add(new TableResult(num++, g));
        foreach (var g in FormTablesGreedy(fourPool, 4))
            tables.Add(new TableResult(num++, g));

        double threeScore = ScoreThreePlayerFairness(threePool);
        double meetScore = ScoreAssignment(tables.Select(t => t.Players).ToList());
        double combined = 10.0 * threeScore + 1.0 * meetScore;

        if (combined < bestCombined)
        {
            bestCombined = combined;
            bestResult = tables;
        }

        if (threeScore <= idealThreeScore + 1e-9)
            break;
    }

    return bestResult!;
}

List<Guid> SelectThreePlayerCandidatesFixed(List<Guid> sortedElig, int slots)
{
    if (sortedElig.Count <= slots) return sortedElig.ToList();

    double ThreeRatio(Guid id)
    {
        var att = attendance.GetValueOrDefault(id, 0);
        var tc = threePlayerCounts.GetValueOrDefault(id, 0);
        return att == 0 ? 0.0 : (double)tc / att;
    }

    var cutoffRatio = ThreeRatio(sortedElig[slots - 1]);
    const double eps = 1e-9;
    var mustInclude = new List<Guid>();
    var borderline = new List<Guid>();

    foreach (var id in sortedElig)
    {
        var r = ThreeRatio(id);
        if (r < cutoffRatio - eps) mustInclude.Add(id);
        else if (Math.Abs(r - cutoffRatio) <= eps) borderline.Add(id);
    }

    var remaining = slots - mustInclude.Count;
    if (remaining <= 0) return mustInclude.Take(slots).ToList();
    if (borderline.Count <= remaining) return mustInclude.Concat(borderline).Take(slots).ToList();

    // Among borderline: prefer higher attendance (lower post-assignment ratio impact)
    // Random tiebreak within same attendance lets multi-attempt loop explore options
    var sorted = borderline
        .OrderByDescending(id => attendance.GetValueOrDefault(id, 0))
        .ThenBy(_ => Random.Shared.Next())
        .ToList();

    return mustInclude.Concat(sorted.Take(remaining)).ToList();
}

List<TableResult> RunTieredAlgorithm()
{
    // Tiered: attendance tiers for fairness, meeting scoring within partial tiers
    var idealThreeScore = ComputeIdealThreeFairnessScore();

    List<TableResult>? bestResult = null;
    double bestCombined = double.MaxValue;

    for (int attempt = 0; attempt < 5; attempt++)
    {
        var sortedEligible = eligible
            .OrderBy(id => {
                var att = attendance.GetValueOrDefault(id, 0);
                var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                return att == 0 ? 0.0 : (double)tc / att;
            })
            .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        List<Guid> threePool, fourPool;
        if (sortedEligible.Count >= threePlayerSlots)
        {
            threePool = SelectThreePlayerCandidatesTiered(sortedEligible, threePlayerSlots);
            var threeSet = new HashSet<Guid>(threePool);
            fourPool = sortedEligible.Where(id => !threeSet.Contains(id)).Concat(excluded).ToList();
        }
        else
        {
            var sortedExcluded = excluded
                .OrderBy(id => {
                    var att = attendance.GetValueOrDefault(id, 0);
                    var tc = threePlayerCounts.GetValueOrDefault(id, 0);
                    return att == 0 ? 0.0 : (double)tc / att;
                })
                .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
                .ThenBy(_ => Random.Shared.Next())
                .ToList();
            var rem = threePlayerSlots - sortedEligible.Count;
            threePool = sortedEligible.Concat(sortedExcluded.Take(rem)).ToList();
            fourPool = sortedExcluded.Skip(rem).ToList();
        }

        var tables = new List<TableResult>();
        int num = 1;
        foreach (var g in FormTablesGreedy(threePool, 3))
            tables.Add(new TableResult(num++, g));
        foreach (var g in FormTablesGreedy(fourPool, 4))
            tables.Add(new TableResult(num++, g));

        double threeScore = ScoreThreePlayerFairness(threePool);
        double meetScore = ScoreAssignment(tables.Select(t => t.Players).ToList());
        double combined = 10.0 * threeScore + 1.0 * meetScore;

        if (combined < bestCombined)
        {
            bestCombined = combined;
            bestResult = tables;
        }

        if (threeScore <= idealThreeScore + 1e-9)
            break;
    }

    return bestResult!;
}

List<Guid> SelectThreePlayerCandidatesTiered(List<Guid> sortedElig, int slots)
{
    if (sortedElig.Count <= slots) return sortedElig.ToList();

    double ThreeRatio(Guid id)
    {
        var att = attendance.GetValueOrDefault(id, 0);
        var tc = threePlayerCounts.GetValueOrDefault(id, 0);
        return att == 0 ? 0.0 : (double)tc / att;
    }

    var cutoffRatio = ThreeRatio(sortedElig[slots - 1]);
    const double eps = 1e-9;
    var mustInclude = new List<Guid>();
    var borderline = new List<Guid>();

    foreach (var id in sortedElig)
    {
        var r = ThreeRatio(id);
        if (r < cutoffRatio - eps) mustInclude.Add(id);
        else if (Math.Abs(r - cutoffRatio) <= eps) borderline.Add(id);
    }

    var remainingSlots = slots - mustInclude.Count;
    if (remainingSlots <= 0) return mustInclude.Take(slots).ToList();
    if (borderline.Count <= remainingSlots) return mustInclude.Concat(borderline).Take(slots).ToList();

    // Group borderline by attendance tiers, process highest first
    var tiers = borderline
        .GroupBy(id => attendance.GetValueOrDefault(id, 0))
        .OrderByDescending(g => g.Key)
        .ToList();

    var selected = new List<Guid>(mustInclude);
    var slotsLeft = remainingSlots;

    foreach (var tier in tiers)
    {
        if (slotsLeft <= 0) break;
        var tierPlayers = tier.ToList();

        if (tierPlayers.Count <= slotsLeft)
        {
            // Entire tier fits — include all
            selected.AddRange(tierPlayers);
            slotsLeft -= tierPlayers.Count;
        }
        else
        {
            // Partial tier — greedy selection by meeting score against already-selected
            var pool = new List<Guid>(tierPlayers);
            for (int i = 0; i < slotsLeft; i++)
            {
                var best = pool[0];
                var bestScore = MeetingScoreAgainstSelected(selected, pool[0]);
                for (int c = 1; c < pool.Count; c++)
                {
                    var s = MeetingScoreAgainstSelected(selected, pool[c]);
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

double MeetingScoreAgainstSelected(List<Guid> group, Guid candidate)
{
    double s = 0;
    var ca = attendance.GetValueOrDefault(candidate, 0);
    foreach (var m in group)
    {
        var k = MakeKey(m, candidate);
        var met = meetingCounts.GetValueOrDefault(k, 0);
        var ma = attendance.GetValueOrDefault(m, 0);
        var co = Math.Min(ca, ma);
        if (co > 0) s += (double)met / co;
    }
    return s;
}

List<Guid> SelectThreePlayerCandidates(List<Guid> sortedElig, int slots)
{
    if (sortedElig.Count <= slots) return sortedElig.ToList();

    double ThreeRatio(Guid id)
    {
        var att = attendance.GetValueOrDefault(id, 0);
        var tc = threePlayerCounts.GetValueOrDefault(id, 0);
        return att == 0 ? 0.0 : (double)tc / att;
    }

    var cutoffRatio = ThreeRatio(sortedElig[slots - 1]);
    const double eps = 1e-9;
    var mustInclude = new List<Guid>();
    var borderline = new List<Guid>();

    foreach (var id in sortedElig)
    {
        var r = ThreeRatio(id);
        if (r < cutoffRatio - eps) mustInclude.Add(id);
        else if (Math.Abs(r - cutoffRatio) <= eps) borderline.Add(id);
    }

    var remaining = slots - mustInclude.Count;
    if (remaining <= 0) return mustInclude.Take(slots).ToList();
    if (borderline.Count <= remaining) return mustInclude.Concat(borderline).Take(slots).ToList();

    var selected = new List<Guid>(mustInclude);
    var pool = new List<Guid>(borderline);
    for (int i = 0; i < remaining; i++)
    {
        var best = pool[0];
        var bestScore = MeetingDeviationScore(selected, pool[0]);
        for (int c = 1; c < pool.Count; c++)
        {
            var s = MeetingDeviationScore(selected, pool[c]);
            if (s < bestScore || (s == bestScore && Random.Shared.Next(2) == 0))
            { bestScore = s; best = pool[c]; }
        }
        selected.Add(best);
        pool.Remove(best);
    }
    return selected;
}

double MeetingDeviationScore(List<Guid> group, Guid candidate)
{
    const double leeway = 0.2;
    double score = 0;
    var candAtt = attendance.GetValueOrDefault(candidate, 0);
    foreach (var m in group)
    {
        var key = MakeKey(m, candidate);
        var met = meetingCounts.GetValueOrDefault(key, 0);
        var mAtt = attendance.GetValueOrDefault(m, 0);
        var co = Math.Min(candAtt, mAtt);
        if (co == 0) continue;
        var ratio = (double)met / co;
        var dev = ratio - idealMeetingRatio;
        if (Math.Abs(dev) <= leeway) continue;
        score += dev * dev;
    }
    return score;
}

double ScoreThreePlayerFairness(List<Guid> threePool)
{
    if (threePool.Count == 0) return 0;
    var ratios = threePool.Select(id => {
        var att = attendance.GetValueOrDefault(id, 0) + 1;
        var tc = threePlayerCounts.GetValueOrDefault(id, 0) + 1;
        return (double)tc / att;
    }).ToList();
    var mean = ratios.Average();
    return ratios.Sum(r => (r - mean) * (r - mean));
}

double ComputeIdealThreeFairnessScore()
{
    if (threePlayerSlots == 0) return 0;
    var sorted = eligible
        .OrderBy(id => {
            var att = attendance.GetValueOrDefault(id, 0);
            var tc = threePlayerCounts.GetValueOrDefault(id, 0);
            return att == 0 ? 0.0 : (double)tc / att;
        }).ToList();

    List<Guid> idealPool;
    if (sorted.Count >= threePlayerSlots)
        idealPool = sorted.Take(threePlayerSlots).ToList();
    else
    {
        var sortedExcl = excluded.OrderBy(id => {
            var att = attendance.GetValueOrDefault(id, 0);
            var tc = threePlayerCounts.GetValueOrDefault(id, 0);
            return att == 0 ? 0.0 : (double)tc / att;
        }).ToList();
        idealPool = sorted.Concat(sortedExcl.Take(threePlayerSlots - sorted.Count)).ToList();
    }
    return ScoreThreePlayerFairness(idealPool);
}

// ─── FormTablesGreedy (shared by both) ───

List<List<Guid>> FormTablesGreedy(List<Guid> players, int tableSize)
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
                var bs = PairScore(tables[t], rem[0]);
                for (int p = 1; p < rem.Count; p++)
                {
                    var s = PairScore(tables[t], rem[p]);
                    if (s < bs) { bs = s; bp = rem[p]; }
                }
                tables[t].Add(bp);
                rem.Remove(bp);
            }
        }

        var score = ScoreAssignment(tables);
        if (score < bestScore) { bestScore = score; best = tables; }
    }
    return best!;
}

double PairScore(List<Guid> table, Guid candidate)
{
    double s = 0;
    var ca = attendance.GetValueOrDefault(candidate, 0);
    foreach (var m in table)
    {
        var k = MakeKey(m, candidate);
        var met = meetingCounts.GetValueOrDefault(k, 0);
        var ma = attendance.GetValueOrDefault(m, 0);
        var co = Math.Min(ca, ma);
        if (co > 0) s += (double)met / co;
    }
    return s;
}

double ScoreAssignment(List<List<Guid>> tables)
{
    double total = 0;
    foreach (var t in tables)
        for (int i = 0; i < t.Count; i++)
            for (int j = i + 1; j < t.Count; j++)
            {
                var k = MakeKey(t[i], t[j]);
                var met = meetingCounts.GetValueOrDefault(k, 0);
                var co = Math.Min(attendance.GetValueOrDefault(t[i], 0), attendance.GetValueOrDefault(t[j], 0));
                if (co > 0) total += (double)met / co;
            }
    return total;
}

// ─── Scoring a result ───

SimResult ScoreResult(List<TableResult> tables)
{
    // 3-player fairness: check who got assigned to 3-player tables
    var threeAssigned = new List<Guid>();
    foreach (var t in tables.Where(t => t.Players.Count == 3))
        threeAssigned.AddRange(t.Players);

    // Post-assignment ratios for ALL present players
    var postRatios = new Dictionary<Guid, double>();
    foreach (var id in presentPlayerIds)
    {
        var att = attendance.GetValueOrDefault(id, 0) + 1;
        var tc = threePlayerCounts.GetValueOrDefault(id, 0) + (threeAssigned.Contains(id) ? 1 : 0);
        postRatios[id] = (double)tc / att;
    }

    // Variance of post-assignment ratios for 3-player assigned
    double threeVariance = 0;
    if (threeAssigned.Count > 0)
    {
        var threeRatios = threeAssigned.Select(id => postRatios[id]).ToList();
        var mean = threeRatios.Average();
        threeVariance = threeRatios.Sum(r => (r - mean) * (r - mean));
    }

    // Max post-ratio among 3-player assigned players
    double maxThreeRatio = threeAssigned.Count > 0
        ? threeAssigned.Max(id => postRatios[id])
        : 0;

    // Min post-ratio among 4-player assigned players
    var fourAssigned = presentPlayerIds.Where(id => !threeAssigned.Contains(id)).ToList();
    double minFourRatio = fourAssigned.Count > 0
        ? fourAssigned.Min(id => postRatios[id])
        : 0;

    // "Wrong assignment" = a player at 3-player table has higher post-ratio
    // than a player at 4-player table
    bool threePlayerOptimal = maxThreeRatio <= minFourRatio + 1e-9;

    // Meeting spread: max pair meetings at any table
    int maxPairMeetings = 0;
    double maxPairRatio = 0;
    foreach (var t in tables)
    {
        for (int i = 0; i < t.Players.Count; i++)
            for (int j = i + 1; j < t.Players.Count; j++)
            {
                var k = MakeKey(t.Players[i], t.Players[j]);
                var met = meetingCounts.GetValueOrDefault(k, 0) + 1; // +1 for this assignment
                if (met > maxPairMeetings) maxPairMeetings = met;
                var co = Math.Min(attendance.GetValueOrDefault(t.Players[i], 0), attendance.GetValueOrDefault(t.Players[j], 0));
                if (co > 0)
                {
                    var r = (double)met / (co + 1);
                    if (r > maxPairRatio) maxPairRatio = r;
                }
            }
    }

    double meetingScore = ScoreAssignment(tables.Select(t => t.Players).ToList());

    return new SimResult(threeVariance, threePlayerOptimal, maxPairMeetings, maxPairRatio, meetingScore, threeAssigned, tables);
}

void PrintResults(string label, List<SimResult> results)
{
    Console.WriteLine($"\n{'─',-60}");
    Console.WriteLine($" {label} — {results.Count} runs");
    Console.WriteLine($"{'─',-60}");

    var optimal = results.Count(r => r.ThreePlayerOptimal);
    Console.WriteLine($"  3-player optimal:     {optimal}/{results.Count} ({100.0 * optimal / results.Count:F1}%)");
    Console.WriteLine($"  3-player variance:    avg={results.Average(r => r.ThreeVariance):F6}  min={results.Min(r => r.ThreeVariance):F6}  max={results.Max(r => r.ThreeVariance):F6}");
    Console.WriteLine($"  Max pair meetings:    avg={results.Average(r => r.MaxPairMeetings):F2}  min={results.Min(r => r.MaxPairMeetings)}  max={results.Max(r => r.MaxPairMeetings)}");
    Console.WriteLine($"  Max pair ratio:       avg={results.Average(r => r.MaxPairRatio):F3}  min={results.Min(r => r.MaxPairRatio):F3}  max={results.Max(r => r.MaxPairRatio):F3}");
    Console.WriteLine($"  Meeting score (sum):  avg={results.Average(r => r.MeetingScore):F3}  min={results.Min(r => r.MeetingScore):F3}  max={results.Max(r => r.MeetingScore):F3}");

    // Show distribution of who gets assigned to 3-player tables
    var threeCounts = new Dictionary<Guid, int>();
    foreach (var r in results)
        foreach (var id in r.ThreeAssigned)
            threeCounts[id] = threeCounts.GetValueOrDefault(id, 0) + 1;

    Console.WriteLine($"\n  3-player table assignment frequency:");
    foreach (var kv in threeCounts.OrderByDescending(kv => kv.Value))
    {
        var name = nameMap.GetValueOrDefault(kv.Key, "?");
        var att = attendance.GetValueOrDefault(kv.Key, 0);
        var tc = threePlayerCounts.GetValueOrDefault(kv.Key, 0);
        var preRatio = att == 0 ? 0.0 : (double)tc / att;
        Console.WriteLine($"    {name,-15} {kv.Value,4}x  (pre: {tc}/{att}={preRatio:F3})");
    }
}

// ─── Helpers ───

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

static HashSet<Guid> FindPlayersAtThreeTableLastSession(List<Session> history, List<Guid> present)
{
    var result = new HashSet<Guid>();
    var desc = history.Where(s => s.IsFinalized).OrderByDescending(s => s.Date).ThenByDescending(s => s.StartTime).ToList();
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

static double ComputeIdealMeetingRatio(List<Guid> present, Dictionary<string, int> meetings, Dictionary<Guid, int> att)
{
    double total = 0; int pairs = 0;
    for (int i = 0; i < present.Count; i++)
    {
        var ai = att.GetValueOrDefault(present[i], 0);
        for (int j = i + 1; j < present.Count; j++)
        {
            var co = Math.Min(ai, att.GetValueOrDefault(present[j], 0));
            if (co == 0) continue;
            var k = MakeKey(present[i], present[j]);
            total += (double)meetings.GetValueOrDefault(k, 0) / co;
            pairs++;
        }
    }
    return pairs > 0 ? total / pairs : 0;
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

// ─── Records ───

record SimResult(double ThreeVariance, bool ThreePlayerOptimal, int MaxPairMeetings, double MaxPairRatio, double MeetingScore, List<Guid> ThreeAssigned, List<TableResult> Tables);
record TableResult(int Number, List<Guid> Players);

record ExportData(List<Member> Members, List<Session> Sessions);
record Member(Guid Id, string Name, string Email, int ExtraThreePlayerTableCount);
record Session(Guid Id, DateTime Date, TimeSpan StartTime, List<Guid> PresentMemberIds, List<Table> Tables, bool IsFinalized, bool ExcludeFromOptimization);
record Table(int TableNumber, List<Guid> PlayerIds);
