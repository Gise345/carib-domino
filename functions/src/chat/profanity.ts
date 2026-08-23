/**
 * Server-side profanity filter (ADR 0023). Pure and dependency-free so the whole
 * matching behaviour — including bypass attempts — is unit-testable.
 *
 * Policy: matches are MASKED and the message is still delivered, while the
 * caller stores the unmasked original for moderators. Blocking would teach an
 * abuser to probe the list; masking keeps the room civil and the evidence intact.
 *
 * Two tiers:
 *  - {@link MASKED_TERMS}: ordinary profanity. Matched per word, so innocent
 *    words that merely CONTAIN a term (the classic "Scunthorpe" problem) survive.
 *  - {@link SEVERE_TERMS}: slurs. Matched against the whole message with every
 *    separator squeezed out, because "n i g g e r" is the standard bypass. A hit
 *    masks the entire message and flags it for proactive review.
 */

/** Ordinary profanity — masked word by word. */
const MASKED_TERMS = new Set([
  'arse',
  'arsehole',
  'ass',
  'asshole',
  'bastard',
  'bitch',
  'bitches',
  'biatch',
  'btch',
  'bollocks',
  'bullshit',
  'cock',
  'crap',
  'cunt',
  'dick',
  'dickhead',
  'douche',
  'fuck',
  'fucked',
  'fucker',
  'fucking',
  'fuk',
  'fck',
  'fack',
  'phuck',
  'phuk',
  'motherfucker',
  'nigga',
  'piss',
  'prick',
  'pussy',
  'shit',
  'shite',
  'shyt',
  'shyte',
  'sht',
  'shitty',
  'slut',
  'twat',
  'wanker',
  'whore',
]);

/** Slurs and sexual-abuse terms — matched across separators, mask the message. */
const SEVERE_TERMS = [
  'nigger',
  'faggot',
  'retard',
  'tranny',
  'kike',
  'spic',
  'chink',
  'paedo',
  'pedophile',
  'rape',
];

/** Leetspeak / homoglyph folding applied before matching. */
const LEET: Readonly<Record<string, string>> = {
  '0': 'o',
  '1': 'i',
  '3': 'e',
  '4': 'a',
  '5': 's',
  '7': 't',
  '8': 'b',
  '@': 'a',
  $: 's',
  '!': 'i',
  '|': 'i',
  '+': 't',
};

/** Outcome of filtering one message. */
export interface FilterResult {
  /** The text safe to show players. Equal to the input when nothing matched. */
  readonly text: string;
  /** True when anything was masked. */
  readonly filtered: boolean;
  /** True when a slur-tier term matched — surfaced to moderators proactively. */
  readonly severe: boolean;
}

/**
 * Folds a token to its matching form: lowercase, diacritics stripped, leetspeak
 * substituted, decorative characters dropped, and runs of a repeated letter
 * collapsed ("fuuuuck" → "fuck").
 *
 * @param token - one whitespace-delimited word
 * @returns the folded, letters-only form (may be empty)
 */
export function foldToken(token: string): string {
  const lowered = token
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    // Edge punctuation is punctuation, not leetspeak: without this the '!' in
    // "sh!t!" folds to a trailing 'i' and the word stops matching.
    .replace(/^[^a-z0-9@$!|+]+|[!?.,;:'"()[\]{}|+*~_-]+$/g, '');

  let mapped = '';
  for (const ch of lowered) {
    mapped += LEET[ch] ?? ch;
  }

  return mapped.replace(/[^a-z]/g, '').replace(/(.)\1{2,}/g, '$1$1');
}

/** Collapses every run of a repeated letter, so "niiiigger" and "nigger" agree. */
function dedupe(word: string): string {
  return word.replace(/(.)\1+/g, '$1');
}

/** Inflections a term is still the same slur under. */
const SUFFIXES = ['', 's', 'es', 'ed', 'er', 'ers', 'ing', 'ist', 'ists', 'z'];

/**
 * Whether one folded word is a slur-tier term under any accepted inflection.
 * Whole-word (not substring) matching \u2014 "grape" must not read as a slur.
 */
function isSevereWord(folded: string): boolean {
  if (folded.length === 0) {
    return false;
  }
  const squashed = dedupe(folded);
  return SEVERE_TERMS.some((term) =>
    SUFFIXES.some((suffix) => folded === term + suffix || squashed === dedupe(term) + suffix),
  );
}

/**
 * Whether the folded form of a single word is ordinary profanity.
 *
 * @param token - one word of the message
 * @returns true when the word should be masked
 */
export function isProfaneToken(token: string): boolean {
  const folded = foldToken(token);
  // "fuck" collapses to itself; "fuuuck" collapses to "fuuck", so also try the
  // fully de-duplicated form before giving up.
  return MASKED_TERMS.has(folded) || MASKED_TERMS.has(folded.replace(/(.)\1+/g, '$1'));
}

/**
 * Whether a message contains a slur-tier term — as a word, or spelled out one
 * character at a time ("n i g g e r"), which is the standard bypass.
 *
 * @param text - the full message
 * @returns true when the message is severe
 */
export function containsSevereTerm(text: string): boolean {
  const folded = text.split(' ').map(foldToken);
  if (folded.some(isSevereWord)) {
    return true;
  }

  // Spelled-out bypass: only fragments of one or two letters are joined, so
  // ordinary sentences can never be glued into an accidental match.
  let run = '';
  for (const word of folded) {
    if (word.length > 0 && word.length <= 2) {
      run += word;
      if (isSevereWord(run) || SEVERE_TERMS.some((term) => run.includes(term))) {
        return true;
      }
    } else {
      run = '';
    }
  }
  return false;
}

/**
 * Masks profanity in a message.
 *
 * @param text - the normalised message text
 * @returns the masked text plus whether anything matched and how badly
 */
export function filterProfanity(text: string): FilterResult {
  if (containsSevereTerm(text)) {
    return { text: '*'.repeat(Math.min(text.length, 12)), filtered: true, severe: true };
  }

  let filtered = false;
  const masked = text
    .split(' ')
    .map((token) => {
      if (token.length > 0 && isProfaneToken(token)) {
        filtered = true;
        return '*'.repeat(token.length);
      }
      return token;
    })
    .join(' ');

  return { text: masked, filtered, severe: false };
}
