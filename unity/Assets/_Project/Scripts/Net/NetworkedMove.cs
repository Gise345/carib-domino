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
    /// <see cref="Kind"/> selects the move type — 0 = Place, 1 = Pass, 2 = Resign.
    /// For Place: pip fields identify the tile, <see cref="EndSide"/> selects the
    /// chain end (0 = Left, 1 = Right). For Pass and Resign, pip/end fields are
    /// ignored.
    /// </summary>
    public struct NetworkedMove : INetworkStruct
    {
        public const byte KindPlace = 0;
        public const byte KindPass = 1;
        public const byte KindResign = 2;

        public byte PlayerIndex;
        public byte HighPip;
        public byte LowPip;
        public byte EndSide;
        public byte Kind;

        public static NetworkedMove FromPlace(byte playerIndex, Tile tile, ChainEnd end)
        {
            return new NetworkedMove
            {
                PlayerIndex = playerIndex,
                HighPip = tile.B,
                LowPip = tile.A,
                EndSide = (byte)(end == ChainEnd.Left ? 0 : 1),
                Kind = KindPlace,
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
                Kind = KindPass,
            };
        }

        public static NetworkedMove FromResign(byte playerIndex)
        {
            return new NetworkedMove
            {
                PlayerIndex = playerIndex,
                HighPip = 0,
                LowPip = 0,
                EndSide = 0,
                Kind = KindResign,
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
            return Kind switch
            {
                KindPass => new PassMove(p),
                KindResign => new ResignMove(p),
                _ => DecodePlace(p),
            };
        }

        private readonly Move DecodePlace(PlayerId p)
        {
            Tile tile = new(LowPip, HighPip);
            ChainEnd end = EndSide == 0 ? ChainEnd.Left : ChainEnd.Right;
            return new PlaceMove(p, tile, end);
        }
    }
}
