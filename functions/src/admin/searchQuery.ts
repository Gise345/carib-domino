/**
 * Pure classification for the admin user-search box (ADR 0022, phase C): decide
 * whether a query should be looked up as an email (via Auth) or a display-name /
 * uid (via Firestore). Kept side-effect free so it's unit-tested in isolation.
 */

/**
 * True if the query looks like an email address (has an `@` with a dotted domain
 * after it) — routed to an exact Auth email lookup rather than a name search.
 *
 * @param query - the raw search input
 * @returns whether to treat the query as an email
 */
export function looksLikeEmail(query: string): boolean {
  const q = query.trim();
  const at = q.indexOf('@');
  if (at <= 0) {
    return false;
  }
  const dot = q.indexOf('.', at + 1);
  return dot > at + 1 && dot < q.length - 1;
}
