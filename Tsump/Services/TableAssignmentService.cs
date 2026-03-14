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

    public async Task<List<TableAssignment>> AssignTables(List<Guid> presentPlayerIds)
    {
        var playerCount = presentPlayerIds.Count;
        if (playerCount < 3)
            return new List<TableAssignment>();

        (int fourPlayerTables, int threePlayerTables) tableNumbers = CalculateNumberofFourAndThreePLayerTables(playerCount);
        var threePlayerTables=tableNumbers.threePlayerTables;

        List <WeeklySession> allHistory = await _sessionService.GetAllAsync();
        List<WeeklySession> history = allHistory.Where(s => !s.ExcludeFromOptimization).ToList();
        List<Member> members = await _memberService.GetAllAsync();
        Dictionary<Guid, int> extraCounts = members.ToDictionary(m => m.Id, m => m.ExtraThreePlayerTableCount);
        Dictionary<Guid, int> attendance = CountAttendance(history, presentPlayerIds);
        Dictionary<Guid, int> threePlayerCounts = CountThreePlayerAssignments(history, presentPlayerIds, extraCounts);
        Dictionary<string, int> meetingCounts = BuildMeetingMatrix(history, presentPlayerIds);

        // Hard constraint: players who were at a 3-player table in their last attended
        // session cannot be assigned to a 3-player table again
        HashSet<Guid> playedThreeLastTime = FindPlayersAtThreeTableLastSession(history, presentPlayerIds);

        // Priority 1: select who goes to 3-player tables
        // Sort by ratio: threePlayerCount / attendanceCount (lowest ratio first)
        // But exclude players who had a 3-player table last time (hard constraint)
        List<Guid> eligible = presentPlayerIds.Where(id => !playedThreeLastTime.Contains(id)).ToList();
        List<Guid> excluded = presentPlayerIds.Where(id => playedThreeLastTime.Contains(id)).ToList();

        List<Guid> sortedEligible = eligible
            .OrderBy(id =>
            {
                var attended = attendance.GetValueOrDefault(id, 0);
                var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                return attended == 0 ? 0.0 : (double)threeCount / attended;
            })
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        var threePlayerSlots = threePlayerTables * 3;

        List<Guid> threePlayerPool;
        List<Guid> fourPlayerPool;

        if (sortedEligible.Count >= threePlayerSlots)
        {
            // Enough eligible players — fill 3-player tables from eligible only
            threePlayerPool = sortedEligible.Take(threePlayerSlots).ToList();
            fourPlayerPool = sortedEligible.Skip(threePlayerSlots).Concat(excluded).ToList();
        }
        else
        {
            // Not enough eligible players — use all eligible, then fill remaining
            // from excluded (sorted by ratio, so least-penalized go first)
            var sortedExcluded = excluded
                .OrderBy(id =>
                {
                    var attended = attendance.GetValueOrDefault(id, 0);
                    var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                    return attended == 0 ? 0.0 : (double)threeCount / attended;
                })
                .ThenBy(_ => Random.Shared.Next())
                .ToList();
  
            var remaining = threePlayerSlots - sortedEligible.Count;
            threePlayerPool = sortedEligible.Concat(sortedExcluded.Take(remaining)).ToList();
            fourPlayerPool = sortedExcluded.Skip(remaining).ToList();
        }

        // Priority 2: within each pool, form tables to minimize repeated meetings (by ratio)
        var tables = new List<TableAssignment>();
        var tableNumber = 1;

        List<List<Guid>> threePlayerAssignments = FormTablesGreedy(threePlayerPool, 3, meetingCounts, attendance);
        foreach (var group in threePlayerAssignments)
        {
            tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });
        }

        List<List<Guid>> fourPlayerAssignments = FormTablesGreedy(fourPlayerPool, 4, meetingCounts, attendance);
        foreach (var group in fourPlayerAssignments)
        {
            tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });
        }

        return tables;
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
        List<WeeklySession> history, List<Guid> presentPlayerIds)
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
        List<WeeklySession> history, List<Guid> relevantPlayerIds)
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
        List<WeeklySession> history, List<Guid> relevantPlayerIds,
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
        List<WeeklySession> history, List<Guid> relevantPlayerIds)
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
