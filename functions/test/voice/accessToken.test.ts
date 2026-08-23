import { createHmac } from 'crypto';
import { describe, expect, it } from 'vitest';
import { buildClaims, signAccessToken } from '../../src/voice/accessToken';
import { VOICE_TOKEN_TTL_MS } from '../../src/voice/model';

const ISSUER = 'pose-carib-domino-dev';
const KEY = 'test-signing-key';
const NOW_MS = 1_600_349_310_000;
const FROM = 'sip:.pose-carib-domino-dev.uid123.@tla.vivox.com';
const TO = 'sip:confctl-g-pose-carib-domino-dev.pose-deadbeef@tla.vivox.com';

const loginClaims = () =>
  buildClaims({ issuer: ISSUER, action: 'login', fromUri: FROM, nowMs: NOW_MS, serial: 1 });

const joinClaims = () =>
  buildClaims({
    issuer: ISSUER,
    action: 'join',
    fromUri: FROM,
    toUri: TO,
    nowMs: NOW_MS,
    serial: 2,
  });

describe('buildClaims', () => {
  it('omits the "to" claim on a login token', () => {
    // A login authorises connecting as a player, not entering anywhere. Vivox's
    // own login example carries no `t`.
    const claims = loginClaims();

    expect(claims).toEqual({
      iss: ISSUER,
      vxi: 1,
      vxa: 'login',
      exp: Math.floor((NOW_MS + VOICE_TOKEN_TTL_MS) / 1000),
      f: FROM,
    });
    expect('t' in claims).toBe(false);
  });

  it('carries the channel on a join token', () => {
    const claims = joinClaims();

    expect(claims.vxa).toBe('join');
    expect(claims.t).toBe(TO);
  });

  it('expires in epoch SECONDS, ninety seconds out', () => {
    // Milliseconds here would produce a token valid until the year 52,000.
    expect(loginClaims().exp).toBe(1_600_349_400);
  });

  it('refuses to build a join token with no channel', () => {
    expect(() =>
      buildClaims({ issuer: ISSUER, action: 'join', fromUri: FROM, nowMs: NOW_MS, serial: 3 }),
    ).toThrow(/join token requires a channel/i);
  });
});

describe('signAccessToken', () => {
  it('uses the empty-object header Vivox expects', () => {
    // Not a real JWT header — Vivox's is a literal {}, which is "e30".
    expect(signAccessToken(loginClaims(), KEY).split('.')[0]).toBe('e30');
  });

  it('produces three base64url segments with no padding', () => {
    const token = signAccessToken(joinClaims(), KEY);
    const parts = token.split('.');

    expect(parts).toHaveLength(3);
    for (const part of parts) {
      expect(part).toMatch(/^[A-Za-z0-9_-]+$/);
    }
  });

  it('signs the header and payload together, matching Vivox HMAC-SHA256', () => {
    const token = signAccessToken(joinClaims(), KEY);
    const [header, payload, signature] = token.split('.');

    const expected = createHmac('sha256', KEY)
      .update(`${header}.${payload}`, 'utf8')
      .digest('base64')
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');

    expect(signature).toBe(expected);
  });

  it('round-trips the claims through the payload segment', () => {
    const payload = signAccessToken(joinClaims(), KEY).split('.')[1] as string;
    const decoded: unknown = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8'));

    expect(decoded).toEqual(joinClaims());
  });

  it('actually uses the key', () => {
    // If the secret were ignored, every token would verify under any key.
    expect(signAccessToken(loginClaims(), KEY)).not.toBe(
      signAccessToken(loginClaims(), 'a-different-key'),
    );
  });

  it('is reproducible for the same claims and key', () => {
    expect(signAccessToken(loginClaims(), KEY)).toBe(signAccessToken(loginClaims(), KEY));
  });

  it('changes with the uniqueness serial', () => {
    const a = buildClaims({
      issuer: ISSUER,
      action: 'login',
      fromUri: FROM,
      nowMs: NOW_MS,
      serial: 1,
    });
    const b = buildClaims({
      issuer: ISSUER,
      action: 'login',
      fromUri: FROM,
      nowMs: NOW_MS,
      serial: 2,
    });

    expect(signAccessToken(a, KEY)).not.toBe(signAccessToken(b, KEY));
  });
});
