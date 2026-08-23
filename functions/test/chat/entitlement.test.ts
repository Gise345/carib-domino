import { describe, expect, it } from 'vitest';
import { isGuestToken } from '../../src/chat/entitlement';
import { isMuteActive } from '../../src/chat/mutes';

describe('isGuestToken', () => {
  it('identifies an anonymous session as a guest', () => {
    expect(isGuestToken({ firebase: { sign_in_provider: 'anonymous' } })).toBe(true);
  });

  it('treats every real sign-in provider as an account', () => {
    expect(isGuestToken({ firebase: { sign_in_provider: 'password' } })).toBe(false);
    expect(isGuestToken({ firebase: { sign_in_provider: 'facebook.com' } })).toBe(false);
    expect(isGuestToken({ firebase: { sign_in_provider: 'google.com' } })).toBe(false);
  });

  it('does not treat a missing provider as a guest', () => {
    // Fail open here, not closed: the ban/mute/membership gates still apply, and
    // wrongly classing an account holder as a guest would silently mute them.
    expect(isGuestToken({})).toBe(false);
    expect(isGuestToken(undefined)).toBe(false);
    expect(isGuestToken({ firebase: {} })).toBe(false);
  });
});

describe('isMuteActive', () => {
  const now = new Date('2026-08-22T12:00:00.000Z');

  it('is inactive when no mute exists', () => {
    expect(isMuteActive(undefined, now)).toBe(false);
    expect(isMuteActive({}, now)).toBe(false);
  });

  it('is active while the expiry is in the future', () => {
    expect(isMuteActive({ until: new Date('2026-08-22T13:00:00.000Z') }, now)).toBe(true);
  });

  it('lapses on its own once the expiry passes', () => {
    expect(isMuteActive({ until: new Date('2026-08-22T11:59:59.000Z') }, now)).toBe(false);
  });

  it('accepts a Firestore timestamp as well as a Date', () => {
    const asTimestamp = { toDate: () => new Date('2026-08-23T00:00:00.000Z') };

    expect(isMuteActive({ until: asTimestamp }, now)).toBe(true);
  });
});
