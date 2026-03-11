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

    public TableAssignmentService(SessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async Task<List<TableAssignment>> AssignTables(List<Guid> presentPlayerIds)
    {
        var playerCount = presentPlayerIds.Count;
        if (playerCount < 3)
            return new List<TableAssignment>();

        CalculateTableCounts(playerCount, out var fourPlayerTables, out var threePlayerTables);

        var history = await _sessionService.GetAllAsync();
        var attendance = CountAttendance(history, presentPlayerIds);
        var threePlayerCounts = CountThreePlayerAssignments(history, presentPlayerIds);
        var meetingCounts = BuildMeetingMatrix(history, presentPlayerIds);

        // Priority 1: select who goes to 3-player tables
        // Sort by ratio: threePlayerCount / attendanceCount (lowest ratio first)
        var sortedByThreeRatio = presentPlayerIds
            .OrderBy(id =>
            {
                var attended = attendance.GetValueOrDefault(id, 0);
                var threeCount = threePlayerCounts.GetValueOrDefault(id, 0);
                // Players who never attended yet get ratio 0 (should go to 3-player table)
                return attended == 0 ? 0.0 : (double)threeCount / attended;
            })
            .ThenBy(_ => Random.Shared.Next())
            .ToList();

        var threePlayerSlots = threePlayerTables * 3;
        var threePlayerPool = sortedByThreeRatio.Take(threePlayerSlots).ToList();
        var fourPlayerPool = sortedByThreeRatio.Skip(threePlayerSlots).ToList();

        // Priority 2: within each pool, form tables to minimize repeated meetings (by ratio)
        var tables = new List<TableAssignment>();
        var tableNumber = 1;

        var threePlayerAssignments = FormTablesGreedy(threePlayerPool, 3, meetingCounts, attendance);
        foreach (var group in threePlayerAssignments)
        {
            tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });
        }

        var fourPlayerAssignments = FormTablesGreedy(fourPlayerPool, 4, meetingCounts, attendance);
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
        List<Guid> players, int tableSize,
        Dictionary<string, int> meetingCounts,
        Dictionary<Guid, int> attendance)
    {
        if (players.Count == 0)
            return new List<List<Guid>>();

        var tableCount = players.Count / tableSize;
        if (tableCount == 0)
            return new List<List<Guid>>();

        var bestAssignment = (List<List<Guid>>?)null;
        var bestScore = double.MaxValue;
        var attempts = Math.Min(20, Math.Max(5, players.Count));

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var remaining = new List<Guid>(players);
            Shuffle(remaining);

            var tables = new List<List<Guid>>();
            for (int t = 0; t < tableCount; t++)
                tables.Add(new List<Guid>());

            // Seed each table with one player
            for (int t = 0; t < tableCount; t++)
            {
                tables[t].Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            // Fill tables round-robin, picking the best fit each time
            for (int seat = 1; seat < tableSize; seat++)
            {
                for (int t = 0; t < tableCount; t++)
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

    private static string MakeKey(Guid a, Guid b)
    {
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal) < 0
            ? $"{a}_{b}"
            : $"{b}_{a}";
    }

    private static void CalculateTableCounts(int playerCount, out int fourPlayerTables, out int threePlayerTables)
    {
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
        List<WeeklySession> history, List<Guid> relevantPlayerIds)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var id in relevantPlayerIds)
            counts[id] = 0;

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
