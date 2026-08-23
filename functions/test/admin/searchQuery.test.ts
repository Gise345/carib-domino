import { describe, expect, it } from 'vitest';
import { looksLikeEmail } from '../../src/admin/searchQuery';

describe('looksLikeEmail', () => {
  it('accepts real email shapes (trimmed)', () => {
    expect(looksLikeEmail('a@b.com')).toBe(true);
    expect(looksLikeEmail('  gise.a.k@gmail.com ')).toBe(true);
    expect(looksLikeEmail('i.t.cayman@invovibetech.com')).toBe(true);
  });

  it('rejects names and uids', () => {
    expect(looksLikeEmail('Marcus')).toBe(false);
    expect(looksLikeEmail('uid123abc')).toBe(false);
  });

  it('rejects malformed near-emails', () => {
    expect(looksLikeEmail('@nope')).toBe(false); // nothing before @
    expect(looksLikeEmail('a@b')).toBe(false); // no dotted domain
    expect(looksLikeEmail('a@.com')).toBe(false); // dot immediately after @
    expect(looksLikeEmail('a@b.')).toBe(false); // dot at the very end
  });
});
