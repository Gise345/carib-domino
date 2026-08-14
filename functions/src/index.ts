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
