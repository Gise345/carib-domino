#nullable enable
using Fusion;
using Pose.Core;

namespace Pose.Net
{
    /// <summary>
    /// Wire-format for a single move replicated through Fusion's networked move log
    /// on <see cref="NetworkedMatch"/>. 5 bytes per move; the full Pose.Core
    /// <see cref="Move"/> hierarchy is reconstructed locally on each client via
    /// <see cref="ToCoreMove"/> using the player order from the local MatchState.
    ///
    /// For <see cref="PassMove"/>: <see cref="IsPass"/> = 1; pip/end fields are
    /// ignored. For <see cref="PlaceMove"/>: <see cref="IsPass"/> = 0, the tile
    /// is identified by (HighPip, LowPip), and <see cref="EndSide"/> selects
    /// which end of the chain (0 = Left, 1 = Right).
    /// </summary>
    public struct NetworkedMove : INetworkStruct
    {
        public byte PlayerIndex;
        public byte HighPip;
        public byte LowPip;
        public byte EndSide;
        public byte IsPass;

        public static NetworkedMove FromPlace(byte playerIndex, Tile tile, ChainEnd end)
        {
            return new NetworkedMove
            {
                PlayerIndex = playerIndex,
                HighPip = tile.B,
                LowPip = tile.A,
                EndSide = (byte)(end == ChainEnd.Left ? 0 : 1),
                IsPass = 0,
            };
        }

        public static NetworkedMove FromPass(byte playerIndex)
        {
            return new NetworkedMove
            {
                PlayerIndex = playerIndex,
                HighPip = 0,
                LowPip = 0,
                EndSide = 0,
                IsPass = 1,
            };
        }

        /// <summary>
        /// Reconstructs the Pose.Core <see cref="Move"/> using the player list
        /// from the live <see cref="MatchState"/>. <paramref name="players"/> must
        /// be the same ordered list that produced this index (i.e. the dealt
        /// state's <see cref="MatchState.Players"/>).
        /// </summary>
        public readonly Move ToCoreMove(System.Collections.Generic.IReadOnlyList<PlayerId> players)
        {
            PlayerId p = players[PlayerIndex];
            if (IsPass == 1)
            {
                return new PassMove(p);
            }
            Tile tile = new(LowPip, HighPip);
            ChainEnd end = EndSide == 0 ? ChainEnd.Left : ChainEnd.Right;
            return new PlaceMove(p, tile, end);
        }
    }
}
