import { describe, expect, it } from 'vitest';
import { resolveFriendUids } from '../../src/social/facebookFriends';

const SELF = 'uid-self';

describe('resolveFriendUids', () => {
  it('maps friend Facebook ids to their app uids in order', () => {
    const map = new Map([
      ['fb1', 'uid-a'],
      ['fb2', 'uid-b'],
    ]);
    expect(resolveFriendUids(['fb1', 'fb2'], map, SELF)).toEqual(['uid-a', 'uid-b']);
  });

  it('drops friends with no app account', () => {
    const map = new Map([['fb2', 'uid-b']]);
    expect(resolveFriendUids(['fb1', 'fb2', 'fb3'], map, SELF)).toEqual(['uid-b']);
  });

  it('excludes the caller even if they appear in their own friend list', () => {
    const map = new Map([
      ['fb-self', SELF],
      ['fb1', 'uid-a'],
    ]);
    expect(resolveFriendUids(['fb-self', 'fb1'], map, SELF)).toEqual(['uid-a']);
  });

  it('dedups when two Facebook ids map to the same uid', () => {
    const map = new Map([
      ['fb1', 'uid-a'],
      ['fb1-alt', 'uid-a'],
      ['fb2', 'uid-b'],
    ]);
    expect(resolveFriendUids(['fb1', 'fb1-alt', 'fb2'], map, SELF)).toEqual(['uid-a', 'uid-b']);
  });

  it('returns empty when no friends resolve', () => {
    expect(resolveFriendUids(['fb1', 'fb2'], new Map(), SELF)).toEqual([]);
    expect(resolveFriendUids([], new Map(), SELF)).toEqual([]);
  });
});
