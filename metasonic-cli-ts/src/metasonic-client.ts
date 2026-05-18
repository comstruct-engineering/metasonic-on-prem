import * as soap from 'soap';
import * as fs from 'fs';
import * as path from 'path';
import { normalizeResponseFieldList } from './field-list';
import { normalizeSearchDocumentsResult } from './search-result';
import {
  LoginData,
  RequestField,
  RequestItem,
  RequestDocument,
  ResponseDocument,
  MetasonicConfig,
  RequestSearchOperatorsInput,
} from './types';

export class MetasonicClient {
  private client: soap.Client | null = null;
  private loginData: LoginData | null = null;
  private config: MetasonicConfig;

  /** Non-identifying localhost default only when METASONIC_ENDPOINT / config.endpoint unset. */
  private static readonly PLACEHOLDER_ENDPOINT =
    'http://127.0.0.1:8089/ScanviewWebservice.svc';

  constructor(config: Partial<MetasonicConfig> = {}) {
    const env = config.environment || 'test';

    this.config = {
      environment: env,
      username: config.username ?? process.env.METASONIC_USER ?? '',
      password: config.password ?? process.env.METASONIC_PASSWORD ?? '',
      database: config.database ?? process.env.METASONIC_DB ?? '',
      endpoint:
        config.endpoint ??
        process.env.METASONIC_ENDPOINT ??
        MetasonicClient.PLACEHOLDER_ENDPOINT,
      comstructInvoiceQualifierId:
        config.comstructInvoiceQualifierId ??
        process.env.METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID,
      metasonicSearchUserFolderId:
        config.metasonicSearchUserFolderId ??
        process.env.METASONIC_SEARCH_USER_FOLDER_ID ??
        '',
    };
  }

  /**
   * Connect and authenticate to Metasonic DMS
   */
  async connect(): Promise<void> {
    try {
      if (
        !(this.config.username ?? '').trim() ||
        !(this.config.password ?? '').trim() ||
        !(this.config.database ?? '').trim()
      ) {
        throw new Error(
          'Missing MetaSonic credentials. Set METASONIC_USER, METASONIC_PASSWORD, METASONIC_DB (or CLI --user/--pass/--db).'
        );
      }
      // Add WSDL suffix for SOAP endpoint
      const wsdlUrl = `${this.config.endpoint}?wsdl`;

      // Create SOAP client
      this.client = await soap.createClientAsync(wsdlUrl, {
        disableCache: true,
        endpoint: this.config.endpoint,
      });

      // Authenticate
      this.loginData = {
        Username: this.config.username,
        Password: this.config.password,
        NoSession: true,
        AutoLogin: true,
        UseSSO: false,
        LoginDatabaseName: this.config.database,
      };

      const result = await this.client.LoginAsync({ loginData: this.loginData });

      if (result && result[0] && result[0].loginData) {
        this.loginData = result[0].loginData;
      }

      if (!this.loginData?.SessionID) {
        throw new Error('Authentication failed - no session ID returned');
      }
    } catch (error) {
      throw new Error(
        `Failed to connect to Metasonic DMS: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Get document with all qualifiers
   */
  async getDocument(docId: string): Promise<ResponseDocument> {
    this.ensureConnected();

    try {
      const result = await this.client!.GetDocumentAsync({
        loginData: this.loginData!,
        documentId: docId,
        p1: false,
        p2: false,
        includeFields: true,
        p4: false,
      });

      if (result && result[0]) {
        // Update login data for session continuity
        if (result[0].loginData) {
          this.loginData = result[0].loginData;
        }

        return result[0].GetDocumentResult;
      }

      throw new Error('No document data returned');
    } catch (error) {
      throw new Error(
        `Failed to get document ${docId}: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * WCF `Search`: find documents by qualifier values (see WSDL `Search` / `RequestField[] searchFields`).
   */
  async searchDocuments(params: {
    docClassId: string;
    userFolderId: string;
    searchFields: RequestField[];
    fulltextSearchValue?: string;
    searchOperators?: RequestSearchOperatorsInput;
  }): Promise<ResponseDocument[]> {
    this.ensureConnected();

    const searchOperators = {
      MaxResults: params.searchOperators?.MaxResults ?? 25,
      OrSearch: params.searchOperators?.OrSearch ?? false,
      NoLikeSearch: params.searchOperators?.NoLikeSearch ?? true,
      RecursiveDocClassSearch:
        params.searchOperators?.RecursiveDocClassSearch ?? false,
      RecursiveUserFolderSearch:
        params.searchOperators?.RecursiveUserFolderSearch ?? false,
    };

    try {
      const client = this.client as soap.Client & {
        SearchAsync?: (args: Record<string, unknown>) => Promise<unknown[]>;
      };
      if (typeof client.SearchAsync !== 'function') {
        throw new Error('SOAP client has no SearchAsync (check WSDL / endpoint)');
      }
      const result = await client.SearchAsync({
        loginData: this.loginData!,
        docClassID: params.docClassId,
        userFolderID: params.userFolderId ?? '',
        fulltextSearchValue: params.fulltextSearchValue ?? '',
        searchFields: { RequestField: params.searchFields },
        searchOperators,
        loadItemList: false,
        smallDocument: true,
        smallFieldList: true,
      });

      if (result && result[0]) {
        const row = result[0] as { loginData?: LoginData; SearchResult?: unknown };
        if (row.loginData) {
          this.loginData = row.loginData;
        }
        return normalizeSearchDocumentsResult(row);
      }

      return [];
    } catch (error) {
      throw new Error(
        `Search failed: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Idempotency: `Search` for a DMS document whose correlation qualifier equals this value
   * (typically Comstruct `external_id`, or `id` if `METASONIC_DMS_CORRELATION_KEY=id`).
   * Requires `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` (or config) and a real `Search` on the server.
   */
  async findExistingDocumentByCorrelationValue(
    correlationValue: string,
    docClassId?: string
  ): Promise<ResponseDocument | null> {
    const qid = this.config.comstructInvoiceQualifierId?.trim();
    if (!qid) {
      return null;
    }
    const cls = docClassId ?? (process.env.METASONIC_DOC_CLASS_ID || '1');
    const folder = this.config.metasonicSearchUserFolderId ?? '';
    const hits = await this.searchDocuments({
      docClassId: cls,
      userFolderId: folder,
      searchFields: [{ ID: qid, Value: correlationValue }],
      searchOperators: { MaxResults: 5, NoLikeSearch: true },
    });
    if (hits.length > 1) {
      // eslint-disable-next-line no-console
      console.warn(
        `Metasonic Search: ${hits.length} documents match correlation value ${correlationValue}; using the first`
      );
    }
    return hits[0] ?? null;
  }

  /**
   * @deprecated Use `findExistingDocumentByCorrelationValue` — same behavior; name kept for older callers.
   */
  async findExistingDocumentByComstructInvoiceId(
    valueForSearch: string,
    docClassId?: string
  ): Promise<ResponseDocument | null> {
    return this.findExistingDocumentByCorrelationValue(valueForSearch, docClassId);
  }

  /**
   * Update document qualifiers
   */
  async updateDocumentFields(
    docId: string,
    fields: RequestField[]
  ): Promise<void> {
    this.ensureConnected();

    try {
      const result = await this.client!.UpdateDocumentFieldsAsync({
        loginData: this.loginData!,
        documentId: docId,
        requestFields: { RequestField: fields },
      });

      if (result && result[0] && result[0].loginData) {
        this.loginData = result[0].loginData;
      }
    } catch (error) {
      throw new Error(
        `Failed to update document fields: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Update a single qualifier
   */
  async updateQualifier(
    docId: string,
    qualifierId: string,
    value: string
  ): Promise<void> {
    return this.updateDocumentFields(docId, [{ ID: qualifierId, Value: value }]);
  }

  /**
   * Replace the document PDF without changing qualifiers unless `metadata` is passed (then merge over existing).
   */
  async updateDocumentPdfBuffer(
    docId: string,
    pdfBuffer: Buffer,
    originalFileName: string,
    metadata?: RequestField[]
  ): Promise<ResponseDocument> {
    this.ensureConnected();

    const currentDoc = await this.getDocument(docId);
    const itemTypeNumber = this.getItemTypeNumber(originalFileName);
    const item: RequestItem = {
      ItemData: pdfBuffer.toString('base64'),
      ItemName: 'Document',
      OriginalFileName: originalFileName,
      ItemTypeNumber: itemTypeNumber,
    };

    const existingFields = normalizeResponseFieldList(currentDoc.FieldList);
    const mergedFields = this.mergeFields(existingFields, metadata ?? []);

    const document: RequestDocument = {
      ID: docId,
      DocClassID: currentDoc.DocClassID || '1',
      MainUserFolderID: currentDoc.MainUserFolderID,
      FieldList: mergedFields,
      ItemList: [item],
    };

    return this.saveDocument(document);
  }

  /**
   * Insert a new PDF at position 0 (first) in an existing MetaSonic document.
   * Fetches the current document (with item data), then calls `SaveDocument`
   * with the new PDF prepended to the existing item list.
   * Existing fields and all prior attachments are preserved in their original order after the new PDF.
   */
  async prependDocumentPdfItem(
    docId: string,
    pdfBuffer: Buffer,
    originalFileName: string
  ): Promise<void> {
    this.ensureConnected();

    const existingDoc = await this.getDocumentWithItems(docId);

    const newPdf: RequestItem = {
      ItemData: pdfBuffer.toString('base64'),
      ItemName: 'Document',
      OriginalFileName: originalFileName,
      ItemTypeNumber: this.getItemTypeNumber(originalFileName),
    };

    const existingItems: RequestItem[] = (existingDoc.ItemList ?? []).map(ri => ({
      ItemData: Buffer.isBuffer(ri.ItemData)
        ? (ri.ItemData as Buffer).toString('base64')
        : String(ri.ItemData ?? ''),
      ItemName: ri.ItemName ?? 'Document',
      OriginalFileName: ri.OriginalFileName ?? '',
      ItemTypeNumber: this.getItemTypeNumber(ri.OriginalFileName ?? ''),
    }));

    const existingFields = normalizeResponseFieldList(existingDoc.FieldList);

    const document: RequestDocument = {
      ID: docId,
      DocClassID: existingDoc.DocClassID ?? '1',
      MainUserFolderID: existingDoc.MainUserFolderID,
      FieldList: existingFields,
      ItemList: [newPdf, ...existingItems],
    };

    await this.saveDocument(document);
  }

  /**
   * Append a PDF after existing items on a MetaSonic document.
   * Same as {@link prependDocumentPdfItem} but adds the new PDF at the end of the item list.
   */
  async appendDocumentPdfItem(
    docId: string,
    pdfBuffer: Buffer,
    originalFileName: string
  ): Promise<void> {
    this.ensureConnected();

    const existingDoc = await this.getDocumentWithItems(docId);

    const newPdf: RequestItem = {
      ItemData: pdfBuffer.toString('base64'),
      ItemName: 'Document',
      OriginalFileName: originalFileName,
      ItemTypeNumber: this.getItemTypeNumber(originalFileName),
    };

    const existingItems: RequestItem[] = (existingDoc.ItemList ?? []).map(ri => ({
      ItemData: Buffer.isBuffer(ri.ItemData)
        ? (ri.ItemData as Buffer).toString('base64')
        : String(ri.ItemData ?? ''),
      ItemName: ri.ItemName ?? 'Document',
      OriginalFileName: ri.OriginalFileName ?? '',
      ItemTypeNumber: this.getItemTypeNumber(ri.OriginalFileName ?? ''),
    }));

    const existingFields = normalizeResponseFieldList(existingDoc.FieldList);

    const document: RequestDocument = {
      ID: docId,
      DocClassID: existingDoc.DocClassID ?? '1',
      MainUserFolderID: existingDoc.MainUserFolderID,
      FieldList: existingFields,
      ItemList: [...existingItems, newPdf],
    };

    await this.saveDocument(document);
  }

  /** Fetch an existing document including full binary item data. */
  private async getDocumentWithItems(docId: string): Promise<ResponseDocument> {
    this.ensureConnected();

    try {
      const result = await this.client!.GetDocumentAsync({
        loginData: this.loginData!,
        documentId: docId,
        p1: true,
        p2: false,
        includeFields: true,
        p4: false,
      });

      if (result && result[0]) {
        if (result[0].loginData) {
          this.loginData = result[0].loginData;
        }
        return result[0].GetDocumentResult;
      }

      throw new Error('No document data returned');
    } catch (error) {
      throw new Error(
        `Failed to get document with items ${docId}: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Update document with new file (replaces existing file)
   */
  async updateDocumentFile(
    docId: string,
    filePath: string,
    metadata?: RequestField[]
  ): Promise<ResponseDocument> {
    this.ensureConnected();
    const fileBuffer = fs.readFileSync(filePath);
    const fileName = path.basename(filePath);
    return this.updateDocumentPdfBuffer(docId, fileBuffer, fileName, metadata);
  }

  /**
   * Save document (create new or update existing)
   */
  async saveDocument(document: RequestDocument): Promise<ResponseDocument> {
    this.ensureConnected();

    try {
      const result = await this.client!.SaveDocumentAsync({
        loginData: this.loginData!,
        document: document,
      });

      if (result && result[0]) {
        // Update login data
        if (result[0].loginData) {
          this.loginData = result[0].loginData;
        }

        return result[0].SaveDocumentResult;
      }

      throw new Error('No response from SaveDocument');
    } catch (error) {
      throw new Error(
        `Failed to save document: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Merge field lists, preferring new values over existing
   */
  private mergeFields(
    existingFields: { ID: string; Value?: string }[],
    newFields: RequestField[]
  ): RequestField[] {
    const fieldMap = new Map<string, string>();

    // Add existing fields
    for (const field of existingFields) {
      if (field.Value) {
        fieldMap.set(field.ID, field.Value);
      }
    }

    // Override with new fields
    for (const field of newFields) {
      fieldMap.set(field.ID, field.Value);
    }

    // Convert back to array
    return Array.from(fieldMap.entries()).map(([ID, Value]) => ({ ID, Value }));
  }

  /**
   * Determine item type number based on file extension
   */
  private getItemTypeNumber(fileName: string): number {
    const ext = path.extname(fileName).toLowerCase();
    const typeMap: Record<string, number> = {
      '.pdf': 1,
      '.tif': 2,
      '.tiff': 2,
      '.jpg': 3,
      '.jpeg': 3,
      '.png': 4,
    };
    return typeMap[ext] || 1; // Default to PDF
  }

  /**
   * Close connection
   */
  disconnect(): void {
    this.client = null;
    this.loginData = null;
  }

  private ensureConnected(): void {
    if (!this.client || !this.loginData) {
      throw new Error(
        'Not connected to Metasonic DMS. Call connect() first.'
      );
    }
  }

  /**
   * Get current endpoint
   */
  getEndpoint(): string {
    return this.config.endpoint!;
  }

  /**
   * Get current environment
   */
  getEnvironment(): string {
    return this.config.environment;
  }
}
