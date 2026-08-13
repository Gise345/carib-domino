#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Pose.Core;

namespace Pose.Tools.ReplayFixtures
{
    /// <summary>
    /// Emits the parity fixtures the TypeScript rule engine is tested against.
    /// The C# engine is the reference; the fixtures capture (seed, players,
    /// moves) plus the outcome C# computes, and the Vitest suite asserts the TS
    /// port reproduces them. Deterministic — the move-selection RNG is fixed-
    /// seeded — so re-running produces byte-identical files (regenerate whenever
    /// the C# engine changes; see docs/DECISIONS/0007).
    /// </summary>
    internal static class Program
    {
        private const int PrngValuesPerSeed = 16;

        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            WriteJson(Path.Combine(outDir, "prng-fixtures.json"), BuildPrngFixtures());
            WriteJson(Path.Combine(outDir, "replay-fixtures.json"), BuildReplayFixtures());

            Console.WriteLine($"Fixtures written to {Path.GetFullPath(outDir)}");
            return 0;
        }

        private static object BuildPrngFixtures()
        {
            ulong[] seeds = { 0UL, 1UL, 0xC0FFEEUL, 0xDEADBEEFCAFEUL, ulong.MaxValue, 6355748219UL };
            int[] bounds = { 28, 27, 14, 9, 7, 2, 1, 52 };

            List<object> fixtures = new();
            foreach (ulong seed in seeds)
            {
                SeededRandomSource uintSrc = new(seed);
                List<string> uint64 = new();
                for (int i = 0; i < PrngValuesPerSeed; i++)
                {
                    uint64.Add(uintSrc.NextUInt64().ToString(CultureInfo.InvariantCulture));
                }

                SeededRandomSource intSrc = new(seed);
                List<object> ints = new();
                foreach (int bound in bounds)
                {
                    ints.Add(new { bound, value = intSrc.NextInt(bound) });
                }

                fixtures.Add(new
                {
                    seed = seed.ToString(CultureInfo.InvariantCulture),
                    uint64,
                    ints,
                });
            }

            return new { prng = fixtures };
        }

        private static object BuildReplayFixtures()
        {
            // Fixed-seeded chooser so fixtures are reproducible. This is a test
            // tool, not gameplay, so System.Random is fine here.
            Random chooser = new(20260803);
            List<object> replays = new();

            // Cut-Throat: a spread of normal games across 2/3/4 players and many
            // deals, so domino / block / draw endings all show up.
            foreach (int playerCount in new[] { 2, 3, 4 })
            {
                for (int g = 0; g < 40; g++)
                {
                    ulong seed = unchecked((ulong)(0x100000000UL + (uint)chooser.Next()) * (ulong)(playerCount * 31 + g + 1));
                    replays.Add(PlayGame(seed, playerCount, chooser, forceResignAfter: -1, "cutthroat").Fixture);
                }
            }

            // Cut-Throat forced-resign games (2P and 3+P branches).
            foreach (int playerCount in new[] { 2, 3, 4 })
            {
                for (int g = 0; g < 8; g++)
                {
                    ulong seed = unchecked((ulong)(0x900000000UL + (uint)chooser.Next()) * (ulong)(playerCount * 17 + g + 1));
                    int resignAfter = chooser.Next(1, 8);
                    replays.Add(PlayGame(seed, playerCount, chooser, resignAfter, "cutthroat").Fixture);
                }
            }

            // Jamaican Partner (4 players, 2 teams): normal games — team domino /
            // team block / cross-team draw all show up.
            for (int g = 0; g < 40; g++)
            {
                ulong seed = unchecked((ulong)(0xA00000000UL + (uint)chooser.Next()) * (ulong)(g + 7));
                replays.Add(PlayGame(seed, 4, chooser, forceResignAfter: -1, "partner").Fixture);
            }

            // Jamaican Partner forced-resign games (a resign loses the whole team).
            for (int g = 0; g < 12; g++)
            {
                ulong seed = unchecked((ulong)(0xB00000000UL + (uint)chooser.Next()) * (ulong)(g + 13));
                int resignAfter = chooser.Next(1, 8);
                replays.Add(PlayGame(seed, 4, chooser, resignAfter, "partner").Fixture);
            }

            // KEY fixtures: deterministically search for games that end in a
            // both-ends lock-out (a "key"), so the isKey parity is actually
            // exercised. The search is a fixed enumeration, so the output stays
            // reproducible. Play is biased toward capicúa domino wins to make
            // keys turn up quickly (see PlayGame's keyBias branch).
            replays.AddRange(FindKeyGames("cutthroat", 2, count: 2));
            replays.AddRange(FindKeyGames("cutthroat", 4, count: 1));
            replays.AddRange(FindKeyGames("partner", 4, count: 2));

            return new { replays };
        }

        // Enumerates seeds deterministically, playing key-biased games, and
        // returns the first <paramref name="count"/> that end in a key. Throws if
        // none are found in the search budget (a signal the key rule regressed).
        private static List<object> FindKeyGames(string mode, int playerCount, int count)
        {
            const long searchBudget = 4_000_000;
            List<object> found = new();
            for (long attempt = 0; attempt < searchBudget && found.Count < count; attempt++)
            {
                ulong seed = unchecked(0xE00000000UL + (ulong)attempt * 2654435761UL);
                // Fresh per-attempt chooser keeps each candidate reproducible.
                Random chooser = new(unchecked((int)(attempt * 40503) ^ 0x5F3D));
                (object fixture, bool isKey) = PlayGame(
                    seed, playerCount, chooser, forceResignAfter: -1, mode, keyBias: true);
                if (isKey)
                {
                    found.Add(fixture);
                }
            }

            if (found.Count < count)
            {
                throw new InvalidOperationException(
                    $"Found only {found.Count}/{count} key games for {mode} {playerCount}P " +
                    "within the search budget — the key rule may have regressed.");
            }
            return found;
        }

        private static (object Fixture, bool IsKey) PlayGame(
            ulong seed, int playerCount, Random chooser, int forceResignAfter, string mode,
            bool keyBias = false)
        {
            PlayerId[] players = new PlayerId[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                players[i] = new PlayerId($"p{i}");
            }

            DealConfig config = DealConfig.CutThroatDoubleSix(playerCount);
            Partnership partnership = mode == "partner"
                ? Partnership.AlternatingPairs(players[0], players[1], players[2], players[3])
                : Partnership.CutThroat(players);
            IRuleEngine rules = mode == "partner"
                ? new JamaicanPartnerRules(config.MaxPip)
                : new CutThroatRules(config.MaxPip);
            MatchState state = Dealer.Deal(config, players, partnership, new SeededRandomSource(seed));

            List<object> moves = new();
            int applied = 0;
            while (!state.IsOver)
            {
                Move move;
                if (forceResignAfter >= 0 && applied == forceResignAfter)
                {
                    PlayerId resigner = players[chooser.Next(playerCount)];
                    move = new ResignMove(resigner);
                }
                else
                {
                    IReadOnlyList<Move> legal = rules.GetLegalMoves(state);
                    move = keyBias
                        ? ChooseKeyBiased(state, legal, chooser)
                        : legal[chooser.Next(legal.Count)];
                }

                moves.Add(EncodeMove(move, players));
                state = rules.Apply(state, move);
                applied++;
            }

            MatchOutcome outcome = rules.GetOutcome(state)!;
            object fixture = new
            {
                seed = seed.ToString(CultureInfo.InvariantCulture),
                mode,
                players = PlayerValues(players),
                moves,
                expected = EncodeOutcome(outcome, players),
            };
            return (fixture, outcome.IsKey);
        }

        // A move chooser that steers toward keys: prefer a placement that both
        // empties the mover's hand AND lands on both open ends (a capicúa) —
        // that is exactly the shape a key needs. Falls back to a plain domino
        // win, then to a random legal move. Deterministic given the chooser.
        private static Move ChooseKeyBiased(MatchState state, IReadOnlyList<Move> legal, Random chooser)
        {
            List<Move> capicuaWins = new();
            List<Move> dominoWins = new();
            foreach (Move m in legal)
            {
                if (m is not PlaceMove pm)
                {
                    continue;
                }
                if (state.Hands[pm.Player].Count != 1)
                {
                    continue;
                }
                dominoWins.Add(m);
                if (!state.Chain.IsEmpty
                    && pm.Tile.Matches(state.Chain.LeftEnd)
                    && pm.Tile.Matches(state.Chain.RightEnd))
                {
                    capicuaWins.Add(m);
                }
            }

            if (capicuaWins.Count > 0)
            {
                return capicuaWins[chooser.Next(capicuaWins.Count)];
            }
            if (dominoWins.Count > 0)
            {
                return dominoWins[chooser.Next(dominoWins.Count)];
            }
            return legal[chooser.Next(legal.Count)];
        }

        private static object EncodeMove(Move move, PlayerId[] players)
        {
            int idx = IndexOf(players, move.Player);
            switch (move)
            {
                case PlaceMove pm:
                    return new
                    {
                        playerIndex = idx,
                        kind = "place",
                        low = (int)pm.Tile.A,
                        high = (int)pm.Tile.B,
                        end = pm.End == ChainEnd.Left ? "left" : "right",
                    };
                case PassMove:
                    return new { playerIndex = idx, kind = "pass" };
                case ResignMove:
                    return new { playerIndex = idx, kind = "resign" };
                default:
                    throw new InvalidOperationException($"Unknown move type {move.GetType().Name}");
            }
        }

        private static object EncodeOutcome(MatchOutcome outcome, PlayerId[] players)
        {
            int[] remaining = new int[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                remaining[i] = outcome.RemainingPips[players[i]];
            }

            return new
            {
                reason = outcome.Reason switch
                {
                    MatchEndReason.Domino => "domino",
                    MatchEndReason.Blocked => "blocked",
                    MatchEndReason.Resigned => "resigned",
                    _ => "domino",
                },
                winnerIndex = outcome.WinnerId.HasValue ? IndexOf(players, outcome.WinnerId.Value) : -1,
                winningTeamId = outcome.WinningTeamId?.Value ?? string.Empty,
                winnerScore = outcome.WinnerScore,
                isKey = outcome.IsKey,
                remainingPips = remaining,
            };
        }

        private static string[] PlayerValues(PlayerId[] players)
        {
            string[] values = new string[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                values[i] = players[i].Value;
            }
            return values;
        }

        private static int IndexOf(PlayerId[] players, PlayerId player)
        {
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == player)
                {
                    return i;
                }
            }
            throw new InvalidOperationException($"Player {player} not found.");
        }

        private static void WriteJson(string path, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
    }
}
