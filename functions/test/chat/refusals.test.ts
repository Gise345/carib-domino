import { describe, expect, it } from 'vitest';
import {
  REFUSAL_GUEST,
  REFUSAL_MUTED,
  REFUSAL_RATE_LIMITED,
  refusal,
} from '../../src/chat/refusals';

/**
 * The server half of the refusal contract. The game client reads the code off
 * the message prefix (Pose.Core.Chat.ChatRefusal) because Unity's
 * FunctionsException drops the structured details, so the codes and the shape of
 * the prefix are a wire format, not cosmetics.
 */
describe('chat refusals', () => {
  it('prefixes the message with its code', () => {
    expect(refusal(REFUSAL_MUTED, 'You are muted in chat.')).toBe(
      'muted: You are muted in chat.',
    );
  });

  it('uses the codes the Unity client matches on', () => {
    expect(REFUSAL_GUEST).toBe('guest-restricted');
    expect(REFUSAL_MUTED).toBe('muted');
    expect(REFUSAL_RATE_LIMITED).toBe('rate-limited');
  });

  it('produces codes the client parser can recover', () => {
    // Mirrors ChatRefusal.CodeOf: everything before the first colon, one token.
    for (const code of [REFUSAL_GUEST, REFUSAL_MUTED, REFUSAL_RATE_LIMITED]) {
      const message = refusal(code, 'Some human explanation: with a colon.');
      const parsed = message.slice(0, message.indexOf(':')).trim();

      expect(parsed).toBe(code);
      expect(parsed).not.toContain(' ');
      expect(parsed.length).toBeLessThanOrEqual(32);
    }
  });
});
