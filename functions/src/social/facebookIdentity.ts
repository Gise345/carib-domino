/**
 * Pure helpers for reading a caller's verified Facebook identity out of their
 * decoded Firebase auth token (M7). Kept side-effect free so the extraction /
 * shaping rules are unit-tested in isolation and reused by the
 * `syncFacebookIdentity` callable. See ADR 0019.
 *
 * The Facebook user id read here is the one Firebase Auth itself verified when
 * the client linked (or signed in) with Facebook — it comes from the signed
 * token's `firebase.identities`, never from a client-supplied payload — which is
 * what makes the fbId -> uid index trustworthy for friend resolution.
 */

/** The subset of a decoded Firebase auth token these helpers read. */
export interface AuthTokenClaims {
  readonly firebase?: {
    readonly identities?: Record<string, unknown>;
  };
  /** Display-name claim, mirrored from the Facebook profile (unknown-typed). */
  readonly name?: unknown;
  /** Profile-picture URL claim (unknown-typed). */
  readonly picture?: unknown;
}

/** Public profile fields a Facebook sign-in carries. */
export interface FacebookProfile {
  readonly displayName?: string;
  readonly photoURL?: string;
}

/**
 * Extracts the verified Facebook user id from a decoded Firebase auth token.
 *
 * @param token - the caller's decoded auth token (`request.auth.token`)
 * @returns the Facebook user id, or null if no Facebook identity is linked
 */
export function facebookIdFromToken(token: AuthTokenClaims | null | undefined): string | null {
  const fb: unknown = token?.firebase?.identities?.['facebook.com'];
  if (!Array.isArray(fb)) {
    return null;
  }
  const first: unknown = fb[0];
  return typeof first === 'string' && first.length > 0 ? first : null;
}

/**
 * Derives the public profile fields (display name, photo) a Facebook sign-in
 * carries, dropping anything absent so a merge write never clobbers an existing
 * value with `undefined`.
 *
 * @param token - the caller's decoded auth token
 * @returns display name and photo URL where present
 */
export function facebookProfileFromToken(
  token: AuthTokenClaims | null | undefined,
): FacebookProfile {
  const profile: { displayName?: string; photoURL?: string } = {};
  const name: unknown = token?.name;
  if (typeof name === 'string' && name.trim().length > 0) {
    profile.displayName = name.trim();
  }
  const picture: unknown = token?.picture;
  if (typeof picture === 'string' && picture.length > 0) {
    profile.photoURL = picture;
  }
  return profile;
}
