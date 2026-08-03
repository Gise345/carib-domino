/**
 * Identifier aliases mirroring the C# `PlayerId` / `TeamId` value types. In C#
 * these wrap a non-empty string with ordinal equality; in TypeScript a plain
 * `string` with `===` is ordinal, so aliases keep the port readable without a
 * branding layer. Empty strings are invalid, same as the C# constructors.
 */

export type PlayerId = string;
export type TeamId = string;
