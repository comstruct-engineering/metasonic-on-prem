/**
 * In-process mock of Metasonic Scanview SOAP (uses the real WSDL for wire compatibility).
 * For local use without VPN. See README "Metasonic mock server".
 */

import * as http from 'http';
import * as fs from 'fs';
import * as soap from 'soap';
import type { IServices } from 'soap';
import { resolveScanviewWsdlPath } from './resolve-wsdl-path';

const TNS = 'http://www.allgeier.com/scanview';

/**
 * The bundled WSDL is huge; `node-soap` object serialization can yield an empty body.
 * Returning `_xml` is supported and matches what the client parses.
 */
function loginResponseXml(): { _xml: string } {
  return {
    _xml: `<LoginResponse xmlns="${TNS}">
  <LoginResult>true</LoginResult>
  <loginData>
    <SessionID>mock-session</SessionID>
    <Username>mock-user</Username>
    <Password></Password>
    <NoSession>true</NoSession>
    <AutoLogin>true</AutoLogin>
    <UseSSO>false</UseSSO>
    <LoginDatabaseName>MOCK_DB</LoginDatabaseName>
  </loginData>
</LoginResponse>`,
  };
}

type FieldMap = Map<string, string>;

function defaultFieldsForDocument(): FieldMap {
  return new Map<string, string>([
    ['89900202', 'MOCK-INV-001'],
    ['223', 'richtig'],
  ]);
}

/**
 * In-memory per-server store so `update` / `SaveDocument` / `Search` can be proven against the mock.
 */
function createFieldStore(): {
  getOrCreate: (docId: string) => FieldMap;
  buildFieldListXml: (docId: string) => string;
  findDocIdsByField: (qualifierId: string, value: string) => string[];
  nextNewDocId: () => string;
} {
  const byDoc = new Map<string, FieldMap>();
  let seq = 1;

  function getOrCreate(docId: string): FieldMap {
    if (!byDoc.has(docId)) {
      byDoc.set(docId, defaultFieldsForDocument());
    }
    return byDoc.get(docId)!;
  }

  function buildFieldListXml(documentId: string): string {
    const m = getOrCreate(documentId);
    const parts: string[] = [];
    for (const [id, value] of m) {
      parts.push(
        `      <ResponseDocumentField>
        <ID>${xmlEscape(id)}</ID>
        <Value>${xmlEscape(value)}</Value>
      </ResponseDocumentField>`
      );
    }
    return parts.join('\n');
  }

  function findDocIdsByField(qualifierId: string, value: string): string[] {
    const out: string[] = [];
    for (const [docId, m] of byDoc) {
      if (m.get(qualifierId) === value) {
        out.push(docId);
      }
    }
    return out;
  }

  function nextNewDocId(): string {
    return `mock-doc-${seq++}`;
  }

  return { getOrCreate, buildFieldListXml, findDocIdsByField, nextNewDocId };
}

function getDocumentResponseXml(
  documentId: string,
  buildFieldListXml: (docId: string) => string
): { _xml: string } {
  return {
    _xml: `<GetDocumentResponse xmlns="${TNS}">
  <GetDocumentResult>
    <ID>${xmlEscape(documentId)}</ID>
    <DocClassID>1</DocClassID>
    <FieldList>
${buildFieldListXml(documentId)}
    </FieldList>
  </GetDocumentResult>
  <loginData>
    <SessionID>mock-session</SessionID>
    <Username>mock-user</Username>
    <NoSession>true</NoSession>
    <AutoLogin>true</AutoLogin>
    <UseSSO>false</UseSSO>
    <LoginDatabaseName>MOCK_DB</LoginDatabaseName>
  </loginData>
</GetDocumentResponse>`,
  };
}

function updateFieldsResponseXml(): { _xml: string } {
  return {
    _xml: `<UpdateDocumentFieldsResponse xmlns="${TNS}">
  <loginData>
    <SessionID>mock-session</SessionID>
    <Username>mock-user</Username>
    <NoSession>true</NoSession>
    <AutoLogin>true</AutoLogin>
    <UseSSO>false</UseSSO>
    <LoginDatabaseName>MOCK_DB</LoginDatabaseName>
  </loginData>
</UpdateDocumentFieldsResponse>`,
  };
}

function saveDocumentResponseXml(id: string): { _xml: string } {
  return {
    _xml: `<SaveDocumentResponse xmlns="${TNS}">
  <SaveDocumentResult>
    <ID>${xmlEscape(id)}</ID>
    <DocClassID>1</DocClassID>
  </SaveDocumentResult>
  <loginData>
    <SessionID>mock-session</SessionID>
    <Username>mock-user</Username>
    <NoSession>true</NoSession>
    <AutoLogin>true</AutoLogin>
    <UseSSO>false</UseSSO>
    <LoginDatabaseName>MOCK_DB</LoginDatabaseName>
  </loginData>
</SaveDocumentResponse>`,
  };
}

function searchResponseXml(
  docIds: string[],
  buildFieldListXml: (docId: string) => string
): { _xml: string } {
  const inner =
    docIds.length === 0
      ? ''
      : docIds
          .map(
            id => `  <ResponseDocument>
    <ID>${xmlEscape(id)}</ID>
    <DocClassID>1</DocClassID>
    <FieldList>
${buildFieldListXml(id)}
    </FieldList>
  </ResponseDocument>`
          )
          .join('\n');
  return {
    _xml: `<SearchResponse xmlns="${TNS}">
  <SearchResult>
${inner}
  </SearchResult>
  <loginData>
    <SessionID>mock-session</SessionID>
    <Username>mock-user</Username>
    <NoSession>true</NoSession>
    <AutoLogin>true</AutoLogin>
    <UseSSO>false</UseSSO>
    <LoginDatabaseName>MOCK_DB</LoginDatabaseName>
  </loginData>
</SearchResponse>`,
  };
}

function xmlEscape(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function parseRequestFieldRows(input: unknown): { ID: string; Value: string }[] {
  if (input == null) {
    return [];
  }
  if (Array.isArray(input)) {
    return input.map(x => {
      const o = x as { ID?: string; Value?: string };
      return { ID: String(o.ID), Value: String(o.Value ?? '') };
    });
  }
  if (typeof input === 'object') {
    const o = input as Record<string, unknown>;
    if (o.ID != null && !Array.isArray(o)) {
      return [{ ID: String(o.ID), Value: String(o.Value ?? '') }];
    }
    if (o.RequestField != null) {
      return parseRequestFieldRows(
        Array.isArray(o.RequestField) ? o.RequestField : [o.RequestField]
      );
    }
  }
  return [];
}

function mergeFieldRows(store: FieldMap, rows: { ID: string; Value: string }[]): void {
  for (const row of rows) {
    if (row.ID) {
      store.set(row.ID, row.Value);
    }
  }
}

export function createScanviewMockServices(): IServices {
  const { getOrCreate, buildFieldListXml, findDocIdsByField, nextNewDocId } =
    createFieldStore();

  return {
    ScanviewWebservice: {
      ScanviewWebserviceBasicHTTP: {
        Login: function () {
          return loginResponseXml();
        },
        Search: function (args: { searchFields?: unknown }) {
          const rows = parseRequestFieldRows(args.searchFields);
          if (rows.length === 0) {
            return searchResponseXml([], buildFieldListXml);
          }
          const { ID: qid, Value: qval } = rows[0];
          const ids = findDocIdsByField(qid, qval);
          return searchResponseXml(ids.slice(0, 25), buildFieldListXml);
        },
        GetDocument: function (args: { documentId?: string; documentID?: string }) {
          const id = String(args.documentId ?? args.documentID ?? '');
          return getDocumentResponseXml(id, buildFieldListXml);
        },
        UpdateDocumentFields: function (args: {
          documentId?: string;
          documentID?: string;
          fields?: unknown;
          requestFields?: unknown;
        }) {
          const id = String(args.documentId ?? args.documentID ?? '');
          const rows = parseRequestFieldRows(args.requestFields ?? args.fields);
          mergeFieldRows(getOrCreate(id), rows);
          return updateFieldsResponseXml();
        },
        SaveDocument: function (args: {
          document?: { ID?: string; FieldList?: unknown };
        }) {
          const raw = args.document?.ID;
          const id =
            raw != null && String(raw).trim() !== ''
              ? String(raw)
              : nextNewDocId();
          const rows = parseRequestFieldRows(args.document?.FieldList);
          mergeFieldRows(getOrCreate(id), rows);
          return saveDocumentResponseXml(id);
        },
      },
    },
  } as IServices;
}

/** Path must match a port in `ScanviewWebservice1.wsdl` (BasicHTTP uses `/ScanviewWCF/...`). */
export const SCANVIEW_MOCK_SVC_PATH = '/ScanviewWCF/ScanviewWebservice.svc';

export interface ScanviewMockServerHandle {
  /** Base URL e.g. http://127.0.0.1:19089 (no trailing slash) */
  baseUrl: string;
  port: number;
  close: () => Promise<void>;
}

/**
 * Binds a Scanview mock (use port `0` for a free port — e.g. in tests).
 */
export function startScanviewMockServer(
  port: number = process.env.SCANVIEW_MOCK_PORT
    ? Number(process.env.SCANVIEW_MOCK_PORT)
    : 19089
): Promise<ScanviewMockServerHandle> {
  const xml = fs.readFileSync(resolveScanviewWsdlPath(), 'utf8');
  const services = createScanviewMockServices();
  const server = http.createServer();

  return new Promise((resolve, reject) => {
    soap.listen(
      server,
      SCANVIEW_MOCK_SVC_PATH,
      services,
      xml,
      (err: Error | null) => {
        if (err) {
          reject(err);
          return;
        }
        server.listen(port, '127.0.0.1', () => {
          const address = server.address();
          if (!address || typeof address === 'string') {
            reject(new Error('Could not get server address'));
            return;
          }
          const p = address.port;
          const baseUrl = `http://127.0.0.1:${p}`;
          resolve({
            baseUrl,
            port: p,
            close: () =>
              new Promise<void>((res, rej) => {
                server.close(e => (e ? rej(e) : res()));
              }),
          });
        });
      }
    );
  });
}

function main(): void {
  const p = Number(process.env.SCANVIEW_MOCK_PORT) || 19089;
  void startScanviewMockServer(p)
    .then(({ baseUrl }) => {
      // eslint-disable-next-line no-console
      console.log(`Scanview mock listening at ${baseUrl}${SCANVIEW_MOCK_SVC_PATH}`);
      // eslint-disable-next-line no-console
      console.log(
        'Example: METASONIC_ENDPOINT=' +
          baseUrl +
          `${SCANVIEW_MOCK_SVC_PATH} METASONIC_DOC_ID=1527018 npm run metasonic:smoke`
      );
    })
    .catch(err => {
      // eslint-disable-next-line no-console
      console.error(err instanceof Error ? err.message : String(err));
      process.exit(1);
    });
}

if (require.main === module) {
  main();
}
