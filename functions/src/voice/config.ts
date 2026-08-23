/**
 * Vivox credentials (ADR 0024). Split deliberately by sensitivity.
 *
 * The signing key is a SECRET: it can mint a credential to join any channel as
 * any user, so it lives in Secret Manager, is bound only to the function that
 * signs tokens, and never crosses the wire. The issuer, domain and server are
 * not secret — but they are still not committed and not compiled into the
 * client. `joinVoiceRoom` hands them to the game at runtime, so switching
 * environments is a config change rather than a store build, and so the repo
 * never repeats the mistake of `PhotonAppSettings.asset`, which carries a
 * plaintext AppId in version control to this day.
 *
 * Set with:
 *   firebase functions:secrets:set VIVOX_TOKEN_KEY
 *   # VIVOX_ISSUER / VIVOX_DOMAIN / VIVOX_SERVER in functions/.env (gitignored)
 */

import { defineSecret, defineString } from 'firebase-functions/params';

/**
 * Named locally rather than imported: firebase-functions does not export its
 * param types from a public subpath, and without an explicit annotation tsc
 * refuses to emit declarations that reference them (TS2742).
 */
type SecretParam = ReturnType<typeof defineSecret>;
type StringParam = ReturnType<typeof defineString>;

/** HMAC key that signs Vivox access tokens. Secret Manager only. */
export const VIVOX_TOKEN_KEY: SecretParam = defineSecret('VIVOX_TOKEN_KEY');

/** Application-specific issuer, e.g. `pose-carib-domino-dev`. */
export const VIVOX_ISSUER: StringParam = defineString('VIVOX_ISSUER', { default: '' });

/** Vivox domain the SIP URIs are built against, e.g. `tla.vivox.com`. */
export const VIVOX_DOMAIN: StringParam = defineString('VIVOX_DOMAIN', { default: '' });

/** Vivox API endpoint the client connects to. */
export const VIVOX_SERVER: StringParam = defineString('VIVOX_SERVER', { default: '' });

/** The non-secret settings the client needs to initialise the Vivox SDK. */
export interface VivoxClientConfig {
  readonly server: string;
  readonly domain: string;
  readonly issuer: string;
}

/**
 * Reads the client-safe Vivox settings.
 *
 * @returns the settings to hand to the game client
 */
export function vivoxClientConfig(): VivoxClientConfig {
  return {
    server: VIVOX_SERVER.value(),
    domain: VIVOX_DOMAIN.value(),
    issuer: VIVOX_ISSUER.value(),
  };
}

/**
 * Whether Vivox has actually been provisioned in this environment. Until the
 * manual setup in ADR 0024 is done every value is empty, and the honest answer
 * to the client is "voice is unavailable" rather than a token it cannot use.
 *
 * @param config - the client settings
 * @returns true when all three are present
 */
export function isVivoxProvisioned(config: VivoxClientConfig): boolean {
  return config.server !== '' && config.domain !== '' && config.issuer !== '';
}
