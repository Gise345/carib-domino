/**
 * The admin allowlist and membership check (admin subsystem, ADR 0022).
 *
 * This is the single source of truth for who may hold admin powers. It lives
 * server-side only — it is never shipped to a client — so it cannot be read,
 * edited, or spoofed from a device. Every admin action re-checks membership here
 * (not just the custom claim), so a stale or forged claim is useless unless the
 * email is still on this list. Comparison is case-insensitive.
 */
const ADMIN_EMAILS: readonly string[] = [
  'gise.a.k@gmail.com',
  'i.t.cayman@invovibetech.com',
  'micheeboo2191@gmail.com',
  'mtjohnson50@gmail.com',
];

/**
 * True if the (verified) email belongs to an allowlisted admin.
 *
 * @param email - the caller's verified email from the signed auth token
 * @returns whether the email is an allowlisted admin
 */
export function isAllowlistedAdmin(email: string | undefined | null): boolean {
  if (email === undefined || email === null) {
    return false;
  }
  const normalized = email.trim().toLowerCase();
  if (normalized.length === 0) {
    return false;
  }
  return ADMIN_EMAILS.some((a) => a.toLowerCase() === normalized);
}
