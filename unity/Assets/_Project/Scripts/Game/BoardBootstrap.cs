#nullable enable
using System.Collections.Generic;
using Pose.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Pose.Game
{
    /// <summary>
    /// M1 step 4 scene controller: deals a fixed-seed 4-player Cut-Throat round
    /// and renders the live state — chain at the top, four hands stacked below,
    /// status footer at the bottom — on a felt-green tabletop. Drives hot-seat
    /// input: clicking a playable tile in the current player's hand applies the
    /// move; the Pass button skips the turn when no tile matches; the status
    /// label switches to a round-over message once the engine reports
    /// <see cref="MatchOutcome"/>.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoardBootstrap : MonoBehaviour
    {
        // Fixed seed so visual output is comparable across runs. A real game
        // gets its seed from a Cloud Function — see docs/ARCHITECTURE.md §5.
        private const ulong SpikeSeed = 0xC0FFEEUL;

        // Felt green — domino-club tabletop palette.
        private static readonly Color FeltColor = new(0.05f, 0.30f, 0.18f, 1f);

        private static readonly PlayerId[] Players =
        {
            new("alice"),
            new("bob"),
            new("cara"),
            new("dan"),
        };

        private readonly CutThroatRules _rules = new();
        private MatchState? _state;
        private ChainView? _chainView;
        private readonly List<HandView> _handViews = new();
        private GameStatusView? _statusView;

        private void Start()
        {
            ConfigureRootLayout();

            _state = Dealer.Deal(
                DealConfig.CutThroatDoubleSix(4),
                Players,
                Partnership.CutThroat(Players),
                new SeededRandomSource(SpikeSeed));

            // Build the views once; subsequent moves re-populate them in place.
            _chainView = CreateChainView();

            for (int i = 0; i < Players.Length; i++)
            {
                PlayerId p = Players[i];
                HandView hv = CreateHandView(p.Value);
                hv.TileClicked += tile => OnTileClicked(p, tile);
                _handViews.Add(hv);
            }

            _statusView = CreateStatusView();
            _statusView.PassClicked += OnPassClicked;

            Render();
        }

        private void OnTileClicked(PlayerId player, Tile tile)
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }

            // Defensive: only the current player may play. The HandView only
            // marks the current player's playable tiles as interactable, so
            // this check is belt-and-suspenders.
            if (player != _state.CurrentPlayer)
            {
                return;
            }

            // Find the legal placement for this tile. If the tile matches both
            // chain ends, CutThroatRules emits two PlaceMoves (LEFT then RIGHT);
            // we auto-pick the first (LEFT). End-choice UI is a polish slice.
            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PlaceMove pm && pm.Tile == tile)
                {
                    _state = _rules.Apply(_state, pm);
                    Render();
                    return;
                }
            }
        }

        private void OnPassClicked()
        {
            if (_state == null || _state.IsOver)
            {
                return;
            }

            IReadOnlyList<Move> legal = _rules.GetLegalMoves(_state);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i] is PassMove pass)
                {
                    _state = _rules.Apply(_state, pass);
                    Render();
                    return;
                }
            }
        }

        private void Render()
        {
            MatchState state = _state!;
            _chainView!.Setup(state.Chain);

            // Compute which tiles in the current player's hand are playable —
            // the HandView shows these bright + interactable, others dimmed.
            HashSet<Tile> playableTiles = new();
            bool passIsLegal = false;
            if (!state.IsOver)
            {
                IReadOnlyList<Move> legal = _rules.GetLegalMoves(state);
                for (int i = 0; i < legal.Count; i++)
                {
                    switch (legal[i])
                    {
                        case PlaceMove pm:
                            playableTiles.Add(pm.Tile);
                            break;
                        case PassMove:
                            passIsLegal = true;
                            break;
                    }
                }
            }

            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerId p = state.Players[i];
                bool isCurrent = !state.IsOver && p == state.CurrentPlayer;
                System.Func<Tile, bool>? predicate = isCurrent
                    ? new System.Func<Tile, bool>(tile => playableTiles.Contains(tile))
                    : null;
                _handViews[i].Setup(p.Value, isCurrent, state.Hands[p], predicate);
            }

            _statusView!.Setup(state, _rules.GetOutcome(state), passIsLegal);
        }

        private void ConfigureRootLayout()
        {
            RectTransform rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image background = gameObject.AddComponent<Image>();
            background.color = FeltColor;

            VerticalLayoutGroup vlg = gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.spacing = 12f;
            vlg.padding = new RectOffset(24, 24, 24, 24);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
        }

        private ChainView CreateChainView()
        {
            GameObject go = new("ChainView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<ChainView>();
        }

        private HandView CreateHandView(string playerName)
        {
            GameObject go = new($"Hand_{playerName}", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<HandView>();
        }

        private GameStatusView CreateStatusView()
        {
            GameObject go = new("StatusView", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<GameStatusView>();
        }
    }
}
