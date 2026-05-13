using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Assigns present players to tables of 4 (preferred) and 3.
/// Priority 1: Evenly spread who plays at 3-player tables (by ratio to attendance).
/// Priority 2: Spread meetings between players — using meeting ratio relative to co-attendance.
/// </summary>
public class TableAssignmentService
{
    private readonly SessionService _sessionService;
    private readonly MemberService _memberService;

    public TableAssignmentService(SessionService sessionService, MemberService memberService)
    {
        _sessionService = sessionService;
        _memberService = memberService;
    }

    private const int MaxAttempts = 5;
    private const double ThreeFairnessWeight = 10.0;
    private const double MeetingSpreadWeight = 1.0;

    public async Task<List<TableAssignment>> AssignTables(List<Guid> presentPlayerIds, Guid? currentSessionId = null, DateTime? currentDate = null, bool currentDateExcluded = false)
    {
        var playerCount = presentPlayerIds.Count;
        if (playerCount < 3)
            return new List<TableAssignment>();

        (int fourPlayerTables, int threePlayerTables) tableNumbers = CalculateNumberofFourAndThreePLayerTables(playerCount);
        var threePlayerTables = tableNumbers.threePlayerTables;

        List<Hanchan> allHistory = await _sessionService.GetAllAsync();
        // History filter:
        //  - Drop the current session (avoid self-contamination during regeneration).
        //  - Same-date prior hanchans always count (this is one event running across multiple hanchans).
        //  - Other-date hanchans count only if BOTH sides are non-excluded:
        //    * if the current date is excluded, no other-date history is used at all;
        //    * if a historical hanchan's date is excluded, it is not used for other dates.
        var currentDay = currentDate?.Date;
        List<Hanchan> history = allHistory
            .Where(s => s.Id != currentSessionId)
            .Where(s =>
                (currentDay.HasValue && s.Date.Date == currentDay.Value)
                || (!currentDateExcluded && !s.ExcludeFromOptimization))
            .ToList();
        List<Member> members = await _memberService.GetAllAsync();
        Dictionary<Guid, int> extraCounts = members.ToDictionary(m => m.Id, m => m.ExtraThreePlayerTableCount);
        Dictionary<Guid, int> attendance = CountAttendance(history, presentPlayerIds);
        Dictionary<Guid, int> threePlayerCounts = CountThreePlayerAssignments(history, presentPlayerIds, extraCounts);
        Dictionary<string, int> meetingCounts = BuildMeetingMatrix(history, presentPlayerIds);

        // Special case: 21 players, second hanchan of a date. Builds tables by rearranging
        // the prior hanchan so no pair repeats and no one plays a 3-table twice in the day.
        var specialCase = TryBuildSpecialCase21SecondHanchan(
            presentPlayerIds, currentSessionId, currentDay, allHistory,
            meetingCounts, attendance);
        if (specialCase != null)
            return specialCase;

        // Hard constraint: players who were at a 3-player table in their last attended
        // session cannot be assigned to a 3-player table again
        HashSet<Guid> playedThreeLastTime = FindPlayersAtThreeTableLastSession(history, presentPlayerIds);

        List<Guid> eligible = presentPlayerIds.Where(id => !playedThreeLastTime.Contains(id)).ToList();
        List<Guid> excluded = presentPlayerIds.Where(id => playedThreeLastTime.Contains(id)).ToList();
        var threePlayerSlots = threePlayerTables * 3;

        // Compute the ideal (best possible) 3-player fairness score:
        // sort all eligible by ratio, take the best N — this is the deterministic optimum
        var idealThreeScore = ComputeIdealThreeFairnessScore(
            eligible, excluded, threePlayerSlots, threePlayerCounts, attendance);

        List<TableAssignment>? bestResult = null;
        double bestCombinedScore = double.MaxValue;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var (tables, threePool) = BuildOneAttempt(
                presentPlayerIds, eligible, excluded, threePlayerSlots,
                threePlayerCounts, attendance, meetingCounts);

            // Score this attempt
            double threeScore = ScoreThreePlayerFairness(threePool, threePlayerCounts, attendance);
            double meetingScore = ScoreAssignmentRatio(
                tables.Select(t => t.PlayerIds).ToList(), meetingCounts, attendance);
            double combined = ThreeFairnessWeight * threeScore + MeetingSpreadWeight * meetingScore;

            if (combined < bestCombinedScore)
            {
                bestCombinedScore = combined;
                bestResult = tables;
            }

            // Early exit: 3-player fairness is at the ideal (within tiny epsilon)
            // and meeting score has no room to improve meaningfully
            if (threeScore <= idealThreeScore + 1e-9)
                break;
        }

        return bestResult!;
    }

    /// <summary>
    /// Builds one complete assignment attempt: selects 3-player candidates,
    /// then forms tables for both pools.
    /// </summary>
    private static (List<TableAssignment> tables, List<Guid> threePool) BuildOneAttempt(
        List<Guid> presentPlayerIds,
        List<Guid> eligible, List<Guid> excluded, int threePlayerSlots,
        Dictionary<Guid, int> threePlayerCounts,
        Dictionary<Guid, int> attendance,
        Dictionary<string, int> meetingCounts)
    {
        // Re-sort with fresh random tie-breaking each attempt.
        // Tiebreaker: prefer higher attendance (smaller post-assignment ratio impact).
        var sortedEligible = eligible
            .OrderBy(id =>
            {
                var attended = attendance.GetValueOrDefault(id, 0);
                var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                return attended == 0 ? 0.0 : (double)threeCount / attended;
            })
            .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        List<Guid> threePlayerPool;
        List<Guid> fourPlayerPool;

        if (sortedEligible.Count >= threePlayerSlots)
        {
            threePlayerPool = SelectThreePlayerCandidates(
                sortedEligible, threePlayerSlots, threePlayerCounts, attendance, meetingCounts);
            var threePlayerSet = new HashSet<Guid>(threePlayerPool);
            fourPlayerPool = sortedEligible.Where(id => !threePlayerSet.Contains(id)).Concat(excluded).ToList();
        }
        else
        {
            var sortedExcluded = excluded
                .OrderBy(id =>
                {
                    var attended = attendance.GetValueOrDefault(id, 0);
                    var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                    return attended == 0 ? 0.0 : (double)threeCount / attended;
                })
                .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
                .ThenBy(_ => Random.Shared.Next())
                .ToList();

            var remaining = threePlayerSlots - sortedEligible.Count;
            threePlayerPool = sortedEligible.Concat(sortedExcluded.Take(remaining)).ToList();
            fourPlayerPool = sortedExcluded.Skip(remaining).ToList();
        }

        var tables = new List<TableAssignment>();
        var tableNumber = 1;

        foreach (var group in FormTablesGreedy(threePlayerPool, 3, meetingCounts, attendance))
            tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });

        foreach (var group in FormTablesGreedy(fourPlayerPool, 4, meetingCounts, attendance))
            tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });

        return (tables, threePlayerPool);
    }

    private static readonly int[][] Perms3 =
    {
        new[] {0,1,2}, new[] {0,2,1}, new[] {1,0,2},
        new[] {1,2,0}, new[] {2,0,1}, new[] {2,1,0},
    };

    /// <summary>
    /// Special case for 21 players when generating the second hanchan of a date.
    /// The prior hanchan must have 3 three-player tables and 3 four-player tables, and the
    /// same 21 players must be present. Produces a rearrangement guaranteeing:
    ///   C1: No player plays a 3-player table twice within the day.
    ///   C2: No pair shares a table twice within the day.
    /// Among valid configurations, picks the one with the lowest meeting score against
    /// prior history. Returns null if conditions aren't met.
    /// </summary>
    private static List<TableAssignment>? TryBuildSpecialCase21SecondHanchan(
        List<Guid> presentPlayerIds,
        Guid? currentSessionId,
        DateTime? currentDay,
        List<Hanchan> allHistory,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        if (presentPlayerIds.Count != 21 || !currentDay.HasValue)
            return null;

        // Find the prior hanchan: exactly one other hanchan on this date with tables assigned.
        var sameDate = allHistory
            .Where(s => s.Id != currentSessionId
                        && s.Date.Date == currentDay.Value
                        && s.Tables.Count > 0)
            .ToList();
        if (sameDate.Count != 1) return null;

        var prior = sameDate[0];
        var priorThree = prior.Tables.Where(t => t.PlayerCount == 3).ToList();
        var priorFour = prior.Tables.Where(t => t.PlayerCount == 4).ToList();
        if (priorThree.Count != 3 || priorFour.Count != 3) return null;

        // The same 21 players must be present (no additions/removals since hanchan 1).
        var priorPlayers = prior.Tables.SelectMany(t => t.PlayerIds).ToHashSet();
        if (priorPlayers.Count != 21 || !priorPlayers.SetEquals(presentPlayerIds))
            return null;

        // Index all 21 players into 0..20 and pre-compute a symmetric pair-score matrix.
        // This is the key optimisation: the inner loops do ~1.6M score lookups, and a
        // dictionary+string-key path costs ~6M Guid.ToString allocations in WASM (30+ seconds).
        // Array indexing brings it under 100ms.
        var idx = new Dictionary<Guid, int>(21);
        for (int i = 0; i < presentPlayerIds.Count; i++)
            idx[presentPlayerIds[i]] = i;

        var score = new double[21, 21];
        for (int i = 0; i < 21; i++)
        {
            var idA = presentPlayerIds[i];
            for (int j = i + 1; j < 21; j++)
            {
                var idB = presentPlayerIds[j];
                var met = meetingCounts.GetValueOrDefault(MakeKey(idA, idB), 0);
                if (met == 0) continue;
                var coAtt = Math.Min(
                    attendance.GetValueOrDefault(idA, 0),
                    attendance.GetValueOrDefault(idB, 0));
                var v = coAtt > 0 ? (double)met / coAtt : 0;
                score[i, j] = v;
                score[j, i] = v;
            }
        }

        // A[i][j]: player-index of player j on prior 3-table i (i=0..2, j=0..2). 9 A-players.
        // B[i][j]: player-index of player j on prior 4-table i (i=0..2, j=0..3). 12 B-players.
        var A = new int[3][];
        for (int i = 0; i < 3; i++)
            A[i] = priorThree[i].PlayerIds.Select(g => idx[g]).ToArray();
        var B = new int[3][];
        for (int i = 0; i < 3; i++)
            B[i] = priorFour[i].PlayerIds.Select(g => idx[g]).ToArray();

        // For each leftover-B choice (4^3 = 64), the 3-table and 4-table sub-problems
        // are independent. Decomposing keeps the search at ~97K cheap evaluations.
        var bestTotal = double.MaxValue;
        var bestLeftover = new int[3];
        var bestSigma = new int[3][]; // bestSigma[i][k] = index in B[i] used at new 3-table k
        var bestPi = new int[3][];    // bestPi[i][k]    = index in A[i] used at new 4-table k
        var bestRho = new int[3];     // bestRho[k]      = i of the prior 4-table whose leftover sits at new 4-table k

        for (int l0 = 0; l0 < 4; l0++)
        for (int l1 = 0; l1 < 4; l1++)
        for (int l2 = 0; l2 < 4; l2++)
        {
            int[] leftover = { l0, l1, l2 };
            // going[i] = the 3 indices in B[i] that aren't leftover (i.e. move to 3-tables)
            var going = new int[3][];
            for (int i = 0; i < 3; i++)
            {
                var g = new int[3];
                int gIdx = 0;
                for (int j = 0; j < 4; j++)
                    if (j != leftover[i]) g[gIdx++] = j;
                going[i] = g;
            }

            // --- 3-tables: pick perms σ_0, σ_1, σ_2 of going[i] across new 3-tables 0..2 ---
            double best3 = double.MaxValue;
            int[][] localSigma = new int[3][];
            for (int s0 = 0; s0 < 6; s0++)
            for (int s1 = 0; s1 < 6; s1++)
            for (int s2 = 0; s2 < 6; s2++)
            {
                var σ0 = Perms3[s0]; var σ1 = Perms3[s1]; var σ2 = Perms3[s2];
                double s = 0;
                for (int k = 0; k < 3; k++)
                {
                    var b0 = B[0][going[0][σ0[k]]];
                    var b1 = B[1][going[1][σ1[k]]];
                    var b2 = B[2][going[2][σ2[k]]];
                    s += score[b0, b1] + score[b0, b2] + score[b1, b2];
                }
                if (s < best3)
                {
                    best3 = s;
                    localSigma = new[]
                    {
                        new[] { going[0][σ0[0]], going[0][σ0[1]], going[0][σ0[2]] },
                        new[] { going[1][σ1[0]], going[1][σ1[1]], going[1][σ1[2]] },
                        new[] { going[2][σ2[0]], going[2][σ2[1]], going[2][σ2[2]] },
                    };
                }
            }

            // --- 4-tables: pick perms π_0, π_1, π_2 of A[i] across new 4-tables, plus ρ of leftovers ---
            double best4 = double.MaxValue;
            int[][] localPi = new int[3][];
            int[] localRho = new int[3];
            for (int p0 = 0; p0 < 6; p0++)
            for (int p1 = 0; p1 < 6; p1++)
            for (int p2 = 0; p2 < 6; p2++)
            for (int r = 0; r < 6; r++)
            {
                var π0 = Perms3[p0]; var π1 = Perms3[p1]; var π2 = Perms3[p2];
                var ρ = Perms3[r];
                double s = 0;
                for (int k = 0; k < 3; k++)
                {
                    var a0 = A[0][π0[k]];
                    var a1 = A[1][π1[k]];
                    var a2 = A[2][π2[k]];
                    var bLeftover = B[ρ[k]][leftover[ρ[k]]];
                    s += score[a0, a1] + score[a0, a2] + score[a1, a2];
                    s += score[bLeftover, a0] + score[bLeftover, a1] + score[bLeftover, a2];
                }
                if (s < best4)
                {
                    best4 = s;
                    localPi = new[] { π0.ToArray(), π1.ToArray(), π2.ToArray() };
                    localRho = ρ.ToArray();
                }
            }

            var total = best3 + best4;
            if (total < bestTotal)
            {
                bestTotal = total;
                bestLeftover = leftover;
                bestSigma = localSigma;
                bestPi = localPi;
                bestRho = localRho;
                if (bestTotal <= 1e-12)
                    goto found;
            }
        }
        found:

        var tables = new List<TableAssignment>();
        int tableNumber = 1;
        // 3-tables 1, 2, 3
        for (int k = 0; k < 3; k++)
        {
            tables.Add(new TableAssignment
            {
                TableNumber = tableNumber++,
                PlayerIds = new List<Guid>
                {
                    presentPlayerIds[B[0][bestSigma[0][k]]],
                    presentPlayerIds[B[1][bestSigma[1][k]]],
                    presentPlayerIds[B[2][bestSigma[2][k]]],
                },
            });
        }
        // 4-tables 4, 5, 6
        for (int k = 0; k < 3; k++)
        {
            var leftoverI = bestRho[k];
            tables.Add(new TableAssignment
            {
                TableNumber = tableNumber++,
                PlayerIds = new List<Guid>
                {
                    presentPlayerIds[A[0][bestPi[0][k]]],
                    presentPlayerIds[A[1][bestPi[1][k]]],
                    presentPlayerIds[A[2][bestPi[2][k]]],
                    presentPlayerIds[B[leftoverI][bestLeftover[leftoverI]]],
                },
            });
        }
        return tables;
    }

    /// <summary>
    /// Scores how fair the 3-player assignment is: sum of squared deviations
    /// of each selected player's post-assignment 3-player ratio from the mean.
    /// Lower = fairer distribution.
    /// </summary>
    private static double ScoreThreePlayerFairness(
        List<Guid> threePlayerPool,
        Dictionary<Guid, int> threePlayerCounts,
        Dictionary<Guid, int> attendance)
    {
        if (threePlayerPool.Count == 0)
            return 0;

        var threeSet = new HashSet<Guid>(threePlayerPool);
        // Compute post-assignment ratios for the selected players
        var ratios = new List<double>();
        foreach (var id in threePlayerPool)
        {
            var attended = attendance.GetValueOrDefault(id, 0) + 1; // +1 for this session
            var threeCount = threePlayerCounts.GetValueOrDefault(id, 0) + 1; // +1 for being assigned
            ratios.Add((double)threeCount / attended);
        }

        var mean = ratios.Average();
        return ratios.Sum(r => (r - mean) * (r - mean));
    }

    /// <summary>
    /// Computes the ideal (minimum possible) 3-player fairness score by deterministically
    /// selecting the players with the lowest 3-player ratios.
    /// </summary>
    private static double ComputeIdealThreeFairnessScore(
        List<Guid> eligible, List<Guid> excluded, int slots,
        Dictionary<Guid, int> threePlayerCounts,
        Dictionary<Guid, int> attendance)
    {
        if (slots == 0)
            return 0;

        // Deterministically pick the best candidates (lowest ratio, highest attendance)
        var sorted = eligible
            .OrderBy(id =>
            {
                var attended = attendance.GetValueOrDefault(id, 0);
                var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                return attended == 0 ? 0.0 : (double)threeCount / attended;
            })
            .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
            .ToList();

        List<Guid> idealPool;
        if (sorted.Count >= slots)
        {
            idealPool = sorted.Take(slots).ToList();
        }
        else
        {
            var sortedExcluded = excluded
                .OrderBy(id =>
                {
                    var attended = attendance.GetValueOrDefault(id, 0);
                    var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                    return attended == 0 ? 0.0 : (double)threeCount / attended;
                })
                .ThenByDescending(id => attendance.GetValueOrDefault(id, 0))
                .ToList();
            var remaining = slots - sorted.Count;
            idealPool = sorted.Concat(sortedExcluded.Take(remaining)).ToList();
        }

        return ScoreThreePlayerFairness(idealPool, threePlayerCounts, attendance);
    }

    /// <summary>
    /// Greedy algorithm that forms tables trying to minimize repeated meetings.
    /// Uses meeting ratios: meetingCount / min(attendanceA, attendanceB) so that
    /// players who attend less often are not unfairly penalized for having met someone.
    /// </summary>
    private static List<List<Guid>> FormTablesGreedy(
        List<Guid> playerGuids, int tableSize,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        if (playerGuids.Count == 0)
            return new List<List<Guid>>();

        var numberOfTables = playerGuids.Count / tableSize;
        if (numberOfTables == 0)
            return new List<List<Guid>>();

        List<List<Guid>>? bestAssignment = null;
        var bestScore = double.MaxValue;
        var attempts = Math.Min(20, Math.Max(5, playerGuids.Count));

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var remaining = new List<Guid>(playerGuids);
            Shuffle(remaining);

            var tables = new List<List<Guid>>();
            for (int t = 0; t < numberOfTables; t++)
                tables.Add(new List<Guid>());

            // Seed each table with one player
            for (int t = 0; t < numberOfTables; t++)
            {
                tables[t].Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            // Fill tables round-robin, picking the best fit each time
            for (int seat = 1; seat < tableSize; seat++)
            {
                for (int t = 0; t < numberOfTables; t++)
                {
                    if (remaining.Count == 0)
                        break;

                    var bestPlayer = remaining[0];
                    var bestPlayerScore = PairScoreRatio(tables[t], remaining[0], meetingCounts, attendance);

                    for (int p = 1; p < remaining.Count; p++)
                    {
                        var score = PairScoreRatio(tables[t], remaining[p], meetingCounts, attendance);
                        if (score < bestPlayerScore)
                        {
                            bestPlayerScore = score;
                            bestPlayer = remaining[p];
                        }
                    }

                    tables[t].Add(bestPlayer);
                    remaining.Remove(bestPlayer);
                }
            }

            var totalScore = ScoreAssignmentRatio(tables, meetingCounts, attendance);
            if (totalScore < bestScore)
            {
                bestScore = totalScore;
                bestAssignment = tables;
            }
        }

        return bestAssignment!;
    }

    /// <summary>
    /// Score for placing a candidate at a table: sum of meeting ratios with each existing member.
    /// Ratio = meetingCount / min(attendanceA, attendanceB).
    /// This normalizes so that a pair who met 2 out of 3 possible times (0.67)
    /// scores higher than a pair who met 2 out of 10 possible times (0.20).
    /// </summary>
    private static double PairScoreRatio(
        List<Guid> tableMembers, Guid candidate,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        double score = 0;
        var candidateAttendance = attendance.GetValueOrDefault(candidate, 0);

        foreach (var member in tableMembers)
        {
            var key = MakeKey(member, candidate);
            var met = meetingCounts.GetValueOrDefault(key, 0);
            var memberAttendance = attendance.GetValueOrDefault(member, 0);

            // min(attendanceA, attendanceB) = max times they could have met
            var coAttendancePossible = Math.Min(candidateAttendance, memberAttendance);
            if (coAttendancePossible > 0)
                score += (double)met / coAttendancePossible;
            // If neither has attendance history yet, score stays 0 (neutral)
        }

        return score;
    }

    /// <summary>
    /// Total score for an assignment: sum of all pair meeting ratios across all tables.
    /// </summary>
    private static double ScoreAssignmentRatio(
        List<List<Guid>> tables,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        double total = 0;
        foreach (var table in tables)
        {
            for (int i = 0; i < table.Count; i++)
            {
                for (int j = i + 1; j < table.Count; j++)
                {
                    var key = MakeKey(table[i], table[j]);
                    var met = meetingCounts.GetValueOrDefault(key, 0);
                    var possibleCoAttendance = Math.Min(
                        attendance.GetValueOrDefault(table[i], 0),
                        attendance.GetValueOrDefault(table[j], 0));

                    if (possibleCoAttendance > 0)
                        total += (double)met / possibleCoAttendance;
                }
            }
        }
        return total;
    }

    /// <summary>
    /// Selects which eligible players go to 3-player tables using tiered selection.
    /// Players with clearly lower 3-player ratios are always included.
    /// Among borderline players (same ratio at the cutoff), groups by attendance tier
    /// and fills from highest attendance first (fairness). Within a partial tier,
    /// uses meeting scores to pick the candidates who have met the group the least.
    /// </summary>
    private static List<Guid> SelectThreePlayerCandidates(
        List<Guid> sortedEligible, int slots,
        Dictionary<Guid, int> threePlayerCounts,
        Dictionary<Guid, int> attendance,
        Dictionary<string, int> meetingCounts)
    {
        if (slots <= 0)
            return new List<Guid>();

        if (sortedEligible.Count <= slots)
            return sortedEligible.ToList();

        double ThreeRatio(Guid id)
        {
            var attended = attendance.GetValueOrDefault(id, 0);
            var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
            return attended == 0 ? 0.0 : (double)threeCount / attended;
        }

        var cutoffRatio = ThreeRatio(sortedEligible[slots - 1]);

        const double ratioEpsilon = 1e-9;
        var mustInclude = new List<Guid>();
        var borderline = new List<Guid>();

        foreach (var id in sortedEligible)
        {
            var ratio = ThreeRatio(id);
            if (ratio < cutoffRatio - ratioEpsilon)
                mustInclude.Add(id);
            else if (Math.Abs(ratio - cutoffRatio) <= ratioEpsilon)
                borderline.Add(id);
        }

        var remainingSlots = slots - mustInclude.Count;

        if (remainingSlots <= 0)
            return mustInclude.Take(slots).ToList();

        if (borderline.Count <= remainingSlots)
            return mustInclude.Concat(borderline).Take(slots).ToList();

        // Group borderline by attendance tiers, process highest attendance first (fairness)
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
                selected.AddRange(tierPlayers);
                slotsLeft -= tierPlayers.Count;
            }
            else
            {
                // Partial tier: greedy selection by meeting score against already-selected
                var pool = new List<Guid>(tierPlayers);
                for (int i = 0; i < slotsLeft; i++)
                {
                    var bestCandidate = pool[0];
                    var bestScore = MeetingScoreAgainstGroup(selected, pool[0], meetingCounts, attendance);

                    for (int c = 1; c < pool.Count; c++)
                    {
                        var score = MeetingScoreAgainstGroup(selected, pool[c], meetingCounts, attendance);
                        if (score < bestScore || (Math.Abs(score - bestScore) < 1e-9 && Random.Shared.Next(2) == 0))
                        {
                            bestScore = score;
                            bestCandidate = pool[c];
                        }
                    }

                    selected.Add(bestCandidate);
                    pool.Remove(bestCandidate);
                }
                slotsLeft = 0;
            }
        }

        return selected;
    }

    /// <summary>
    /// Sum of meeting ratios between a candidate and each member of a group.
    /// Lower = candidate has met the group less. Used within attendance tiers
    /// to pick 3-player candidates with the least meeting overlap.
    /// </summary>
    private static double MeetingScoreAgainstGroup(
        List<Guid> groupMembers, Guid candidate,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        double score = 0;
        var candidateAttendance = attendance.GetValueOrDefault(candidate, 0);

        foreach (var member in groupMembers)
        {
            var key = MakeKey(member, candidate);
            var met = meetingCounts.GetValueOrDefault(key, 0);
            var memberAttendance = attendance.GetValueOrDefault(member, 0);
            var coAttendancePossible = Math.Min(candidateAttendance, memberAttendance);
            if (coAttendancePossible > 0)
                score += (double)met / coAttendancePossible;
        }

        return score;
    }

    private static void Shuffle(List<Guid> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    internal static string MakeKey(Guid a, Guid b)
    {
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) < 0
            ? $"{a}_{b}"
            : $"{b}_{a}";
    }

    internal static (int fourPlayerTables, int threePlayerTables) CalculateNumberofFourAndThreePLayerTables(int playerCount)
    {
        int fourPlayerTables;
        int threePlayerTables;
        var remainder = playerCount % 4;
       
        switch (remainder)
        {
            case 0:
                fourPlayerTables = playerCount / 4;
                threePlayerTables = 0;
                break;
            case 1:
                if (playerCount >= 9)
                {
                    threePlayerTables = 3;
                    fourPlayerTables = (playerCount - 9) / 4;
                }
                else if (playerCount == 5)
                {
                    fourPlayerTables = 1;
                    threePlayerTables = 0;
                }
                else
                {
                    fourPlayerTables = 0;
                    threePlayerTables = 0;
                }
                break;
            case 2:
                if (playerCount >= 6)
                {
                    threePlayerTables = 2;
                    fourPlayerTables = (playerCount - 6) / 4;
                }
                else
                {
                    fourPlayerTables = 0;
                    threePlayerTables = 0;
                }
                break;
            case 3:
                threePlayerTables = 1;
                fourPlayerTables = (playerCount - 3) / 4;
                break;
            default:
                fourPlayerTables = 0;
                threePlayerTables = 0;
                break;
        }
        return(fourPlayerTables,threePlayerTables);
    }

    /// <summary>
    /// For each present player, finds their most recent attended session and checks
    /// if they were at a 3-player table. Returns the set of players who were.
    /// </summary>
    private static HashSet<Guid> FindPlayersAtThreeTableLastSession(
        List<Hanchan> history, List<Guid> presentPlayerIds)
    {
        var result = new HashSet<Guid>();
        var sessionsDescending = history
            .Where(s => s.IsFinalized)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.StartTime ?? TimeSpan.Zero)
            .ToList();

        foreach (var playerId in presentPlayerIds)
        {
            // Find the most recent session this player attended
            foreach (var session in sessionsDescending)
            {
                if (!session.PresentMemberIds.Contains(playerId))
                    continue;

                // Found their last session — check if they were at a 3-player table
                foreach (var table in session.Tables)
                {
                    if (table.PlayerCount == 3 && table.PlayerIds.Contains(playerId))
                    {
                        result.Add(playerId);
                    }
                }
                break; // only check the most recent attended session
            }
        }

        return result;
    }

    private static Dictionary<Guid, int> CountAttendance(
        List<Hanchan> history, List<Guid> relevantPlayerIds)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var id in relevantPlayerIds)
            counts[id] = 0;

        foreach (var session in history)
        {
            foreach (var playerId in session.PresentMemberIds)
            {
                if (counts.ContainsKey(playerId))
                    counts[playerId]++;
            }
        }

        return counts;
    }

    private static Dictionary<Guid, int> CountThreePlayerAssignments(
        List<Hanchan> history, List<Guid> relevantPlayerIds,
        Dictionary<Guid, int>? extraCounts = null)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var id in relevantPlayerIds)
            counts[id] = extraCounts?.GetValueOrDefault(id, 0) ?? 0;

        foreach (var session in history)
        {
            foreach (var table in session.Tables)
            {
                if (table.PlayerCount == 3)
                {
                    foreach (var playerId in table.PlayerIds)
                    {
                        if (counts.ContainsKey(playerId))
                            counts[playerId]++;
                    }
                }
            }
        }

        return counts;
    }

    private static Dictionary<string, int> BuildMeetingMatrix(
        List<Hanchan> history, List<Guid> relevantPlayerIds)
    {
        var matrix = new Dictionary<string, int>();
        var relevantSet = new HashSet<Guid>(relevantPlayerIds);

        foreach (var session in history)
        {
            foreach (var table in session.Tables)
            {
                for (int i = 0; i < table.PlayerIds.Count; i++)
                {
                    if (!relevantSet.Contains(table.PlayerIds[i]))
                        continue;

                    for (int j = i + 1; j < table.PlayerIds.Count; j++)
                    {
                        if (!relevantSet.Contains(table.PlayerIds[j]))
                            continue;

                        var key = MakeKey(table.PlayerIds[i], table.PlayerIds[j]);
                        matrix[key] = matrix.GetValueOrDefault(key, 0) + 1;
                    }
                }
            }
        }

        return matrix;
    }
}
