import { QUALIFIERS, type RequestField } from './types';
import type { ComstructInvoiceListRow } from './comstruct-invoice';

/** What to put in `METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID` for Search / Save (matches MetaSonic / external system). */
export type MetasonicDmsCorrelationKey = 'id' | 'external_id';

function str(n: number | undefined): string {
  if (n === undefined || Number.isNaN(n)) {
    return '';
  }
  return String(n);
}

/**
 * Maps a Comstruct invoice list row to MetaSonic RequestField list (qualifier IDs from PoC).
 * Extend when product adds more DMS fields.
 */
export function invoiceRowToMetasonicFields(
  row: ComstructInvoiceListRow
): RequestField[] {
  const fields: RequestField[] = [];
  const supplier = row.supplier_name?.trim();
  if (supplier) {
    fields.push({ ID: QUALIFIERS.KREDITOR, Value: supplier });
  }
  const beleg =
    row.invoice_number?.trim() || row.external_id?.trim() || row.id || '';
  if (beleg) {
    fields.push({ ID: QUALIFIERS.BELEGNR, Value: beleg });
  }
  const amount = row.net_amount ?? row.total_amount;
  if (amount !== undefined) {
    fields.push({ ID: QUALIFIERS.BETRAG, Value: str(amount) });
  }
  fields.push({ ID: QUALIFIERS.SACHLICH, Value: 'ungeprüft' });
  fields.push({ ID: QUALIFIERS.PREISLICH, Value: 'ungeprüft' });
  return fields;
}

/**
 * Value used for MetaSonic `Search` and the correlation qualifier: Comstruct `id` (uuid) or `external_id`
 * (must match the value stored in MetaSonic for that field).
 */
export function resolveMetasonicCorrelationValue(
  row: ComstructInvoiceListRow,
  key: MetasonicDmsCorrelationKey
): string | null {
  if (key === 'id') {
    const v = String(row.id ?? '').trim();
    return v ? v : null;
  }
  const v = String(row.external_id ?? '').trim();
  return v ? v : null;
}

/**
 * Adds the correlation value (e.g. `external_id`) to the MetaSonic dedupe / link qualifier.
 */
export function withComstructInvoiceCorrelation(
  fields: RequestField[],
  correlationValue: string,
  qualifierId: string | undefined
): RequestField[] {
  const q = qualifierId?.trim();
  if (!q) {
    return fields;
  }
  return [...fields, { ID: q, Value: correlationValue }];
}

export function invoiceCursorTimestamp(row: ComstructInvoiceListRow): string {
  const t = row.updated_at || row.created_at;
  return t || new Date(0).toISOString();
}
