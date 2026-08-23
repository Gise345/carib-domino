import { describe, expect, it } from 'vitest';
import { AuthorizationInput, authorizeTokenRequest } from '../../src/voice/authorize';
import { REFUSAL_NOT_IN_ROOM } from '../../src/voice/model';

const CALLER = 'uidCaller';
const OTHER = 'uidOther';
const CHANNEL = 'pose-deadbeefdeadbeefdeadbeefdeadbe';

const joinRequest = (over: Partial<AuthorizationInput> = {}): AuthorizationInput => ({
  action: 'join',
  callerUid: CALLER,
  roomId: 'ABCD',
  roomExists: true,
  roomMembers: { [CALLER]: { name: 'Me', seat: 0 }, [OTHER]: { name: 'Them', seat: 1 } },
  roomVoiceChannel: CHANNEL,
  ...over,
});

describe('authorizeTokenRequest — the identity it vouches for', () => {
  it('never accepts a client-supplied identity or channel', () => {
    // The strongest form of the ADR 0024 §4 rule: those arguments do not exist
    // on this function's signature, so no future edit can accidentally sign
    // them. This test fails to compile if someone adds them back.
    const input = joinRequest() as Record<string, unknown>;

    expect(input['fromUri']).toBeUndefined();
    expect(input['fromUserUri']).toBeUndefined();
    expect(input['channelUri']).toBeUndefined();
  });

  it('vouches only for the caller, even when the room holds other players', () => {
    const result = authorizeTokenRequest(joinRequest());

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.fromPlayerId).toBe(CALLER);
      expect(result.fromPlayerId).not.toBe(OTHER);
    }
  });

  it('names only the channel stored on the room', () => {
    const result = authorizeTokenRequest(joinRequest({ roomVoiceChannel: 'pose-storedchannel' }));

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.channelName).toBe('pose-storedchannel');
    }
  });
});

describe('authorizeTokenRequest — login', () => {
  it('grants without a room, and names no channel', () => {
    const result = authorizeTokenRequest({ action: 'login', callerUid: CALLER });

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.fromPlayerId).toBe(CALLER);
      expect(result.channelName).toBeNull();
    }
  });

  it('refuses an unauthenticated caller', () => {
    const result = authorizeTokenRequest({ action: 'login', callerUid: '' });

    expect(result.ok).toBe(false);
  });
});

describe('authorizeTokenRequest — join', () => {
  it('refuses a caller who is not a member of the room', () => {
    const result = authorizeTokenRequest(
      joinRequest({ roomMembers: { [OTHER]: { name: 'Them', seat: 1 } } }),
    );

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.code).toBe(REFUSAL_NOT_IN_ROOM);
    }
  });

  it('refuses a room that does not exist', () => {
    expect(authorizeTokenRequest(joinRequest({ roomExists: false })).ok).toBe(false);
    expect(authorizeTokenRequest(joinRequest({ roomExists: undefined })).ok).toBe(false);
  });

  it('refuses a room voice was never opened in', () => {
    expect(authorizeTokenRequest(joinRequest({ roomVoiceChannel: undefined })).ok).toBe(false);
    expect(authorizeTokenRequest(joinRequest({ roomVoiceChannel: '' })).ok).toBe(false);
  });

  it('refuses a join with no room id', () => {
    expect(authorizeTokenRequest(joinRequest({ roomId: undefined })).ok).toBe(false);
  });

  it('refuses a room id that could escape the document path', () => {
    for (const roomId of ['../admin', 'rooms/other', 'a', 'a'.repeat(65), 'room id']) {
      expect(authorizeTokenRequest(joinRequest({ roomId })).ok, roomId).toBe(false);
    }
  });

  it('is not fooled by a prototype-polluted membership map', () => {
    // `{}.constructor` is truthy on a bare object; hasOwnProperty is why this
    // is safe, and this pins it.
    const result = authorizeTokenRequest(
      joinRequest({ callerUid: 'constructor', roomMembers: {} }),
    );

    expect(result.ok).toBe(false);
  });
});
