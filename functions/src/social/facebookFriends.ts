/**
 * Pure resolver for turning a player's Facebook friend ids into the app uids that
 * own them (M7). Kept side-effect free — the caller supplies the fbId -> uid
 * lookups already read from `/facebookIndex` — so the dedup / self-exclusion
 * rules are unit-tested without Firestore. See ADR 0019.
 */

/**
 * Maps Facebook friend ids to unique app uids, dropping ids with no app account,
 * the caller themselves, and duplicates. Input order is preserved.
 *
 * @param friendFacebookIds - the caller's Facebook friend ids (from the graph API)
 * @param uidByFacebookId - fbId -> uid lookups read from `/facebookIndex`
 * @param selfUid - the caller's own uid, always excluded
 * @returns the resolved friend uids, unique and in first-seen order
 */
export function resolveFriendUids(
  friendFacebookIds: readonly string[],
  uidByFacebookId: ReadonlyMap<string, string>,
  selfUid: string,
): string[] {
  const seen = new Set<string>();
  const uids: string[] = [];
  for (const fbId of friendFacebookIds) {
    const uid = uidByFacebookId.get(fbId);
    if (uid === undefined || uid === selfUid || seen.has(uid)) {
      continue;
    }
    seen.add(uid);
    uids.push(uid);
  }
  return uids;
}
