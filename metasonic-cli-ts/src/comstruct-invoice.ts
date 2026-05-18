/**
 * Subset of Comstruct GET /v1/invoices list row (see comstruct-nest InvoiceResponseDto).
 */
export interface ComstructInvoiceListRow {
  id?: string;
  invoice_number?: string;
  supplier_name?: string;
  net_amount?: number;
  total_amount?: number;
  currency?: string;
  created_at?: string;
  /** Used as cursor / idempotency watermark */
  updated_at?: string;
  external_id?: string;
  /**
   * Set by this worker when MetaSonic sync finishes. Server should filter out `SYNCED` rows
   * from the list so the worker does not re-process them (client-side skip is a fallback).
   */
  external_status?: string | null;
}

export interface ComstructInvoicesListResponse {
  data: ComstructInvoiceListRow[];
  total: number;
  limit?: number | null;
  offset?: number;
  hasNext?: boolean;
}

/** Comstruct invoice field set when MetaSonic PDF sync has finished (or failed). */
export type InvoiceExternalStatus = 'SYNCED' | 'FAILED';

/** Body sent via PATCH …/invoices/{id}/dms. Only `external_status` is required; add `dmsLastError` on failure. */
export interface PatchInvoiceDmsBody {
  external_status: InvoiceExternalStatus;
  dmsLastError?: string;
}

export interface DmsWorkerState {
  /** ISO-8601 — only process invoices with updated_at (or created_at) strictly after this when using server-side filter; also used as high-water mark after a successful run */
  lastCheckedAt?: string;
}
