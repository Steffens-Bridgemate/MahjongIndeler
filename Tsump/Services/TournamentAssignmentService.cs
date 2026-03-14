using Tsump.Models;

namespace Tsump.Services;

public class TournamentAssignmentService
{
    public List<TournamentSession> GenerateAllSessions(
        List<TournamentParticipant> participants, int sessionCount)
    {
        var playerCount = participants.Count;
        if (playerCount < 3)
            return new List<TournamentSession>();

        var (fourPlayerTables, threePlayerTables) =
            TableAssignmentService.CalculateNumberofFourAndThreePLayerTables(playerCount);

        var threePlayerSlots = threePlayerTables * 3;

        // Running counters across all sessions
        var threeCount = new Dictionary<Guid, int>();
        var meetingCounts = new Dictionary<string, int>();
        foreach (var p in participants)
            threeCount[p.Id] = 0;

        var sessions = new List<TournamentSession>();

        for (int s = 0; s < sessionCount; s++)
        {
            // Priority 1: select who goes to 3-player tables
            // Sort by running 3-player count (lowest first), break ties by participant number
            var sorted = participants
                .OrderBy(p => threeCount[p.Id])
                .ThenBy(p => p.Number)
                .ToList();

            var threePlayerPool = sorted.Take(threePlayerSlots).ToList();
            var fourPlayerPool = sorted.Skip(threePlayerSlots).ToList();

            // Priority 2: form tables minimizing meeting repetition
            var tables = new List<TableAssignment>();
            var tableNumber = 1;

            // Use a prime number for seed rotation to vary table compositions across sessions
            var prime = GetPrimeAbove(Math.Max(threePlayerPool.Count, fourPlayerPool.Count));

            var threeAssignments = FormTablesDeterministic(
                threePlayerPool.Select(p => p.Id).ToList(), 3, meetingCounts,
                participants, s, prime);
            foreach (var group in threeAssignments)
                tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });

            var fourAssignments = FormTablesDeterministic(
                fourPlayerPool.Select(p => p.Id).ToList(), 4, meetingCounts,
                participants, s, prime);
            foreach (var group in fourAssignments)
                tables.Add(new TableAssignment { TableNumber = tableNumber++, PlayerIds = group });

            // Update running counters
            foreach (var p in threePlayerPool)
                threeCount[p.Id]++;

            foreach (var table in tables)
            {
                for (int i = 0; i < table.PlayerIds.Count; i++)
                {
                    for (int j = i + 1; j < table.PlayerIds.Count; j++)
                    {
                        var key = TableAssignmentService.MakeKey(table.PlayerIds[i], table.PlayerIds[j]);
                        meetingCounts[key] = meetingCounts.GetValueOrDefault(key, 0) + 1;
                    }
                }
            }

            sessions.Add(new TournamentSession { SessionNumber = s + 1, Tables = tables });
        }

        return sessions;
    }

    private static List<List<Guid>> FormTablesDeterministic(
        List<Guid> playerIds, int tableSize,
        Dictionary<string, int> meetingCounts,
        List<TournamentParticipant> allParticipants,
        int sessionIndex, int prime)
    {
        if (playerIds.Count == 0)
            return new List<List<Guid>>();

        var numberOfTables = playerIds.Count / tableSize;
        if (numberOfTables == 0)
            return new List<List<Guid>>();

        // Build a lookup for participant number
        var numberLookup = allParticipants.ToDictionary(p => p.Id, p => p.Number);

        // Rotate seed order per session for variety
        var sorted = playerIds
            .OrderBy(id => (numberLookup.GetValueOrDefault(id, 0) + sessionIndex * prime) % playerIds.Count)
            .ThenBy(id => numberLookup.GetValueOrDefault(id, 0))
            .ToList();

        var tables = new List<List<Guid>>();
        for (int t = 0; t < numberOfTables; t++)
            tables.Add(new List<Guid>());

        // Seed each table with one player
        for (int t = 0; t < numberOfTables; t++)
        {
            tables[t].Add(sorted[t]);
        }

        var remaining = new List<Guid>(sorted.Skip(numberOfTables));

        // Fill tables round-robin, picking the best fit each time
        for (int seat = 1; seat < tableSize; seat++)
        {
            for (int t = 0; t < numberOfTables; t++)
            {
                if (remaining.Count == 0)
                    break;

                // Find the candidate with the lowest meeting score with current table members
                Guid bestCandidate = remaining[0];
                int bestScore = PairScore(tables[t], remaining[0], meetingCounts);

                for (int p = 1; p < remaining.Count; p++)
                {
                    var score = PairScore(tables[t], remaining[p], meetingCounts);
                    if (score < bestScore ||
                        (score == bestScore && numberLookup.GetValueOrDefault(remaining[p], 0) <
                         numberLookup.GetValueOrDefault(bestCandidate, 0)))
                    {
                        bestScore = score;
                        bestCandidate = remaining[p];
                    }
                }

                tables[t].Add(bestCandidate);
                remaining.Remove(bestCandidate);
            }
        }

        return tables;
    }

    private static int PairScore(List<Guid> tableMembers, Guid candidate,
        Dictionary<string, int> meetingCounts)
    {
        int score = 0;
        foreach (var member in tableMembers)
        {
            var key = TableAssignmentService.MakeKey(member, candidate);
            score += meetingCounts.GetValueOrDefault(key, 0);
        }
        return score;
    }

    private static int GetPrimeAbove(int n)
    {
        // Return a prime number larger than n for seed rotation
        var candidate = Math.Max(n + 1, 7);
        while (!IsPrime(candidate))
            candidate++;
        return candidate;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n < 4) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (int i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0)
                return false;
        }
        return true;
    }
}
