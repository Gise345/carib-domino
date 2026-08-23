import { describe, expect, it } from 'vitest';
import {
  containsSevereTerm,
  filterProfanity,
  foldToken,
  isProfaneToken,
} from '../../src/chat/profanity';

describe('foldToken', () => {
  it('lowercases, strips punctuation and folds leetspeak', () => {
    expect(foldToken('F@ck!')).toBe('fack'); // '@' folds to 'a'; the trailing '!' is punctuation
    expect(foldToken('Sh1t.')).toBe('shit');
    expect(foldToken('$hit')).toBe('shit');
  });

  it('strips diacritics', () => {
    expect(foldToken('shít')).toBe('shit');
  });

  it('collapses long letter runs', () => {
    expect(foldToken('fuuuuuck')).toBe('fuuck');
  });
});

describe('isProfaneToken', () => {
  it('catches plain profanity', () => {
    expect(isProfaneToken('shit')).toBe(true);
    expect(isProfaneToken('SHIT')).toBe(true);
    expect(isProfaneToken('bitch')).toBe(true);
  });

  it('catches punctuation, leetspeak and stretched spellings', () => {
    expect(isProfaneToken('sh!t')).toBe(true);
    expect(isProfaneToken('$h1t')).toBe(true);
    expect(isProfaneToken('shiiiiit')).toBe(true);
    expect(isProfaneToken('sh!t!')).toBe(true); // trailing '!' must not fold to a letter
    expect(isProfaneToken('f@ck')).toBe(true);
    expect(isProfaneToken('fck')).toBe(true);
    expect(isProfaneToken('b*tch')).toBe(true); // the star drops out, leaving a listed spelling
  });

  it('leaves innocent words alone (the Scunthorpe problem)', () => {
    // Each of these CONTAINS a listed term but is not one.
    expect(isProfaneToken('Scunthorpe')).toBe(false);
    expect(isProfaneToken('class')).toBe(false);
    expect(isProfaneToken('assess')).toBe(false);
    expect(isProfaneToken('cocktail')).toBe(false);
    expect(isProfaneToken('dickens')).toBe(false);
    expect(isProfaneToken('pass')).toBe(false);
  });
});

describe('containsSevereTerm', () => {
  it('catches a slur written as a word', () => {
    expect(containsSevereTerm('you faggot')).toBe(true);
  });

  it('catches a slur spelled out one letter at a time', () => {
    expect(containsSevereTerm('f a g g o t')).toBe(true);
    expect(containsSevereTerm('k i k e')).toBe(true);
  });

  it('catches a slur split by punctuation inside one token', () => {
    expect(containsSevereTerm('f.a.g.g.o.t')).toBe(true);
  });

  it('does not fire on ordinary sentences', () => {
    expect(containsSevereTerm('good luck everyone')).toBe(false);
    expect(containsSevereTerm('pass the grapes')).toBe(false); // contains "rape"
    expect(containsSevereTerm('I drape the board')).toBe(false);
    expect(containsSevereTerm('a b c d e f g')).toBe(false); // short tokens, no slur
    expect(containsSevereTerm('that was a great trap')).toBe(false);
  });
});

describe('filterProfanity', () => {
  it('passes a clean message through untouched', () => {
    const result = filterProfanity('Good luck everyone!');

    expect(result.text).toBe('Good luck everyone!');
    expect(result.filtered).toBe(false);
    expect(result.severe).toBe(false);
  });

  it('masks only the offending word and delivers the rest', () => {
    const result = filterProfanity('that was shit luck');

    expect(result.text).toBe('that was **** luck');
    expect(result.filtered).toBe(true);
    expect(result.severe).toBe(false);
  });

  it('masks the whole message when a slur is used', () => {
    const result = filterProfanity('shut up you faggot');

    expect(result.text).toMatch(/^\*+$/);
    expect(result.filtered).toBe(true);
    expect(result.severe).toBe(true);
  });

  it('preserves word length so the shape of the sentence survives', () => {
    const result = filterProfanity('bitch please');

    expect(result.text).toBe('***** please');
  });
});
