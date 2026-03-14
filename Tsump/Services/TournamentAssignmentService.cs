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

        return sessions;
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
