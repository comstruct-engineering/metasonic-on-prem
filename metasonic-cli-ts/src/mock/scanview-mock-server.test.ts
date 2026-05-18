import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { normalizeResponseFieldList } from '../field-list';
import { MetasonicClient } from '../metasonic-client';
import {
  SCANVIEW_MOCK_SVC_PATH,
  startScanviewMockServer,
} from './scanview-mock-server';
import type { ScanviewMockServerHandle } from './scanview-mock-server';

function fieldListRowCount(
  fieldList: { ResponseDocumentField?: unknown[] } | unknown[] | undefined
): number {
  if (!fieldList) {
    return 0;
  }
  if (Array.isArray(fieldList)) {
    return fieldList.length;
  }
  if (
    typeof fieldList === 'object' &&
    fieldList !== null &&
    Array.isArray(
      (fieldList as { ResponseDocumentField?: unknown[] }).ResponseDocumentField
    )
  ) {
    return (fieldList as { ResponseDocumentField: unknown[] }).ResponseDocumentField
      .length;
  }
  return 0;
}

describe('Scanview mock server (integration)', () => {
  let handle: ScanviewMockServerHandle;

  beforeAll(async () => {
    handle = await startScanviewMockServer(0);
  }, 30_000);

  afterAll(async () => {
    await handle.close();
  });

  it('Login + GetDocument with MetasonicClient', async () => {
    const c = new MetasonicClient({
      environment: 'test',
      username: 'mock-user',
      password: 'any',
      database: 'MOCK_DB',
      endpoint: `${handle.baseUrl}${SCANVIEW_MOCK_SVC_PATH}`,
    });
    await c.connect();
    const doc = await c.getDocument('test-doc-1');
    expect(doc.ID).toBe('test-doc-1');
    expect(fieldListRowCount(doc.FieldList as never)).toBeGreaterThan(0);
    c.disconnect();
  });

  it('updateQualifier changes qualifier then visible in GetDocument', async () => {
    const c = new MetasonicClient({
      environment: 'test',
      username: 'mock-user',
      password: 'any',
      database: 'MOCK_DB',
      endpoint: `${handle.baseUrl}${SCANVIEW_MOCK_SVC_PATH}`,
    });
    await c.connect();
    const docId = 'doc-update-test';
    await c.updateQualifier(docId, '223', 'falsch');
    const after = await c.getDocument(docId);
    const fields = normalizeResponseFieldList(after.FieldList);
    expect(fields.find(f => f.ID === '223')?.Value).toBe('falsch');
    c.disconnect();
  });

  it('SaveDocument merges FieldList (write path; no binary ItemData in SOAP)', async () => {
    const c = new MetasonicClient({
      environment: 'test',
      username: 'mock-user',
      password: 'any',
      database: 'MOCK_DB',
      endpoint: `${handle.baseUrl}${SCANVIEW_MOCK_SVC_PATH}`,
    });
    await c.connect();
    const docId = 'doc-save-test';
    await c.saveDocument({
      ID: docId,
      DocClassID: '1',
      FieldList: [
        { ID: '89900202', Value: 'SAVE-OK' },
        { ID: '223', Value: 'ungeprüft' },
      ],
    });
    const d = await c.getDocument(docId);
    const fields = normalizeResponseFieldList(d.FieldList);
    expect(fields.find(f => f.ID === '89900202')?.Value).toBe('SAVE-OK');
    expect(fields.find(f => f.ID === '223')?.Value).toBe('ungeprüft');
    c.disconnect();
  });

  it('Search finds a document by correlation qualifier (idempotency)', async () => {
    const qual = '99900001';
    const c = new MetasonicClient({
      environment: 'test',
      username: 'mock-user',
      password: 'any',
      database: 'MOCK_DB',
      endpoint: `${handle.baseUrl}${SCANVIEW_MOCK_SVC_PATH}`,
      comstructInvoiceQualifierId: qual,
    });
    await c.connect();
    await c.saveDocument({
      DocClassID: '1',
      FieldList: [
        { ID: qual, Value: 'comstruct-999' },
        { ID: '89900202', Value: 'INV-X' },
      ],
    });
    const found = await c.findExistingDocumentByCorrelationValue('comstruct-999', '1');
    expect(found?.ID).toBeTruthy();
    const again = await c.searchDocuments({
      docClassId: '1',
      userFolderId: '',
      searchFields: [{ ID: qual, Value: 'comstruct-999' }],
    });
    expect(again.length).toBeGreaterThanOrEqual(1);
    c.disconnect();
  });

  it('updateDocumentFile sends base64 ItemData and completes SaveDocument round-trip', async () => {
    const minimalPdf = Buffer.from(
      '%PDF-1.0\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/MediaBox[0 0 2 2]>>endobj\nxref\n0 0\ntrailer<</Root 1 0 R>>\n%%EOF\n',
      'utf8'
    );
    const tmp = path.join(os.tmpdir(), `scanview-mock-upload-${Date.now()}.pdf`);
    fs.writeFileSync(tmp, minimalPdf);

    const c = new MetasonicClient({
      environment: 'test',
      username: 'mock-user',
      password: 'any',
      database: 'MOCK_DB',
      endpoint: `${handle.baseUrl}${SCANVIEW_MOCK_SVC_PATH}`,
    });
    await c.connect();
    const docId = 'doc-upload-pdf-test';
    const saved = await c.updateDocumentFile(docId, tmp);
    expect(saved.ID).toBe(docId);
    const after = await c.getDocument(docId);
    expect(after.ID).toBe(docId);
    c.disconnect();
    fs.unlinkSync(tmp);
  });
});
