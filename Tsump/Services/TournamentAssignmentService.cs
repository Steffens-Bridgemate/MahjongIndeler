using Tsump.Models;

namespace Tsump.Services;

/// <summary>
/// Generates table assignments for all tournament sessions at once.
/// Adopts the fixed player movement pattern from MahjongMeetingsManagement:
/// - Players are split into 4 groups (remaining, +1, +2, +3)
/// - "Remaining" players stay fixed at their table
/// - +1/+2/+3 players rotate across tables between sessions with different offsets
/// - For 3-player tables: phantom players are added then removed after rotation
/// </summary>
public class TournamentAssignmentService
{
    public List<TournamentSession> GenerateAllSessions(
        List<TournamentParticipant> participants, int sessionCount)
    {
        var playerCount = participants.Count;
        if (playerCount < 3)
            return new List<TournamentSession>();

        // Determine how many 3-player tables are needed
        var numberOfTablesForThreePlayers = (4 - playerCount % 4) % 4;

        // Work with virtual 4-player tables (add phantoms for 3-player tables)
        var virtualPlayerCount = playerCount + numberOfTablesForThreePlayers;
        var numberOfTables = virtualPlayerCount / 4;

        // Create numbered player slots (1-based, matching the original algorithm)
        var sortedParticipants = participants.OrderBy(p => p.Number).ToList();

        // Phantom player IDs (will be removed from final output)
        var phantomIds = new List<Guid>();
        for (int i = 0; i < numberOfTablesForThreePlayers; i++)
            phantomIds.Add(Guid.NewGuid());

        // Build the full player list: real participants + phantoms appended at end
        var allPlayerIds = sortedParticipants.Select(p => p.Id).Concat(phantomIds).ToList();

        // Swap phantoms into "remaining" positions (1-based: 1, 5, 9, ...)
        // so each phantom ends up at a different table's fixed slot.
        // This matches RenumberForTablesOfThreePlayers from the original algorithm.
        for (int i = 0; i < numberOfTablesForThreePlayers; i++)
        {
            var phantomIndex = playerCount + i;          // where phantom currently is (0-based)
            var remainingIndex = i * 4;                  // "remaining" slot to swap into (0-based positions 0, 4, 8, ...)
            (allPlayerIds[phantomIndex], allPlayerIds[remainingIndex]) =
                (allPlayerIds[remainingIndex], allPlayerIds[phantomIndex]);
        }

        // Split into 4 groups based on 1-based position modulo 4
        var remainingPlayers = new List<Guid>(); // position % 4 == 1
        var plusOnePlayers = new List<Guid>();    // position % 4 == 2
        var plusTwoPlayers = new List<Guid>();    // position % 4 == 3
        var plusThreePlayers = new List<Guid>();  // position % 4 == 0

        for (int i = 0; i < allPlayerIds.Count; i++)
        {
            var pos = i + 1;
            switch (pos % 4)
            {
                case 1: remainingPlayers.Add(allPlayerIds[i]); break;
                case 2: plusOnePlayers.Add(allPlayerIds[i]); break;
                case 3: plusTwoPlayers.Add(allPlayerIds[i]); break;
                case 0: plusThreePlayers.Add(allPlayerIds[i]); break;
            }
        }

        // Build table assignments per session using fixed movement pattern
        var tablesInSessions = new Dictionary<(int sessionNr, int tableNr),
            (Guid remaining, Guid plusOne, Guid plusTwo, Guid plusThree)>();

        var sessions = new List<TournamentSession>();
        var phantomSet = new HashSet<Guid>(phantomIds);

        for (int sessionNr = 1; sessionNr <= sessionCount; sessionNr++)
        {
            var tables = new List<TableAssignment>();

            for (int tableNr = 1; tableNr <= numberOfTables; tableNr++)
            {
                Guid remaining, plusOne, plusTwo, plusThree;

                if (sessionNr == 1)
                {
                    // First session: straightforward assignment
                    var idx = tableNr - 1;
                    remaining = remainingPlayers[idx];
                    plusOne = plusOnePlayers[idx];
                    plusTwo = plusTwoPlayers[idx];
                    plusThree = plusThreePlayers[idx];
                }
                else if (numberOfTables > 4 || (numberOfTables == 4 && sessionCount > 2))
                {
                    // Fixed player movement pattern
                    remaining = remainingPlayers[tableNr - 1];

                    var skipRound = (numberOfTables - 1) / 3 + 2;
                    var extraSkip = (numberOfTables % 2 == 0 && sessionNr == skipRound) ? 1 : 0;

                    var prevPlusOneTable = tableNr - 1 - extraSkip;
                    if (prevPlusOneTable <= 0)
                        prevPlusOneTable = numberOfTables + prevPlusOneTable;
                    plusOne = tablesInSessions[(sessionNr - 1, prevPlusOneTable)].plusOne;

                    var prevPlusTwoTable = tableNr - 2 - extraSkip;
                    while (prevPlusTwoTable <= 0)
                        prevPlusTwoTable = numberOfTables + prevPlusTwoTable;
                    plusTwo = tablesInSessions[(sessionNr - 1, prevPlusTwoTable)].plusTwo;

                    var prevPlusThreeTable = tableNr - 3 - extraSkip;
                    while (prevPlusThreeTable <= 0)
                        prevPlusThreeTable = numberOfTables + prevPlusThreeTable;
                    plusThree = tablesInSessions[(sessionNr - 1, prevPlusThreeTable)].plusThree;
                }
                else if (numberOfTables == 4)
                {
                    // 4 tables, <= 2 sessions: simple rotation
                    remaining = remainingPlayers[tableNr - 1];
                    var offset = sessionNr - 2 + tableNr - 1;
                    plusOne = plusOnePlayers[offset % numberOfTables];
                    plusTwo = plusTwoPlayers[offset % numberOfTables];
                    plusThree = plusThreePlayers[offset % numberOfTables];
                }
                else // numberOfTables == 3
                {
                    // 3 tables: hardcoded optimal patterns
                    (remaining, plusOne, plusTwo, plusThree) =
                        GetThreeTablePattern(allPlayerIds, sessionNr, tableNr);
                }

                tablesInSessions[(sessionNr, tableNr)] = (remaining, plusOne, plusTwo, plusThree);

                // Build the table, removing phantom players
                var playerIds = new List<Guid> { remaining, plusOne, plusTwo, plusThree }
                    .Where(id => !phantomSet.Contains(id))
                    .ToList();

                tables.Add(new TableAssignment
                {
                    TableNumber = tableNr,
                    PlayerIds = playerIds
                });
            }

            // Renumber: 3-player tables first, then 4-player
            var reordered = tables
                .OrderBy(t => t.PlayerCount == 3 ? 0 : 1)
                .ThenBy(t => t.TableNumber)
                .ToList();
            for (int i = 0; i < reordered.Count; i++)
                reordered[i].TableNumber = i + 1;

            sessions.Add(new TournamentSession
            {
                SessionNumber = sessionNr,
                Tables = reordered
            });
        }

        BalanceThreePlayerDistribution(sessions);

        return sessions;
    }

    /// <summary>
    /// Post-processing: balance 3-player table assignments across participants.
    /// Swaps players between 3-player and 4-player tables in the same session
    /// until the max difference in 3-player count is at most 1.
    /// </summary>
    private static void BalanceThreePlayerDistribution(List<TournamentSession> sessions)
    {
        // Collect all real player IDs
        var allPlayerIds = sessions
            .SelectMany(s => s.Tables.SelectMany(t => t.PlayerIds))
            .Distinct()
            .ToList();

        if (allPlayerIds.Count == 0) return;

        // Build meeting count matrix: how often each pair of players sits at the same table
        var meetingCounts = new Dictionary<(Guid, Guid), int>();
        foreach (var session in sessions)
            foreach (var table in session.Tables)
                for (int i = 0; i < table.PlayerIds.Count; i++)
                    for (int j = i + 1; j < table.PlayerIds.Count; j++)
                    {
                        var key = table.PlayerIds[i].CompareTo(table.PlayerIds[j]) < 0
                            ? (table.PlayerIds[i], table.PlayerIds[j])
                            : (table.PlayerIds[j], table.PlayerIds[i]);
                        meetingCounts[key] = meetingCounts.GetValueOrDefault(key) + 1;
                    }

        int GetMeetings(Guid a, Guid b)
        {
            var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
            return meetingCounts.GetValueOrDefault(key);
        }

        // Count how many meetings a player would have with the other players at a table
        int TableMeetings(Guid player, TableAssignment table)
        {
            int count = 0;
            foreach (var id in table.PlayerIds)
                if (id != player)
                    count += GetMeetings(player, id);
            return count;
        }

        void UpdateMeetings(Guid player, List<Guid> tablemates, int delta)
        {
            foreach (var id in tablemates)
            {
                if (id == player) continue;
                var key = player.CompareTo(id) < 0 ? (player, id) : (id, player);
                meetingCounts[key] = meetingCounts.GetValueOrDefault(key) + delta;
            }
        }

        for (int iteration = 0; iteration < 1000; iteration++)
        {
            // Count 3-player table assignments per player
            var threePlayerCount = new Dictionary<Guid, int>();
            foreach (var id in allPlayerIds)
                threePlayerCount[id] = 0;

            foreach (var session in sessions)
                foreach (var table in session.Tables)
                    if (table.PlayerCount == 3)
                        foreach (var id in table.PlayerIds)
                            threePlayerCount[id]++;

            var minCount = threePlayerCount.Values.Min();
            var maxCount = threePlayerCount.Values.Max();

            if (maxCount - minCount <= 1)
                break;

            // Find an over-assigned player
            var overPlayer = threePlayerCount
                .Where(kv => kv.Value == maxCount)
                .Select(kv => kv.Key)
                .First();

            // Find all possible swaps: underPlayers at a 4-player table
            // in a session where overPlayer is at a 3-player table
            var candidates = new List<(Guid underPlayer, TournamentSession session,
                TableAssignment threeTable, TableAssignment fourTable, int meetingScore)>();

            var underPlayerIds = threePlayerCount
                .Where(kv => kv.Value == minCount)
                .Select(kv => kv.Key)
                .ToHashSet();

            foreach (var session in sessions)
            {
                TableAssignment? threeTable = null;
                foreach (var table in session.Tables)
                    if (table.PlayerCount == 3 && table.PlayerIds.Contains(overPlayer))
                    { threeTable = table; break; }

                if (threeTable == null) continue;

                foreach (var table in session.Tables)
                {
                    if (table.PlayerCount != 4) continue;
                    foreach (var candidate in table.PlayerIds)
                    {
                        if (!underPlayerIds.Contains(candidate)) continue;

                        // Score: fewer meetings with new tablemates is better
                        // underPlayer moves to 3-player table, overPlayer moves to 4-player table
                        var score = TableMeetings(candidate, threeTable)
                                  + TableMeetings(overPlayer, table);

                        candidates.Add((candidate, session, threeTable, table, score));
                    }
                }
            }

            if (candidates.Count == 0)
                break;

            // Pick the swap that minimizes meetings between swapped players and their new tablemates
            var best = candidates.OrderBy(c => c.meetingScore).First();

            // Update meeting counts: remove old pairings, add new ones
            UpdateMeetings(overPlayer, best.threeTable.PlayerIds, -1);
            UpdateMeetings(best.underPlayer, best.fourTable.PlayerIds, -1);

            // Perform the swap
            var overIdx = best.threeTable.PlayerIds.IndexOf(overPlayer);
            var underIdx = best.fourTable.PlayerIds.IndexOf(best.underPlayer);
            best.threeTable.PlayerIds[overIdx] = best.underPlayer;
            best.fourTable.PlayerIds[underIdx] = overPlayer;

            // Add new pairings
            UpdateMeetings(best.underPlayer, best.threeTable.PlayerIds, 1);
            UpdateMeetings(overPlayer, best.fourTable.PlayerIds, 1);
        }
    }

    /// <summary>
    /// Hardcoded optimal patterns for 3 tables (12 players), sessions 2 and 3.
    /// </summary>
    private static (Guid remaining, Guid plusOne, Guid plusTwo, Guid plusThree) GetThreeTablePattern(
        List<Guid> players, int sessionNr, int tableNr)
    {
        if (sessionNr == 2)
        {
            return tableNr switch
            {
                1 => (players[0], players[1], players[6], players[10]),
                2 => (players[2], players[4], players[5], players[11]),
                3 => (players[3], players[7], players[8], players[9]),
                _ => default
            };
        }
        if (sessionNr == 3)
        {
            return tableNr switch
            {
                1 => (players[0], players[5], players[6], players[9]),
                2 => (players[1], players[2], players[7], players[8]),
                3 => (players[3], players[4], players[10], players[11]),
                _ => default
            };
        }
        return default;
    }
}
