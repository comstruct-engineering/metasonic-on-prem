/**
 * TypeScript interfaces for Metasonic Scanview SOAP service
 */

export interface LoginData {
  Username: string;
  Password: string;
  NoSession: boolean;
  AutoLogin: boolean;
  UseSSO: boolean;
  LoginDatabaseName: string;
  SessionID?: string;
  LoginConfigurationName?: string;
  Language?: string;
}

export interface RequestField {
  ID: string;
  Value: string;
}

export interface ResponseField {
  ID: string;
  Name?: string;
  Value: string;
  DataType?: string;
}

export interface ResponseDocument {
  ID: string;
  DocClassID?: string;
  MainUserFolderID?: string;
  FieldList?: ResponseField[];
  ItemList?: ResponseItem[];
}

export interface ResponseItem {
  ItemData?: Buffer;
  ItemName?: string;
  ItemTypeNumber?: number;
  OriginalFileName?: string;
}

export interface RequestDocument {
  ID?: string;
  DocClassID: string;
  MainUserFolderID?: string;
  FieldList: RequestField[];
  ItemList?: RequestItem[];
}

export interface RequestItem {
  /** Base64-encoded bytes for SOAP `xsd:base64Binary`. Raw `Buffer` must not be passed: node-soap would serialize it incorrectly and break XML. */
  ItemData: string;
  ItemName: string;
  ItemTypeNumber: number;
  OriginalFileName: string;
}

export interface MetasonicConfig {
  environment: 'test' | 'live';
  username: string;
  password: string;
  database: string;
  endpoint?: string;
  /**
   * Qualifier ID on the DMS document storing the Comstruct invoice id (for `Search` idempotency).
   * Set via `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID`.
   */
  comstructInvoiceQualifierId?: string;
  /**
   * User folder scope for `Search` (often `""`). `METASONIC_SEARCH_USER_FOLDER_ID`.
   */
  metasonicSearchUserFolderId?: string;
}

/** SOAP `RequestSearchOperators` (subset used by the worker). */
export interface RequestSearchOperatorsInput {
  MaxResults?: number;
  OrSearch?: boolean;
  NoLikeSearch?: boolean;
  RecursiveDocClassSearch?: boolean;
  RecursiveUserFolderSearch?: boolean;
}

export interface ScanviewWebservice {
  Login(args: { loginData: LoginData }): Promise<{ loginData: LoginData }>;
  GetDocument(args: {
    loginData: LoginData;
    documentId: string;
    p1: boolean;
    p2: boolean;
    includeFields: boolean;
    p4: boolean;
  }): Promise<{ GetDocumentResult: ResponseDocument; loginData: LoginData }>;
  UpdateDocumentFields(args: {
    loginData: LoginData;
    documentID: string;
    fields: RequestField[];
  }): Promise<{ loginData: LoginData }>;
  SaveDocument(args: {
    loginData: LoginData;
    document: RequestDocument;
  }): Promise<{ SaveDocumentResult: ResponseDocument; loginData: LoginData }>;
}

// Qualifier constants
export const QUALIFIERS = {
  KREDITOR: '89900207',
  BELEGNR: '89900202',
  BETRAG: '89900131',
  SACHLICH: '223',
  PREISLICH: '222',
  IS_SPLIT: '234',
} as const;

// Qualifier names mapping
export const QUALIFIER_NAMES: Record<string, string> = {
  [QUALIFIERS.KREDITOR]: 'Kreditor',
  [QUALIFIERS.BELEGNR]: 'Belegnummer',
  [QUALIFIERS.BETRAG]: 'Betrag',
  [QUALIFIERS.SACHLICH]: 'Sachlich',
  [QUALIFIERS.PREISLICH]: 'Preislich',
  [QUALIFIERS.IS_SPLIT]: 'IsSplit',
};

// Friendly name to ID mapping
export const QUALIFIER_ID_MAP: Record<string, string> = {
  kreditor: QUALIFIERS.KREDITOR,
  belegnr: QUALIFIERS.BELEGNR,
  betrag: QUALIFIERS.BETRAG,
  sachlich: QUALIFIERS.SACHLICH,
  preislich: QUALIFIERS.PREISLICH,
  issplit: QUALIFIERS.IS_SPLIT,
};
