#nullable enable
using System;
using System.Collections.Generic;

namespace Pose.Core
{
    /// <summary>
    /// Jamaican Partner dominoes — exactly 4 players in 2 teams of 2 (partners
    /// across the table, see <see cref="Partnership.AlternatingPairs"/>). Standard
    /// double-six set, 7 tiles each, no boneyard. Mid-game placement and turn
    /// rotation are identical to <see cref="CutThroatRules"/>; only end-of-round
    /// scoring differs (see ADR 0003 and the M1 step 3 handoff for the canonical
    /// rule text):
    /// <list type="bullet">
    ///   <item>Domino end: dominoing player's <em>team</em> wins. Score equals the
    ///         sum of the <em>opposing</em> team's pip totals — teammate's pips
    ///         don't count for or against.</item>
    ///   <item>Block end: the player with the single lowest pip count picks the
    ///         winning team. If teammates tie at the lowest, that team still wins.
    ///         If the tied lowest spans both teams (or all four are tied) the
    ///         round is a draw.</item>
    /// </list>
    /// Multi-round mechanics ("Six-Love", tied-block replay for 2 points) live at
    /// a higher tournament/scoring layer and are out of scope here.
    /// </summary>
    public sealed class JamaicanPartnerRules : IRuleEngine
    {
        // Move enumeration, legality, and the place/pass mechanics are identical
        // to Cut-Throat — both Jamaican variants share the same mid-game rules.
        // Composing rather than inheriting keeps the relationship explicit and
        // means a fix in CutThroatRules.Apply is automatically picked up here.
        private readonly CutThroatRules _moveLogic;

        public JamaicanPartnerRules(byte maxPip = 6)
        {
            _moveLogic = new CutThroatRules(maxPip);
        }

        public IReadOnlyList<Move> GetLegalMoves(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            EnsurePartnerSetup(state);
            return _moveLogic.GetLegalMoves(state);
        }

        public bool IsLegal(MatchState state, Move move)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (move == null)
            {
                throw new ArgumentNullException(nameof(move));
            }

            EnsurePartnerSetup(state);
            return _moveLogic.IsLegal(state, move);
        }

        public MatchState Apply(MatchState state, Move move)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (move == null)
            {
                throw new ArgumentNullException(nameof(move));
            }

            EnsurePartnerSetup(state);
            return _moveLogic.Apply(state, move);
        }

        public MatchOutcome? GetOutcome(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.IsOver)
            {
                return null;
            }

            EnsurePartnerSetup(state);

            Dictionary<PlayerId, int> remaining = new(state.Players.Count);
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                remaining[p] = state.Hands[p].PipTotal;
            }

            // Domino end: someone has emptied their hand. Their team wins; the
            // score is the opposing team's combined pips (teammate excluded).
            PlayerId? dominoer = null;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (state.Hands[p].Count == 0)
                {
                    dominoer = p;
                    break;
                }
            }

            if (dominoer.HasValue)
            {
                TeamId winningTeam = state.Partnership.GetTeamOf(dominoer.Value);
                int score = SumPipsOfOpposingTeam(state, winningTeam);
                return new MatchOutcome(
                    MatchEndReason.Domino,
                    dominoer,
                    winningTeam,
                    score,
                    remaining);
            }

            // Block end (corrected partner scoring): the player with the single
            // lowest pip count selects the winning team. We walk the players,
            // tracking the lowest pip count and which team owns it. If a second
            // team also has a player at the lowest, the tied-across-teams rule
            // applies and the round is a draw.
            int lowest = int.MaxValue;
            for (int i = 0; i < state.Players.Count; i++)
            {
                int pips = state.Hands[state.Players[i]].PipTotal;
                if (pips < lowest)
                {
                    lowest = pips;
                }
            }

            TeamId? winningTeamId = null;
            bool tiedAcrossTeams = false;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (state.Hands[p].PipTotal != lowest)
                {
                    continue;
                }

                TeamId thisTeam = state.Partnership.GetTeamOf(p);
                if (winningTeamId == null)
                {
                    winningTeamId = thisTeam;
                }
                else if (winningTeamId.Value != thisTeam)
                {
                    tiedAcrossTeams = true;
                    break;
                }
            }

            if (tiedAcrossTeams)
            {
                return new MatchOutcome(
                    MatchEndReason.Blocked,
                    winnerId: null,
                    winningTeamId: null,
                    winnerScore: 0,
                    remaining);
            }

            // One team owns the lowest pip count. Pick the lowest-indexed player
            // on that team at the min pip count as the WinnerId — when teammates
            // tie within the winning team, this gives us a deterministic
            // representative (required for replay determinism in M4).
            TeamId winner = winningTeamId!.Value;
            PlayerId? winnerId = null;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (state.Hands[p].PipTotal == lowest
                    && state.Partnership.GetTeamOf(p) == winner)
                {
                    winnerId = p;
                    break;
                }
            }

            int blockScore = SumPipsOfOpposingTeam(state, winner);
            return new MatchOutcome(
                MatchEndReason.Blocked,
                winnerId,
                winner,
                blockScore,
                remaining);
        }

        // The shape check for Jamaican Partner: exactly 4 players partitioned
        // into 2 teams of 2. We don't check positional alignment (0+2 vs 1+3)
        // because the rule engine cares about the partnership shape, not which
        // factory built it — a hand-constructed Partnership with the right shape
        // is just as valid as one from Partnership.AlternatingPairs.
        private static void EnsurePartnerSetup(MatchState state)
        {
            if (state.Players.Count != 4)
            {
                throw new InvalidOperationException(
                    $"Jamaican Partner requires exactly 4 players; got {state.Players.Count}.");
            }

            if (state.Partnership.Teams.Count != 2)
            {
                throw new InvalidOperationException(
                    $"Jamaican Partner requires exactly 2 teams; got {state.Partnership.Teams.Count}.");
            }

            for (int i = 0; i < state.Partnership.Teams.Count; i++)
            {
                Team t = state.Partnership.Teams[i];
                if (t.Members.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"Jamaican Partner requires 2 players per team; team {t.Id} has {t.Members.Count}.");
                }
            }
        }

        private static int SumPipsOfOpposingTeam(MatchState state, TeamId winningTeam)
        {
            int sum = 0;
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                if (state.Partnership.GetTeamOf(p) != winningTeam)
                {
                    sum += state.Hands[p].PipTotal;
                }
            }
            return sum;
        }
    }
}
