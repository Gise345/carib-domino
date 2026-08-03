import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * `testerSignup` is an `onRequest` handler, so the tests drive the raw
 * (req, res) callback rather than going through the Functions framework. The
 * Admin SDK is mocked so nothing touches a real (or emulated) Firestore.
 */

const setMock = vi.fn().mockResolvedValue(undefined);
const getMock = vi.fn().mockResolvedValue({ exists: false });
const docMock = vi.fn(() => ({ set: setMock, get: getMock }));
const collectionMock = vi.fn(() => ({ doc: docMock }));

vi.mock('firebase-admin/app', () => ({
  getApps: () => [{}],
  initializeApp: vi.fn(),
}));

vi.mock('firebase-admin/firestore', () => ({
  getFirestore: () => ({ collection: collectionMock }),
  FieldValue: { serverTimestamp: () => '<<ts>>' },
}));

vi.mock('firebase-functions/v2', () => ({
  logger: { info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

// onRequest here just hands back the handler so the tests can call it directly.
vi.mock('firebase-functions/v2/https', () => ({
  onRequest: (_opts: unknown, handler: unknown) => handler,
}));

const { testerSignup, signupDocId, normalisePlatforms } = await import(
  '../../src/web/testerSignup'
);

type Handler = (req: unknown, res: unknown) => Promise<void>;

interface FakeRes {
  statusCode: number;
  body: Record<string, unknown>;
  headers: Record<string, string>;
  status: (code: number) => FakeRes;
  json: (payload: Record<string, unknown>) => FakeRes;
  set: (key: string, value: string) => FakeRes;
}

function makeRes(): FakeRes {
  const res: FakeRes = {
    statusCode: 0,
    body: {},
    headers: {},
    status(code) {
      res.statusCode = code;
      return res;
    },
    json(payload) {
      res.body = payload;
      return res;
    },
    set(key, value) {
      res.headers[key] = value;
      return res;
    },
  };
  return res;
}

async function post(body: unknown, opts: { method?: string; rawBytes?: number } = {}) {
  const json = JSON.stringify(body ?? {});
  const req = {
    method: opts.method ?? 'POST',
    body,
    rawBody: Buffer.alloc(opts.rawBytes ?? Buffer.byteLength(json)),
  };
  const res = makeRes();

  await (testerSignup as unknown as Handler)(req, res);

  return res;
}

const VALID = { email: 'granny@yard.jm', platforms: ['android'] };

beforeEach(() => {
  vi.clearAllMocks();
  setMock.mockResolvedValue(undefined);
  getMock.mockResolvedValue({ exists: false });
});

describe('testerSignup — happy path', () => {
  it('accepts a valid signup and writes exactly one document', async () => {
    const res = await post(VALID);

    expect(res.statusCode).toBe(200);
    expect(res.body).toEqual({ ok: true });

    expect(collectionMock).toHaveBeenCalledWith('testerSignups');
    expect(setMock).toHaveBeenCalledTimes(1);

    const [written] = setMock.mock.calls[0] as [Record<string, unknown>];
    expect(written.email).toBe('granny@yard.jm');
    expect(written.platforms).toEqual(['android']);
    expect(written.source).toBe('caribbeandominos.com');
  });

  it('accepts both platforms at once', async () => {
    const res = await post({ email: 'both@yard.jm', platforms: ['ios', 'android'] });

    expect(res.statusCode).toBe(200);

    const [written] = setMock.mock.calls[0] as [Record<string, unknown>];
    expect(written.platforms).toEqual(['android', 'ios']);
  });

  it('stores the optional country when supplied', async () => {
    await post({ ...VALID, country: '  Cayman Islands  ' });

    const [written] = setMock.mock.calls[0] as [Record<string, unknown>];
    expect(written.country).toBe('Cayman Islands');
  });

  it('omits country entirely when blank', async () => {
    await post({ ...VALID, country: '   ' });

    const [written] = setMock.mock.calls[0] as [Record<string, unknown>];
    expect(written).not.toHaveProperty('country');
  });
});

describe('testerSignup — normalisation', () => {
  it('trims and lowercases the email before writing', async () => {
    await post({ email: '  GRANNY@Yard.JM  ', platforms: ['ios'] });

    const [written] = setMock.mock.calls[0] as [Record<string, unknown>];
    expect(written.email).toBe('granny@yard.jm');
  });

  it('keys the document by the normalised email, so casing cannot duplicate a seat', async () => {
    await post({ email: 'Granny@Yard.JM', platforms: ['ios'] });

    expect(docMock).toHaveBeenCalledWith(signupDocId('granny@yard.jm'));
  });

  it('deduplicates and sorts platforms', () => {
    expect(normalisePlatforms(['ios', 'android', 'ios'])).toEqual(['android', 'ios']);
  });
});

describe('testerSignup — repeat submissions', () => {
  it('merges into the existing document rather than creating a second one', async () => {
    getMock.mockResolvedValue({ exists: true });

    const res = await post(VALID);

    expect(res.statusCode).toBe(200);
    expect(docMock).toHaveBeenCalledWith(signupDocId('granny@yard.jm'));
    expect(setMock).toHaveBeenCalledTimes(1);
    expect(setMock.mock.calls[0]?.[1]).toEqual({ merge: true });
  });

  it('sets createdAt only on the first submission', async () => {
    await post(VALID);
    expect(setMock.mock.calls[0]?.[0]).toHaveProperty('createdAt');

    setMock.mockClear();
    getMock.mockResolvedValue({ exists: true });

    await post(VALID);
    expect(setMock.mock.calls[0]?.[0]).not.toHaveProperty('createdAt');
    expect(setMock.mock.calls[0]?.[0]).toHaveProperty('updatedAt');
  });
});

describe('testerSignup — rejected payloads', () => {
  it('rejects a malformed email', async () => {
    const res = await post({ email: 'not-an-email', platforms: ['android'] });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects a missing email', async () => {
    const res = await post({ platforms: ['android'] });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects an empty platform list', async () => {
    const res = await post({ email: 'granny@yard.jm', platforms: [] });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects an unknown platform', async () => {
    const res = await post({ email: 'granny@yard.jm', platforms: ['windows-phone'] });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects an over-long email', async () => {
    const res = await post({ email: `${'a'.repeat(250)}@yard.jm`, platforms: ['ios'] });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects an over-long country', async () => {
    const res = await post({ ...VALID, country: 'x'.repeat(61) });

    expect(res.statusCode).toBe(400);
    expect(setMock).not.toHaveBeenCalled();
  });

  it('rejects an oversized body before parsing', async () => {
    const res = await post(VALID, { rawBytes: 5000 });

    expect(res.statusCode).toBe(400);
    expect(res.body).toEqual({ error: 'Payload too large.' });
    expect(setMock).not.toHaveBeenCalled();
  });
});

describe('testerSignup — method handling', () => {
  it.each(['GET', 'PUT', 'DELETE'])('rejects %s with 405 and an Allow header', async (method) => {
    const res = await post(VALID, { method });

    expect(res.statusCode).toBe(405);
    expect(res.headers.Allow).toBe('POST');
    expect(setMock).not.toHaveBeenCalled();
  });
});

describe('testerSignup — bot handling', () => {
  it('looks successful but writes nothing when the honeypot is filled', async () => {
    const res = await post({ ...VALID, nickname: 'buy-cheap-tiles' });

    expect(res.statusCode).toBe(200);
    expect(res.body).toEqual({ ok: true });
    expect(setMock).not.toHaveBeenCalled();
  });

  it('still writes when the honeypot is present but empty', async () => {
    const res = await post({ ...VALID, nickname: '' });

    expect(res.statusCode).toBe(200);
    expect(setMock).toHaveBeenCalledTimes(1);
  });
});

describe('testerSignup — failure handling', () => {
  it('returns 500 without leaking the underlying error', async () => {
    setMock.mockRejectedValue(new Error('firestore exploded'));

    const res = await post(VALID);

    expect(res.statusCode).toBe(500);
    expect(JSON.stringify(res.body)).not.toContain('exploded');
  });
});
