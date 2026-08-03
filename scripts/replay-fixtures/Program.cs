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

            // A spread of normal games across 2/3/4 players and many deals, so
            // domino / block / draw endings all show up.
            foreach (int playerCount in new[] { 2, 3, 4 })
            {
                for (int g = 0; g < 40; g++)
                {
                    ulong seed = unchecked((ulong)(0x100000000UL + (uint)chooser.Next()) * (ulong)(playerCount * 31 + g + 1));
                    replays.Add(PlayGame(seed, playerCount, chooser, forceResignAfter: -1));
                }
            }

            // A batch of forced-resign games (2P and 3+P branches).
            foreach (int playerCount in new[] { 2, 3, 4 })
            {
                for (int g = 0; g < 8; g++)
                {
                    ulong seed = unchecked((ulong)(0x900000000UL + (uint)chooser.Next()) * (ulong)(playerCount * 17 + g + 1));
                    int resignAfter = chooser.Next(1, 8);
                    replays.Add(PlayGame(seed, playerCount, chooser, resignAfter));
                }
            }

            return new { replays };
        }

        private static object PlayGame(ulong seed, int playerCount, Random chooser, int forceResignAfter)
        {
            PlayerId[] players = new PlayerId[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                players[i] = new PlayerId($"p{i}");
            }

            DealConfig config = DealConfig.CutThroatDoubleSix(playerCount);
            Partnership partnership = Partnership.CutThroat(players);
            MatchState state = Dealer.Deal(config, players, partnership, new SeededRandomSource(seed));
            CutThroatRules rules = new(config.MaxPip);

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
                    move = legal[chooser.Next(legal.Count)];
                }

                moves.Add(EncodeMove(move, players));
                state = rules.Apply(state, move);
                applied++;
            }

            MatchOutcome outcome = rules.GetOutcome(state)!;
            return new
            {
                seed = seed.ToString(CultureInfo.InvariantCulture),
                players = PlayerValues(players),
                moves,
                expected = EncodeOutcome(outcome, players),
            };
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
