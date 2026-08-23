import { onCall } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';

// Server-issued seed (M4.2): clients fetch the deal seed here so they can't
// pick a loaded hand. See ADR 0007.
export { startMatch } from './matchmaking/startMatch';

// Settlement (M4.3): the server replays the round from its issued seed + the
// submitted move log and writes the recomputed result. Replaces the old
// submitMatchResult, which trusted the client's claimed outcome. ADR 0007.
export { submitRoundLog } from './settlement/submitRoundLog';

// Marketing site (caribbeandominos.com) tester signups. Public HTTPS endpoint
// behind the Hosting rewrite /api/tester-signup. See ADR 0009.
export { testerSignup } from './web/testerSignup';

// Coin economy (M6): server-authoritative wallet + series roster. A player reads
// their balance via getWallet (lazily funded on first access); a series is opened
// (openSeries) and each client stakes into it by claiming its own authenticated
// seat (joinSeries) — the roster that closes the result->uid trust gap. See ADR 0016.
export { getWallet } from './wallet/getWallet';
export { openSeries } from './economy/openSeries';
export { joinSeries } from './economy/joinSeries';

// Social (M7): Facebook-friend leaderboard, a single-call profile-card aggregate,
// and the daily-capped +250-coin invite reward. See ADR 0017. (The Facebook login
// / friend-graph / invite-send flow lives client-side and feeds friend uids +
// invite ids into these; it needs the FB SDK + app, which are set up separately.)
export { getLeaderboard } from './social/leaderboard';
export { getProfile } from './social/getProfile';
export { claimInviteReward } from './social/claimInviteReward';

// Social auth (M7 phase 1): after a client links Facebook onto its Firebase
// account, syncFacebookIdentity records the verified fbId -> uid mapping (read
// from the signed token) so friends can be resolved later. See ADR 0019.
export { syncFacebookIdentity } from './social/syncFacebookIdentity';

// Social friends (M7 phase 2): resolveFacebookFriends turns the caller's Facebook
// friend ids into app friends (uid + name + record) via the /facebookIndex map;
// the uids feed getLeaderboard(scope:'friends'). See ADR 0019.
export { resolveFacebookFriends } from './social/resolveFacebookFriends';

// Admin (phase A): syncAdminClaim grants/revokes the caller's own `admin` custom
// claim from the server-side allowlist (verified email only). The claim + an
// allowlist re-check gate every admin action. See ADR 0022.
export { syncAdminClaim } from './admin/syncAdminClaim';

/**
 * Health check callable function — returns server time and a static OK marker.
 * Used by the client to confirm Cloud Functions reachability and clock skew.
 *
 * @returns Object containing `status`, ISO `timestamp`, and `version`.
 */
export const healthCheck = onCall((request) => {
  logger.info('healthCheck invoked', {
    auth: request.auth?.uid ?? 'anonymous',
  });

  return {
    status: 'ok',
    timestamp: new Date().toISOString(),
    version: '0.1.0',
  };
});
