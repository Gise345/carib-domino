import { describe, expect, it } from 'vitest';
import { INVITE_DAILY_CAP, INVITE_REWARD } from '../../src/lib/economy';
import { evaluateInvite, InviteDay } from '../../src/social/invite';

const DAY = '2026-08-14';

describe('evaluateInvite', () => {
  it('rewards the first invite of a fresh day', () => {
    const r = evaluateInvite(null, DAY, 'a');
    expect(r.reward).toBe(INVITE_REWARD);
    expect(r.outcome).toBe('ok');
    expect(r.nextRecord).toEqual({ day: DAY, ids: ['a'] });
    expect(r.remainingToday).toBe(INVITE_DAILY_CAP - 1);
  });

  it('does not double-pay the same invite id', () => {
    const rec: InviteDay = { day: DAY, ids: ['a'] };
    const r = evaluateInvite(rec, DAY, 'a');
    expect(r.reward).toBe(0);
    expect(r.outcome).toBe('duplicate');
    expect(r.nextRecord.ids).toEqual(['a']); // unchanged
  });

  it('stops paying once the daily cap is hit', () => {
    let rec: InviteDay = { day: DAY, ids: [] };
    for (let i = 0; i < INVITE_DAILY_CAP; i++) {
      const r = evaluateInvite(rec, DAY, `inv${String(i)}`);
      expect(r.reward).toBe(INVITE_REWARD);
      rec = r.nextRecord;
    }
    expect(rec.ids.length).toBe(INVITE_DAILY_CAP);

    const over = evaluateInvite(rec, DAY, 'one-more');
    expect(over.reward).toBe(0);
    expect(over.outcome).toBe('cap');
    expect(over.remainingToday).toBe(0);
  });

  it('resets on a new day (yesterday full → today pays again)', () => {
    const full: InviteDay = { day: '2026-08-13', ids: ['x', 'y', 'z'] };
    const r = evaluateInvite(full, DAY, 'x'); // same id, but new day
    expect(r.reward).toBe(INVITE_REWARD);
    expect(r.nextRecord).toEqual({ day: DAY, ids: ['x'] });
  });

  it('never lets remainingToday go negative', () => {
    const rec: InviteDay = { day: DAY, ids: ['a', 'b', 'c', 'd'] }; // already over cap
    const r = evaluateInvite(rec, DAY, 'e');
    expect(r.remainingToday).toBe(0);
  });
});
