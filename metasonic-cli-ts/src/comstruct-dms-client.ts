import * as http from 'node:http';
import * as https from 'node:https';
import type {
  ComstructInvoicesListResponse,
  PatchInvoiceDmsBody,
} from './comstruct-invoice';

export interface ComstructDmsClientConfig {
  baseUrl: string;
  apiPrefix: string;
  apiKey: string;
  /**
   * Optional `status` filter sent to the list endpoint. Leave empty to rely solely on
   * server-side `external_status` filtering (recommended when the backend filters that way).
   */
  queueStatus: string;
  listLimit: number;
  /** Query param name for watermark filter (server must implement) */
  updatedAfterParam: string;
  /** PATCH path pattern; `{id}` replaced with invoice id */
  patchPathTemplate: string;
  /**
   * When true (default), requests `GET .../pdf?annotations=true` per Comstruct contract.
   * Set env `COMSTRUCT_PDF_ANNOTATIONS=0` or `false` to omit the parameter (plain PDF).
   */
  pdfIncludeAnnotations: boolean;
  /**
   * When true (default), requests `GET .../pdf?overlay=true` so match-status boxes are embedded in the PDF.
   * Set env `COMSTRUCT_PDF_OVERLAY=0` or `false` to omit the parameter.
   */
  pdfIncludeOverlay: boolean;
}

function pdfIncludeAnnotationsFromEnv(): boolean {
  const v = process.env.COMSTRUCT_PDF_ANNOTATIONS;
  if (v === '0' || v?.toLowerCase() === 'false') {
    return false;
  }
  return true;
}

function pdfIncludeOverlayFromEnv(): boolean {
  const v = process.env.COMSTRUCT_PDF_OVERLAY;
  if (v === '0' || v?.toLowerCase() === 'false') {
    return false;
  }
  return true;
}

function parseConfigFromEnv(): ComstructDmsClientConfig {
  const baseUrl =
    process.env.COMSTRUCT_NEST_URL?.replace(/\/$/, '') || 'https://api.comstruct.com';
  return {
    baseUrl,
    apiPrefix: (process.env.COMSTRUCT_API_PREFIX || 'v1').replace(/^\/|\/$/g, ''),
    apiKey: process.env.COMSTRUCT_API_KEY ?? '',
    queueStatus: process.env.COMSTRUCT_DMS_QUEUE_STATUS || 'PRICE_CHECKING',
    listLimit: Math.min(
      500,
      Math.max(1, Number(process.env.COMSTRUCT_DMS_LIMIT || '50') || 50)
    ),
    updatedAfterParam:
      process.env.COMSTRUCT_DMS_UPDATED_AFTER_PARAM || 'updated_after',
    patchPathTemplate:
      process.env.COMSTRUCT_DMS_PATCH_PATH ||
      '/{prefix}/invoices/{id}/external-status',
    pdfIncludeAnnotations: pdfIncludeAnnotationsFromEnv(),
    pdfIncludeOverlay: pdfIncludeOverlayFromEnv(),
  };
}

function applyPathTemplate(
  template: string,
  prefix: string,
  invoiceId: string
): string {
  return template
    .replace(/\{prefix\}/g, prefix)
    .replace(/\{id\}/g, encodeURIComponent(invoiceId));
}

function invoicesBasePath(apiPrefix: string): string {
  const p = apiPrefix.trim();
  if (!p) {
    return '/invoices';
  }
  return `/${p.replace(/^\/|\/$/g, '')}/invoices`;
}

export function loadComstructDmsConfig(
  overrides?: Partial<ComstructDmsClientConfig>
): ComstructDmsClientConfig {
  return { ...parseConfigFromEnv(), ...overrides };
}

function requestBuffer(
  urlString: string,
  method: string,
  headers: Record<string, string>,
  body?: string
): Promise<{ status: number; headers: http.IncomingHttpHeaders; body: Buffer }> {
  return new Promise((resolve, reject) => {
    const u = new URL(urlString);
    const isHttps = u.protocol === 'https:';
    const lib = isHttps ? https : http;
    const port = u.port || (isHttps ? 443 : 80);
    const req = lib.request(
      {
        hostname: u.hostname,
        port,
        path: `${u.pathname}${u.search}`,
        method,
        headers: {
          ...headers,
          ...(body
            ? { 'Content-Length': String(Buffer.byteLength(body, 'utf8')) }
            : {}),
        },
      },
      res => {
        const chunks: Buffer[] = [];
        res.on('data', (c: Buffer) => chunks.push(c));
        res.on('end', () => {
          resolve({
            status: res.statusCode ?? 0,
            headers: res.headers,
            body: Buffer.concat(chunks),
          });
        });
      }
    );
    req.on('error', reject);
    if (body) {
      req.write(body, 'utf8');
    }
    req.end();
  });
}

async function requestJson<T>(
  urlString: string,
  method: string,
  headers: Record<string, string>,
  body?: object
): Promise<{ status: number; json: T | null; text: string }> {
  const payload = body ? JSON.stringify(body) : undefined;
  const { status, body: buf } = await requestBuffer(
    urlString,
    method,
    {
      ...headers,
      Accept: 'application/json',
      ...(payload ? { 'Content-Type': 'application/json' } : {}),
    },
    payload
  );
  const text = buf.toString('utf8');
  if (!text) {
    return { status, json: null, text: '' };
  }
  try {
    return { status, json: JSON.parse(text) as T, text };
  } catch {
    return { status, json: null, text };
  }
}

export class ComstructDmsClient {
  constructor(private readonly cfg: ComstructDmsClientConfig) {}

  private authHeaders(): Record<string, string> {
    return {
      'x-api-key': this.cfg.apiKey,
      Accept: 'application/json',
    };
  }

  /**
   * Lists invoices eligible for DMS upload.
   * Server should return only rows where `external_status` is not `SYNCED`.
   * An optional `status` filter is sent when `COMSTRUCT_DMS_QUEUE_STATUS` is set.
   */
  async listInvoicesForDmsUpload(
    updatedAfter?: string
  ): Promise<ComstructInvoicesListResponse> {
    const pathBase = invoicesBasePath(this.cfg.apiPrefix);
    const q = new URLSearchParams();
    if (this.cfg.queueStatus) {
      q.set('status', this.cfg.queueStatus);
    }
    q.set('limit', String(this.cfg.listLimit));
    q.set('offset', '0');
    if (updatedAfter) {
      q.set(this.cfg.updatedAfterParam, updatedAfter);
    }
    const url = `${this.cfg.baseUrl}${pathBase}?${q.toString()}`;
    const { status, json } = await requestJson<ComstructInvoicesListResponse>(
      url,
      'GET',
      this.authHeaders()
    );
    if (status < 200 || status >= 300 || !json) {
      throw new Error(
        `Comstruct list invoices failed: HTTP ${status} ${url} ${json === null ? '' : JSON.stringify(json)}`
      );
    }
    if (!Array.isArray(json.data)) {
      throw new Error('Comstruct list invoices: expected { data: [] }');
    }
    return json;
  }

  /**
   * Downloads the invoice PDF from Comstruct.
   * When `annotations=true` the endpoint returns `application/json` containing
   * `{ pdf_base64: string, ... }`; we decode the base64 field back to raw bytes.
   * Without annotations the endpoint returns raw `application/pdf` bytes directly.
   * Returns null when the PDF is not yet available (HTTP 404 or empty body).
   */
  async downloadInvoicePdf(invoiceId: string): Promise<Buffer | null> {
    const path = `${this.cfg.baseUrl}${invoicesBasePath(this.cfg.apiPrefix)}/${encodeURIComponent(invoiceId)}/pdf`;
    const url = new URL(path);
    if (this.cfg.pdfIncludeAnnotations) {
      url.searchParams.set('annotations', 'true');
    }
    if (this.cfg.pdfIncludeOverlay) {
      url.searchParams.set('overlay', 'true');
    }
    const { status, headers, body } = await requestBuffer(url.toString(), 'GET', {
      ...this.authHeaders(),
      Accept: 'application/pdf, application/json',
    });
    if (status === 404) {
      return null;
    }
    if (status < 200 || status >= 300) {
      throw new Error(
        `Comstruct PDF download failed: HTTP ${status} for invoice ${invoiceId}`
      );
    }
    if (!body || body.length === 0) {
      return null;
    }
    const contentType = String(headers['content-type'] ?? '');
    if (contentType.includes('json')) {
      const payload = JSON.parse(body.toString('utf8')) as {
        pdf_base64?: string;
      };
      if (!payload.pdf_base64) {
        return null;
      }
      return Buffer.from(payload.pdf_base64, 'base64');
    }
    return body;
  }

  /**
   * Reports DMS lifecycle state back to Comstruct. Endpoint is assumed (see README).
   */
  async patchInvoiceDms(
    invoiceId: string,
    body: PatchInvoiceDmsBody
  ): Promise<void> {
    const path = applyPathTemplate(
      this.cfg.patchPathTemplate,
      this.cfg.apiPrefix,
      invoiceId
    );
    const url = `${this.cfg.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
    const { status, text } = await requestJson<unknown>(
      url,
      'PATCH',
      this.authHeaders(),
      body
    );
    if (status < 200 || status >= 300) {
      throw new Error(
        `Comstruct PATCH DMS failed: HTTP ${status} ${url} ${text}`
      );
    }
  }
}

export { parseConfigFromEnv };
