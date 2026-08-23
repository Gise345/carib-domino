import { describe, expect, it } from 'vitest';
import { channelUri, isValidVivoxName, userUri, voiceChannelName } from '../../src/voice/vivoxUri';

const ISSUER = 'pose-carib-domino-dev';
const DOMAIN = 'tla.vivox.com';

describe('voiceChannelName', () => {
  it('is deterministic, so every player derives the same channel', () => {
    expect(voiceChannelName('ABCD')).toBe(voiceChannelName('ABCD'));
  });

  it('distinguishes case-variant room ids', () => {
    // The whole reason we hash. Photon session names are case-SENSITIVE, so
    // these are two different rooms — but Vivox folds case-variant channel
    // names together, which would put strangers in each other's ears.
    expect(voiceChannelName('ABC123')).not.toBe(voiceChannelName('abc123'));
  });

  it('does not leak the room code', () => {
    expect(voiceChannelName('WXYZ')).not.toContain('WXYZ');
    expect(voiceChannelName('WXYZ').toLowerCase()).not.toContain('wxyz');
  });

  it('always produces a Vivox-legal name, for every legal room id', () => {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-';
    for (let i = 0; i < 500; i++) {
      let roomId = '';
      const length = 4 + (i % 61);
      for (let c = 0; c < length; c++) {
        roomId += alphabet[(i * 31 + c * 17) % alphabet.length];
      }

      const channel = voiceChannelName(roomId);

      expect(isValidVivoxName(channel), `illegal channel for ${roomId}`).toBe(true);
      expect(channel.length).toBeLessThanOrEqual(200);
    }
  });

  it('handles both a 4-char room code and a Photon session id', () => {
    expect(isValidVivoxName(voiceChannelName('7Q2M'))).toBe(true);
    expect(isValidVivoxName(voiceChannelName('a1b2c3d4-e5f6-7890-abcd-ef1234567890'))).toBe(true);
  });
});

describe('isValidVivoxName', () => {
  it('accepts the documented character set', () => {
    expect(isValidVivoxName('pose-abc123')).toBe(true);
    expect(isValidVivoxName('a!()+-.=_~')).toBe(true);
  });

  it('rejects anything Vivox would refuse', () => {
    expect(isValidVivoxName('has space')).toBe(false);
    expect(isValidVivoxName('slash/name')).toBe(false);
    expect(isValidVivoxName('at@name')).toBe(false);
    expect(isValidVivoxName('')).toBe(false);
    expect(isValidVivoxName('a'.repeat(201))).toBe(false);
  });
});

describe('userUri', () => {
  it('produces the exact shape Vivox parses player ids out of', () => {
    // The leading dot and the dot before "@" are mandatory, not cosmetic.
    expect(userUri(ISSUER, DOMAIN, 'uid123')).toBe(
      'sip:.pose-carib-domino-dev.uid123.@tla.vivox.com',
    );
  });
});

describe('channelUri', () => {
  it('produces a non-positional group channel uri', () => {
    // "confctl-g-" is what marks it 2D; positional audio is meaningless at a
    // domino table.
    expect(channelUri(ISSUER, DOMAIN, 'pose-deadbeef')).toBe(
      'sip:confctl-g-pose-carib-domino-dev.pose-deadbeef@tla.vivox.com',
    );
  });
});
