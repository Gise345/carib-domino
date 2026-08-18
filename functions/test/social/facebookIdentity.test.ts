import { describe, expect, it } from 'vitest';
import {
  AuthTokenClaims,
  facebookIdFromToken,
  facebookProfileFromToken,
} from '../../src/social/facebookIdentity';

/** A token as Firebase issues it after a Facebook link/sign-in. */
function tokenWith(overrides: Partial<AuthTokenClaims> = {}): AuthTokenClaims {
  return {
    firebase: { identities: { 'facebook.com': ['100000000000001'] } },
    name: 'Giselle Johnson',
    picture: 'https://graph.facebook.com/100000000000001/picture',
    ...overrides,
  };
}

describe('facebookIdFromToken', () => {
  it('reads the Facebook user id from firebase.identities', () => {
    expect(facebookIdFromToken(tokenWith())).toBe('100000000000001');
  });

  it('returns null when no Facebook identity is present', () => {
    const emailOnly: AuthTokenClaims = {
      firebase: { identities: { email: ['a@b.com'] } },
    };
    expect(facebookIdFromToken(emailOnly)).toBeNull();
  });

  it('returns null for a token with no firebase claim (e.g. anonymous)', () => {
    expect(facebookIdFromToken({})).toBeNull();
    expect(facebookIdFromToken(null)).toBeNull();
    expect(facebookIdFromToken(undefined)).toBeNull();
  });

  it('returns null when the identity array is empty or malformed', () => {
    expect(facebookIdFromToken({ firebase: { identities: { 'facebook.com': [] } } })).toBeNull();
    expect(
      facebookIdFromToken({ firebase: { identities: { 'facebook.com': [42] } } }),
    ).toBeNull();
    expect(
      facebookIdFromToken({ firebase: { identities: { 'facebook.com': '123' } } }),
    ).toBeNull();
  });
});

describe('facebookProfileFromToken', () => {
  it('extracts display name and photo when present', () => {
    expect(facebookProfileFromToken(tokenWith())).toEqual({
      displayName: 'Giselle Johnson',
      photoURL: 'https://graph.facebook.com/100000000000001/picture',
    });
  });

  it('trims the display name', () => {
    expect(facebookProfileFromToken(tokenWith({ name: '  Marcus  ' })).displayName).toBe('Marcus');
  });

  it('omits fields that are absent or not strings (never writes undefined)', () => {
    expect(facebookProfileFromToken({ firebase: { identities: {} } })).toEqual({});
    expect(facebookProfileFromToken(tokenWith({ name: '', picture: 42 }))).toEqual({});
  });
});
