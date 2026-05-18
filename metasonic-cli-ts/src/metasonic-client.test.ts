import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('soap', () => ({
  createClientAsync: vi.fn(),
}));

import * as soap from 'soap';
import { MetasonicClient } from './metasonic-client';
import type { RequestDocument } from './types';

function mockSoapClient(overrides?: {
  loginResult?: { loginData: Record<string, unknown> };
  saveResult?: { SaveDocumentResult: { ID: string } };
}) {
  const loginDataWithSession = {
    SessionID: 'test-session',
    Username: 'u',
    Password: 'p',
    NoSession: true,
    AutoLogin: true,
    UseSSO: false,
    LoginDatabaseName: 'test-db',
    ...overrides?.loginResult?.loginData,
  };

  const loginResult = overrides?.loginResult ?? {
    loginData: loginDataWithSession,
  };

  const saveResult = {
    SaveDocumentResult: { ID: 'new-doc-42' },
    loginData: loginDataWithSession,
    ...overrides?.saveResult,
  };

  return {
    LoginAsync: vi.fn().mockResolvedValue([loginResult]),
    GetDocumentAsync: vi.fn(),
    UpdateDocumentFieldsAsync: vi.fn(),
    SaveDocumentAsync: vi.fn().mockResolvedValue([saveResult]),
  };
}

describe('MetasonicClient (SOAP mocked)', () => {
  beforeEach(() => {
    vi.mocked(soap.createClientAsync).mockReset();
  });

  it('connect calls LoginAsync and requires SessionID', async () => {
    const mockClient = mockSoapClient();
    vi.mocked(soap.createClientAsync).mockResolvedValue(
      mockClient as unknown as soap.Client
    );

    const metasonic = new MetasonicClient({
      environment: 'test',
      username: 'u',
      password: 'secret',
      database: 'test-db',
      endpoint: 'http://127.0.0.1/mock',
    });
    await metasonic.connect();

    expect(soap.createClientAsync).toHaveBeenCalled();
    expect(mockClient.LoginAsync).toHaveBeenCalled();
  });

  it('connect fails when no SessionID in response', async () => {
    const mockClient = {
      LoginAsync: vi
        .fn()
        .mockResolvedValue([{ loginData: { SessionID: undefined } }]),
    };
    vi.mocked(soap.createClientAsync).mockResolvedValue(
      mockClient as unknown as soap.Client
    );

    const metasonic = new MetasonicClient({
      environment: 'test',
      username: 'u',
      password: 'x',
      database: 'test-db',
      endpoint: 'http://127.0.0.1/mock',
    });
    await expect(metasonic.connect()).rejects.toThrow(
      /Authentication failed|no session ID/i
    );
  });

  it('saveDocument calls SaveDocumentAsync with the request document', async () => {
    const mockClient = mockSoapClient();
    vi.mocked(soap.createClientAsync).mockResolvedValue(
      mockClient as unknown as soap.Client
    );

    const metasonic = new MetasonicClient({
      environment: 'test',
      username: 'u',
      password: 'secret',
      database: 'test-db',
      endpoint: 'http://127.0.0.1/mock',
    });
    await metasonic.connect();

    const document: RequestDocument = {
      DocClassID: '1',
      FieldList: [
        { ID: '89900202', Value: 'INV-2026-001' },
        { ID: '89900207', Value: 'Test Supplier' },
      ],
    };

    const result = await metasonic.saveDocument(document);

    expect(result.ID).toBe('new-doc-42');
    expect(mockClient.SaveDocumentAsync).toHaveBeenCalledTimes(1);
    const call = mockClient.SaveDocumentAsync.mock.calls[0]![0] as {
      document: RequestDocument;
    };
    expect(call.document.DocClassID).toBe('1');
    expect(call.document.FieldList).toEqual(document.FieldList);
  });
});
