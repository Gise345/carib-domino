#nullable enable
using System;
using Fusion;
using Pose.Core;
using UnityEngine;
// Disambiguate from Fusion.GameMode — here GameMode always means the gameplay
// ruleset (Pose.Core.GameMode).
using GameMode = Pose.Core.GameMode;

namespace Pose.Net
{
    /// <summary>
    /// Fusion <see cref="NetworkBehaviour"/> that synchronises the minimal
    /// inputs needed to derive a 2-, 3- or 4-player Cut-Throat round: the deal
    /// seed, each player's display name and seat, and the append-only move log.
    /// Both clients run the deterministic Pose.Core engine against these inputs
    /// and reconstruct the same <c>MatchState</c> locally — state is never
    /// pushed over the wire.
    ///
    /// Seating and the deal handshake (M3.7):
    /// - The host (shared-mode master client) spawns this object, seats itself
    ///   at index 0 (writing <see cref="PlayerIds"/>[0], its
    ///   <see cref="SeatPlayerRefs"/>[0], <see cref="Seed"/> and the target
    ///   <see cref="PlayerCount"/>), and sets <see cref="RegisteredCount"/> = 1.
    /// - Each joiner calls <see cref="RPC_RegisterPlayer"/>; the host appends
    ///   them to the next seat, keyed by the sender's <see cref="PlayerRef"/> so
    ///   every client can find its own seat regardless of display-name clashes.
    /// - When every seat is filled (<see cref="RegisteredCount"/> ==
    ///   <see cref="PlayerCount"/>), or when the <see cref="AutoStartTimer"/>
    ///   deadline elapses (empty seats are filled with bots, M3.9b), the authority
    ///   sets <see cref="RoundNumber"/> = 1 and flips <see cref="DealReady"/>.
    ///
    /// Rematch: both/all seated players must opt in. Each vote sets a bit in
    /// <see cref="RematchVoteMask"/>; when every seat's bit is set the host
    /// re-seeds, truncates the log and advances <see cref="RoundNumber"/>.
    ///
    /// Edge detection over the replicated fields (deal landed / round advanced /
    /// new move range) is delegated to the Unity-free
    /// <see cref="MatchSignalTracker"/>. See
    /// <c>docs/DECISIONS/0006-n-player-online.md</c>.
    /// </summary>
    public sealed class NetworkedMatch : NetworkBehaviour
    {
        /// <summary>Maximum seats. Cut-Throat ships 2–4 players.</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// Maximum moves that can fit in the per-round networked log. A full
        /// double-six deal is 28 placements; the rest are passes and an optional
        /// resign. 128 leaves comfortable headroom even for 4-hand rounds with
        /// many passes, and stays a power of two for the NetworkArray layout.
        /// The log is per-round — a rematch truncates it — so this is not
        /// cumulative across rounds.
        /// </summary>
        public const int MaxMoves = 128;

        public static event Action<NetworkedMatch>? AnySpawned;

        /// <summary>Fires once, when the initial deal becomes available on this client.</summary>
        public event Action? DealReadyChanged;

        /// <summary>Fires when <see cref="RoundNumber"/> advances (a rematch was agreed).</summary>
        public event Action? RoundStartedChanged;

        /// <summary>Fires whenever <see cref="RematchVoteMask"/> changes.</summary>
        public event Action? RematchVotesChanged;

        /// <summary>Fires whenever <see cref="RegisteredCount"/> changes (players joining while waiting).</summary>
        public event Action? RegisteredCountChanged;

        /// <summary>
        /// Fires whenever the move log advances, with the new [from, to) index
        /// range for the listener to pull and apply. Indices are per-round.
        /// </summary>
        public event Action<int, int>? MoveAppliedChanged;

        [Networked] public ulong Seed { get; set; }

        /// <summary>
        /// The server-issued match id this round's <see cref="Seed"/> was recorded
        /// under (M4.2). Replicated so any client can reference it when submitting
        /// the round log for settlement (M4.3). Empty when the seed was generated
        /// locally as a fallback (server unreachable) — such rounds can't settle.
        /// </summary>
        [Networked] public NetworkString<_32> MatchId { get; set; }

        /// <summary>
        /// The ruleset this match is played under (M3.8). Set by the host at spawn
        /// and replicated so every client deals with the matching rules +
        /// partnership. The server independently records it at startMatch time, so
        /// settlement never trusts this value (ADR 0009).
        /// </summary>
        [Networked] public GameMode GameMode { get; set; }

        [Networked, Capacity(MaxPlayers)] public NetworkArray<NetworkString<_32>> PlayerIds => default;
        [Networked, Capacity(MaxPlayers)] public NetworkArray<int> SeatPlayerRefs => default;
        [Networked] public int PlayerCount { get; set; }
        [Networked] public int RegisteredCount { get; set; }
        [Networked] public bool DealReady { get; set; }
        [Networked, Capacity(MaxMoves)] public NetworkArray<NetworkedMove> Moves => default;
        [Networked] public int MoveCount { get; set; }

        /// <summary>
        /// 0 before the first deal, 1 for the first round, incrementing once per
        /// agreed rematch. Paired with a fresh <see cref="Seed"/> and a
        /// <see cref="MoveCount"/> of 0 in the same replicated snapshot.
        /// </summary>
        [Networked] public int RoundNumber { get; set; }

        /// <summary>Bit <c>i</c> set means the player seated at index <c>i</c> wants a rematch.</summary>
        [Networked] public int RematchVoteMask { get; set; }

        /// <summary>
        /// Bit <c>i</c> set means seat <c>i</c> is a bot — either filled at the
        /// auto-start deadline or converted from a player who left mid-round
        /// (M3.9b). The table's authority drives bot turns; every client renders
        /// bot seats as such. Bots have no Firebase uid, so settlement skips them.
        /// </summary>
        [Networked] public int BotSeatMask { get; set; }

        /// <summary>
        /// Fires 60s after the table opens; when it expires with the table not yet
        /// full, the authority fills the empty seats with bots and deals — so play
        /// never waits on a seat that never fills, and no player has to press
        /// "start" (there is no host role). Migration-safe: it is networked, so a
        /// promoted authority honours the same deadline (ADR 0011).
        /// </summary>
        [Networked] public TickTimer AutoStartTimer { get; set; }

        // ---- Match series (M5, Cut-Throat) --------------------------------

        /// <summary>The series format — meaningful only for Cut-Throat games.</summary>
        [Networked] public MatchFormat SeriesFormat { get; set; }

        /// <summary>Each seat's cumulative series points (1000 per round won).</summary>
        [Networked, Capacity(MaxPlayers)] public NetworkArray<int> SeriesPoints => default;

        /// <summary>True once the series is decided (Classic target hit / Quick finished).</summary>
        [Networked] public bool MatchOver { get; set; }

        /// <summary>The winning seat once <see cref="MatchOver"/>, or -1 for none/tie.</summary>
        [Networked] public int WinnerSeat { get; set; }

        /// <summary>Bumped by the authority on every series update, so clients refresh the scoreboard.</summary>
        [Networked] public int SeriesVersion { get; set; }

        /// <summary>Fires when the series scores advance (for the scoreboard).</summary>
        public event Action? SeriesChanged;

        /// <summary>Fires when the match ends (for the match-over screen).</summary>
        public event Action? MatchOverChanged;

        /// <summary>
        /// Set by the host's <see cref="OnlineMatchController"/> so
        /// <see cref="RPC_SubmitMove"/> can validate against the current
        /// authoritative state before appending. Kept as a callback rather than
        /// a direct Pose.Core dependency so this file doesn't drag in the engine.
        /// </summary>
        public Func<NetworkedMove, bool>? MoveValidator { get; set; }

        private readonly MatchSignalTracker _signals = new();
        private int _lastRematchVoteMask;
        private int _lastRegisteredCount;
        private int _lastSeriesVersion;
        private bool _lastMatchOver;

        // Host-local: Firebase uid per seat, self-reported by each player at
        // registration (host at seat 0; joiners via RPC_RegisterPlayer). Used
        // only by the host to attribute settlement results (M4.3). NOT networked
        // — only the host submits. A client reports its OWN seat's uid; a
        // modified client could report a false uid, a gap a server-side roster
        // closes (ADR 0007).
        private readonly string[] _seatUids = new string[MaxPlayers];

        /// <summary>Host-side: records the Firebase uid a seat reported. No-op off the host.</summary>
        public void RecordSeatUid(int seat, string uid)
        {
            if (Object.HasStateAuthority && seat >= 0 && seat < MaxPlayers)
            {
                _seatUids[seat] = uid ?? string.Empty;
            }
        }

        /// <summary>
        /// Host-side: the reported Firebase uids for the current <see cref="PlayerCount"/>
        /// seats (empty string where unknown). Used to attribute settlement results.
        /// </summary>
        public string[] HostSeatUids()
        {
            int n = Mathf.Clamp(PlayerCount, 0, MaxPlayers);
            string[] result = new string[n];
            for (int i = 0; i < n; i++)
            {
                result[i] = _seatUids[i] ?? string.Empty;
            }
            return result;
        }

        /// <summary>
        /// True once every seated player has voted for a rematch. Bot seats count
        /// as always-in (they can't vote), so a table with bots can still rematch
        /// on the humans' votes alone.
        /// </summary>
        public bool AllRematchVotesIn =>
            PlayerCount > 0
            && ((RematchVoteMask | BotSeatMask) & SeatMask(PlayerCount)) == SeatMask(PlayerCount);

        private static int SeatMask(int count) => (1 << count) - 1;

        /// <summary>True if seat <paramref name="seat"/> is a bot.</summary>
        public bool IsBotSeat(int seat) =>
            seat >= 0 && seat < MaxPlayers && (BotSeatMask & (1 << seat)) != 0;

        public override void Spawned()
        {
            base.Spawned();
            Debug.Log(
                $"[NetworkedMatch] Spawned (authority={Object.HasStateAuthority}, " +
                $"seed={Seed}, count={PlayerCount}, registered={RegisteredCount})");
            AnySpawned?.Invoke(this);
        }

        public override void Render()
        {
            MatchSignal signal = _signals.Observe(DealReady, RoundNumber, MoveCount);
            if (signal.DealStarted)
            {
                DealReadyChanged?.Invoke();
            }
            if (signal.RoundStarted)
            {
                RoundStartedChanged?.Invoke();
            }
            if (signal.HasMoves)
            {
                MoveAppliedChanged?.Invoke(signal.MovesFrom, signal.MovesTo);
            }

            if (RegisteredCount != _lastRegisteredCount)
            {
                _lastRegisteredCount = RegisteredCount;
                RegisteredCountChanged?.Invoke();
            }
            if (RematchVoteMask != _lastRematchVoteMask)
            {
                _lastRematchVoteMask = RematchVoteMask;
                RematchVotesChanged?.Invoke();
            }
            if (SeriesVersion != _lastSeriesVersion)
            {
                _lastSeriesVersion = SeriesVersion;
                SeriesChanged?.Invoke();
            }
            if (MatchOver != _lastMatchOver)
            {
                _lastMatchOver = MatchOver;
                MatchOverChanged?.Invoke();
            }
        }

        /// <summary>
        /// Called by a joining client to claim the next free seat. Source = All
        /// so a non-authority joiner can invoke it; target = StateAuthority so
        /// only the host writes. The sender's <see cref="PlayerRef"/> is recorded
        /// so every client can later identify its own seat.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RegisterPlayer(string playerId, string uid, RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (DealReady)
            {
                Debug.LogWarning("[NetworkedMatch] RPC_RegisterPlayer ignored — deal already ready.");
                return;
            }
            int seat = RegisteredCount;
            if (seat >= PlayerCount || seat >= MaxPlayers)
            {
                Debug.LogWarning($"[NetworkedMatch] RPC_RegisterPlayer dropped — room full at {seat}.");
                return;
            }
            // Ignore a duplicate registration from a player who already has a seat
            // (e.g. a retried RPC), so a client can't consume two seats.
            for (int i = 0; i < seat; i++)
            {
                if (SeatPlayerRefs.Get(i) == info.Source.PlayerId)
                {
                    Debug.LogWarning($"[NetworkedMatch] RPC_RegisterPlayer ignored — player already seated at {i}.");
                    return;
                }
            }

            PlayerIds.Set(seat, playerId);
            SeatPlayerRefs.Set(seat, info.Source.PlayerId);
            RecordSeatUid(seat, uid);
            RegisteredCount = seat + 1;
            Debug.Log($"[NetworkedMatch] Seated \"{playerId}\" at index {seat} ({RegisteredCount}/{PlayerCount}).");

            if (RegisteredCount >= PlayerCount)
            {
                StartFirstRound();
            }
        }

        /// <summary>
        /// Authority-only. Arms the auto-start deadline (see
        /// <see cref="AutoStartTimer"/>). Called once when the table opens.
        /// </summary>
        public void ArmAutoStart(float seconds)
        {
            if (Object.HasStateAuthority)
            {
                AutoStartTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            }
        }

        /// <summary>
        /// Authority-side: at the auto-start deadline, fill every seat not held by
        /// a present human with a bot and deal. Replaces the old host-driven
        /// "start with current players" prompt — there is no host role, and no one
        /// taps to start (ADR 0011).
        /// </summary>
        private void AutoStartWithBots()
        {
            if (!Object.HasStateAuthority || DealReady)
            {
                return;
            }

            for (int seat = 0; seat < PlayerCount; seat++)
            {
                if (!SeatHeldByPresentHuman(seat))
                {
                    FillSeatAsBot(seat);
                }
            }
            RegisteredCount = PlayerCount;
            StartFirstRound();
        }

        private bool SeatHeldByPresentHuman(int seat)
        {
            if (seat >= RegisteredCount || IsBotSeat(seat))
            {
                return false;
            }
            int seatRef = SeatPlayerRefs.Get(seat);
            foreach (PlayerRef p in Runner.ActivePlayers)
            {
                if (p.PlayerId == seatRef)
                {
                    return true;
                }
            }
            return false;
        }

        // Authority-only. Turns an empty / vacated-before-deal seat into a fresh
        // bot: names it, marks the seat a bot, and clears its ref and uid.
        private void FillSeatAsBot(int seat)
        {
            PlayerIds.Set(seat, $"Bot {seat + 1}");
            SeatPlayerRefs.Set(seat, -1);
            BotSeatMask |= 1 << seat;
            _seatUids[seat] = string.Empty;
        }

        /// <summary>
        /// Authority-only. Converts a seat whose player left MID-round into a bot,
        /// so the table plays on. Keeps the seat's existing player id (its dealt
        /// hand is keyed by it); only marks it a bot and drops its uid so
        /// settlement won't attribute the round to the departed player.
        /// </summary>
        public void MakeSeatBot(int seat)
        {
            if (!Object.HasStateAuthority || seat < 0 || seat >= PlayerCount)
            {
                return;
            }
            BotSeatMask |= 1 << seat;
            _seatUids[seat] = string.Empty;
            Debug.Log($"[NetworkedMatch] Seat {seat} converted to a bot (player left).");
        }

        public override void FixedUpdateNetwork()
        {
            base.FixedUpdateNetwork();
            // Only the authority drives the deadline; it is networked, so a
            // migrated authority picks up the same timer without resetting it.
            if (Object.HasStateAuthority && !DealReady && AutoStartTimer.Expired(Runner))
            {
                AutoStartTimer = TickTimer.None;
                AutoStartWithBots();
            }
        }

        private void StartFirstRound()
        {
            RoundNumber = 1;
            DealReady = true;
            Debug.Log($"[NetworkedMatch] Deal ready — {PlayerCount} players.");
        }

        /// <summary>
        /// Either client submits its intended move. Host validates via
        /// <see cref="MoveValidator"/> and, if legal, appends to the log.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SubmitMove(NetworkedMove move, RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (MoveCount >= MaxMoves)
            {
                Debug.LogWarning($"[NetworkedMatch] RPC_SubmitMove dropped — log full at {MoveCount}.");
                return;
            }
            if (MoveValidator == null)
            {
                Debug.LogWarning(
                    "[NetworkedMatch] RPC_SubmitMove dropped — host has no MoveValidator installed yet.");
                return;
            }
            if (!MoveValidator(move))
            {
                Debug.LogWarning(
                    $"[NetworkedMatch] RPC_SubmitMove rejected as illegal: player={move.PlayerIndex} " +
                    $"kind={move.Kind} tile=[{move.LowPip}|{move.HighPip}] end={move.EndSide}");
                return;
            }
            Moves.Set(MoveCount, move);
            MoveCount++;
        }

        /// <summary>
        /// A player opts into a rematch of the finished round. Every seated
        /// player must vote before the host re-deals, so one player cannot
        /// restart the match under the others. Votes are idempotent.
        /// </summary>
        /// <param name="playerIndex">The seat index of the voting player.</param>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestRematch(byte playerIndex, RpcInfo info = default)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            if (!DealReady)
            {
                Debug.LogWarning("[NetworkedMatch] RPC_RequestRematch dropped — no deal yet.");
                return;
            }
            if (playerIndex >= PlayerCount)
            {
                Debug.LogWarning($"[NetworkedMatch] RPC_RequestRematch dropped — bad seat {playerIndex}.");
                return;
            }

            RematchVoteMask |= 1 << playerIndex;
            Debug.Log($"[NetworkedMatch] Rematch vote from seat {playerIndex} (mask={RematchVoteMask}).");

            // The host controller watches the votes; when they are all in it
            // fetches a fresh server seed and calls AdvanceRound. We do NOT
            // advance here because the seed fetch is asynchronous (M4.2).
        }

        /// <summary>
        /// Host-only. Publishes a fresh (server-issued) seed and match id,
        /// truncates the move log, clears the rematch votes and advances the
        /// round — all in one tick so every client re-deals against a consistent
        /// snapshot. Called by the controller once every seat has voted for a
        /// rematch and a new seed has been fetched.
        /// </summary>
        public void AdvanceRound(ulong seed, string matchId)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            Seed = seed;
            MatchId = matchId;
            MoveCount = 0;
            RematchVoteMask = 0;
            RoundNumber++;
            Debug.Log($"[NetworkedMatch] Advancing — round {RoundNumber}, seed={seed}, match={matchId}.");
        }

        /// <summary>
        /// Authority-only. Publishes the series scores after a round, and whether
        /// the match is now over (M5). Every client reads these to draw the
        /// scoreboard and the match-over screen. Bumps <see cref="SeriesVersion"/>
        /// so clients notice even a same-shaped update.
        /// </summary>
        public void RecordSeriesResult(int[] pointsBySeat, bool over, int winnerSeat)
        {
            if (!Object.HasStateAuthority)
            {
                return;
            }
            int n = Mathf.Min(pointsBySeat.Length, MaxPlayers);
            for (int i = 0; i < n; i++)
            {
                SeriesPoints.Set(i, pointsBySeat[i]);
            }
            MatchOver = over;
            WinnerSeat = winnerSeat;
            SeriesVersion++;
        }
    }
}
