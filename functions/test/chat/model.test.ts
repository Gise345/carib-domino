import { describe, expect, it } from 'vitest';
import {
  MAX_MESSAGE_LENGTH,
  ROOM_RETENTION_DAYS,
  isValidRoomId,
  normalizeMessageText,
  retentionExpiry,
} from '../../src/chat/model';

describe('isValidRoomId', () => {
  it('accepts Photon room codes and session ids', () => {
    expect(isValidRoomId('ABCD')).toBe(true);
    expect(isValidRoomId('room-42_x')).toBe(true);
    expect(isValidRoomId('a'.repeat(64))).toBe(true);
  });

  it('rejects ids that could escape the document path', () => {
    expect(isValidRoomId('rooms/other')).toBe(false);
    expect(isValidRoomId('../admin')).toBe(false);
    expect(isValidRoomId('room id')).toBe(false);
  });

  it('rejects out-of-range lengths', () => {
    expect(isValidRoomId('abc')).toBe(false);
    expect(isValidRoomId('a'.repeat(65))).toBe(false);
    expect(isValidRoomId('')).toBe(false);
  });
});

describe('normalizeMessageText', () => {
  it('trims and collapses whitespace', () => {
    expect(normalizeMessageText('  good   luck  ')).toBe('good luck');
  });

  it('flattens the newline padding used to shout', () => {
    expect(normalizeMessageText('hey\n\n\n\n\n\n\nyou')).toBe('hey you');
  });

  it('strips control characters', () => {
    expect(normalizeMessageText('nice\u0007\u0000play')).toBe('nice play');
  });

  it('can empty a message made only of whitespace', () => {
    expect(normalizeMessageText('   \n\t  ')).toBe('');
  });

  it('leaves ordinary punctuation and accents intact', () => {
    expect(normalizeMessageText('¡Buena jugada, compadre!')).toBe('¡Buena jugada, compadre!');
  });

  it('lets a padded over-long message be caught by the length rule', () => {
    const padded = ` ${'a'.repeat(MAX_MESSAGE_LENGTH + 1)} `;

    expect(normalizeMessageText(padded).length).toBe(MAX_MESSAGE_LENGTH + 1);
  });
});

describe('retentionExpiry', () => {
  it('is the retention window past the given moment', () => {
    const now = new Date('2026-08-22T12:00:00.000Z');

    const expiry = retentionExpiry(now);

    expect(expiry.getTime() - now.getTime()).toBe(ROOM_RETENTION_DAYS * 86_400_000);
  });
});
