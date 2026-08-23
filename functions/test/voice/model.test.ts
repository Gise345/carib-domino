import { describe, expect, it } from 'vitest';
import {
  VOICE_ACTIONS,
  VOICE_TOKEN_RATE_MAX,
  VOICE_TOKEN_TTL_MS,
  VOICE_TOKEN_WINDOW_MS,
  isVoiceAction,
} from '../../src/voice/model';

describe('voice token limits', () => {
  it('mints short-lived tokens, per Vivox guidance', () => {
    expect(VOICE_TOKEN_TTL_MS).toBe(90_000);
  });

  it('allows enough mints for a match with reconnects, and no more', () => {
    // Two per match (login, join) plus headroom for reconnects.
    expect(VOICE_TOKEN_RATE_MAX).toBeGreaterThanOrEqual(4);
    expect(VOICE_TOKEN_RATE_MAX).toBeLessThanOrEqual(20);
    expect(VOICE_TOKEN_WINDOW_MS).toBeGreaterThan(VOICE_TOKEN_TTL_MS / 2);
  });
});

describe('isVoiceAction', () => {
  it('accepts the two actions a player performs for themselves', () => {
    expect(isVoiceAction('login')).toBe(true);
    expect(isVoiceAction('join')).toBe(true);
  });

  it('refuses the moderator actions Vivox also supports', () => {
    // kick/mute/transcription let one player act on another. Nothing in this
    // game should ever ask for them, so they are not mintable at all.
    expect(isVoiceAction('kick')).toBe(false);
    expect(isVoiceAction('mute')).toBe(false);
    expect(isVoiceAction('transcription')).toBe(false);
  });

  it('refuses junk', () => {
    expect(isVoiceAction('')).toBe(false);
    expect(isVoiceAction('LOGIN')).toBe(false);
    expect(isVoiceAction('toString')).toBe(false);
  });

  it('stays in step with the declared action list', () => {
    for (const action of VOICE_ACTIONS) {
      expect(isVoiceAction(action)).toBe(true);
    }
  });
});
