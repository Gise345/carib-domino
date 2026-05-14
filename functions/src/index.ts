import { onCall } from 'firebase-functions/v2/https';
import { logger } from 'firebase-functions/v2';

// Settlement pipeline (M2.3 stub; full replay validation in M4).
export { submitMatchResult } from './settlement/submitMatchResult';

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
