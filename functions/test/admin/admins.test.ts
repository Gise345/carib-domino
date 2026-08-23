import { describe, expect, it } from 'vitest';
import { isAllowlistedAdmin } from '../../src/admin/admins';

describe('isAllowlistedAdmin', () => {
  it('accepts each allowlisted admin', () => {
    expect(isAllowlistedAdmin('gise.a.k@gmail.com')).toBe(true);
    expect(isAllowlistedAdmin('i.t.cayman@invovibetech.com')).toBe(true);
    expect(isAllowlistedAdmin('micheeboo2191@gmail.com')).toBe(true);
    expect(isAllowlistedAdmin('mtjohnson50@gmail.com')).toBe(true);
  });

  it('is case-insensitive and trims whitespace', () => {
    expect(isAllowlistedAdmin('  GISE.A.K@Gmail.com ')).toBe(true);
  });

  it('rejects non-admins', () => {
    expect(isAllowlistedAdmin('someone@gmail.com')).toBe(false);
    expect(isAllowlistedAdmin('gise.a.k@evil.com')).toBe(false);
  });

  it('rejects the typo variant that is NOT the real address', () => {
    // Guards against accidentally allowlisting a look-alike (e.g. the ".om" typo).
    expect(isAllowlistedAdmin('mtjohnson50@gmail.om')).toBe(false);
  });

  it('rejects empty / missing', () => {
    expect(isAllowlistedAdmin('')).toBe(false);
    expect(isAllowlistedAdmin('   ')).toBe(false);
    expect(isAllowlistedAdmin(undefined)).toBe(false);
    expect(isAllowlistedAdmin(null)).toBe(false);
  });
});
