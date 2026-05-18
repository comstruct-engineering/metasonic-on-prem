/**
 * DMS upload worker: Comstruct → MetaSonic (cron can call `npm run dms:worker`).
 * Requires Comstruct REST contract documented in metasonic-cli-ts README (PATCH + list filters).
 */

import * as fs from 'node:fs';
import * as path from 'node:path';
import { ComstructDmsClient, loadComstructDmsConfig } from './comstruct-dms-client';
import type { ComstructInvoiceListRow, DmsWorkerState } from './comstruct-invoice';
import {
  invoiceCursorTimestamp,
  invoiceRowToMetasonicFields,
  type MetasonicDmsCorrelationKey,
  resolveMetasonicCorrelationValue,
  withComstructInvoiceCorrelation,
} from './invoice-to-metasonic-fields';
import { MetasonicClient } from './metasonic-client';
import type { RequestItem } from './types';

const STATE_ENV = 'DMS_WORKER_STATE_FILE';
const DRY = process.env.DMS_WORKER_DRY_RUN === '1' || process.env.DMS_WORKER_DRY_RUN === 'true';

function readStateFile(filePath: string): DmsWorkerState {
  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    return JSON.parse(raw) as DmsWorkerState;
  } catch {
    return {};
  }
}

function writeStateFile(filePath: string, state: DmsWorkerState): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, JSON.stringify(state, null, 2), 'utf8');
}

function maxIso(a: string, b: string): string {
  return Date.parse(a) >= Date.parse(b) ? a : b;
}

function sortByCursor(rows: ComstructInvoiceListRow[]): ComstructInvoiceListRow[] {
  return [...rows].sort(
    (x, y) =>
      Date.parse(invoiceCursorTimestamp(x)) - Date.parse(invoiceCursorTimestamp(y))
  );
}

/** Skip rows where `external_status` is already SYNCED — FAILED rows are retried. */
function skipFinishedRows(rows: ComstructInvoiceListRow[]): ComstructInvoiceListRow[] {
  return rows.filter(r => {
    const ext = String(r.external_status ?? '').trim().toUpperCase();
    return ext !== 'SYNCED';
  });
}

function metasonicConfigFromEnv(): ConstructorParameters<typeof MetasonicClient>[0] {
  const env = (process.env.METASONIC_ENV || 'test').toLowerCase() === 'live' ? 'live' : 'test';
  return {
    environment: env,
    username: process.env.METASONIC_USER,
    password: process.env.METASONIC_PASSWORD,
    database: process.env.METASONIC_DB,
    endpoint: process.env.METASONIC_ENDPOINT,
    comstructInvoiceQualifierId: process.env.METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID,
    metasonicSearchUserFolderId: process.env.METASONIC_SEARCH_USER_FOLDER_ID,
  };
}

function docClassId(): string {
  return process.env.METASONIC_DOC_CLASS_ID || '1';
}

function comstructCorrelationQualifierId(): string | undefined {
  return process.env.METASONIC_COMSTRUCT_INVOICE_QUALIFIER_ID?.trim() || undefined;
}

function metasonicDmsCorrelationKey(): MetasonicDmsCorrelationKey {
  const m = (process.env.METASONIC_DMS_CORRELATION_KEY || 'external_id').toLowerCase();
  return m === 'id' ? 'id' : 'external_id';
}

function allowCreateDmsDocumentIfNotFound(): boolean {
  return (
    process.env.DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT === '1' ||
    process.env.DMS_WORKER_ALLOW_CREATE_DMS_DOCUMENT === 'true'
  );
}

async function processOne(
  c: ComstructDmsClient,
  ms: MetasonicClient,
  row: ComstructInvoiceListRow
): Promise<'ok' | 'skipped_no_pdf'> {
  const invoiceId = String(row.id ?? '');
  if (!invoiceId) {
    throw new Error('Invoice row missing id');
  }

  const keyMode = metasonicDmsCorrelationKey();
  const correlationValue = resolveMetasonicCorrelationValue(row, keyMode);
  if (comstructCorrelationQualifierId() && !correlationValue) {
    throw new Error(
      `Invoice ${invoiceId}: missing ${keyMode} for MetaSonic Search; set the field or use METASONIC_DMS_CORRELATION_KEY=id`
    );
  }

  if (DRY) {
    // eslint-disable-next-line no-console
    console.log(`[dry-run] would process invoice ${invoiceId}`);
    return 'ok';
  }

  const pdf = await c.downloadInvoicePdf(invoiceId);
  if (!pdf || pdf.length === 0) {
    // eslint-disable-next-line no-console
    console.log(
      `Invoice ${invoiceId}: no PDF from Comstruct, skipping.`
    );
    return 'skipped_no_pdf';
  }

  const fileName = `invoice-${invoiceId}.pdf`;

  const hasCorrelation =
    !!comstructCorrelationQualifierId() && !!correlationValue;

  if (hasCorrelation) {
    const existing = await ms.findExistingDocumentByCorrelationValue(
      correlationValue,
      docClassId()
    );
    if (existing?.ID) {
      await ms.appendDocumentPdfItem(String(existing.ID), pdf, fileName);
      await c.patchInvoiceDms(invoiceId, { external_status: 'SYNCED' });
      // eslint-disable-next-line no-console
      console.log(
        `Invoice ${invoiceId}: appended PDF on DMS document ${String(existing.ID)} (correlation=${keyMode}).`
      );
      return 'ok';
    }

    if (!allowCreateDmsDocumentIfNotFound()) {
      throw new Error(
        `MetaSonic: no document for correlation ${keyMode}=${correlationValue}`
      );
    }

    const createFields = withComstructInvoiceCorrelation(
      [],
      correlationValue,
      comstructCorrelationQualifierId()
    );
    const item: RequestItem = {
      ItemData: pdf.toString('base64'),
      ItemName: 'Document',
      ItemTypeNumber: 1,
      OriginalFileName: fileName,
    };
    await ms.saveDocument({
      DocClassID: docClassId(),
      FieldList: createFields,
      ItemList: [item],
    });
    await c.patchInvoiceDms(invoiceId, { external_status: 'SYNCED' });
    return 'ok';
  }

  const fieldList = withComstructInvoiceCorrelation(
    invoiceRowToMetasonicFields(row),
    correlationValue ?? invoiceId,
    comstructCorrelationQualifierId()
  );
  const item: RequestItem = {
    ItemData: pdf.toString('base64'),
    ItemName: 'Document',
    ItemTypeNumber: 1,
    OriginalFileName: fileName,
  };

  await ms.saveDocument({
    DocClassID: docClassId(),
    FieldList: fieldList,
    ItemList: [item],
  });

  await c.patchInvoiceDms(invoiceId, { external_status: 'SYNCED' });
  return 'ok';
}

async function processOneWithFailure(
  c: ComstructDmsClient,
  ms: MetasonicClient,
  row: ComstructInvoiceListRow
): Promise<'ok' | 'skipped_no_pdf'> {
  const invoiceId = String(row.id ?? '');
  try {
    return await processOne(c, ms, row);
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    // eslint-disable-next-line no-console
    console.error(`Invoice ${invoiceId} DMS upload failed:`, message);
    if (!DRY && invoiceId) {
      try {
        await c.patchInvoiceDms(invoiceId, {
          external_status: 'FAILED',
          dmsLastError: message,
        });
      } catch (reportErr) {
        // eslint-disable-next-line no-console
        console.error(
          `Could not PATCH dms_upload_failed for ${invoiceId}:`,
          reportErr instanceof Error ? reportErr.message : String(reportErr)
        );
      }
    }
    return 'ok';
  }
}

async function main(): Promise<void> {
  const stateFile =
    process.env[STATE_ENV] || path.join(process.cwd(), '.dms-worker-state.json');
  const state = readStateFile(stateFile);
  const initialWatermark =
    state.lastCheckedAt || process.env.DMS_WORKER_INITIAL_UPDATED_AFTER || undefined;

  const dmsCfg = loadComstructDmsConfig();
  const comstruct = new ComstructDmsClient(dmsCfg);
  const corrKey = metasonicDmsCorrelationKey();
  // eslint-disable-next-line no-console
  console.log(
    `DMS worker: Comstruct base=${dmsCfg.baseUrl}${dmsCfg.queueStatus ? ` statusFilter=${dmsCfg.queueStatus}` : ''} metasonicCorrelationKey=${corrKey} dryRun=${DRY}`
  );

  const list = await comstruct.listInvoicesForDmsUpload(initialWatermark);
  let rows = list.data;
  const fromApi = rows.length;
  if (initialWatermark) {
    rows = rows.filter(
      r => Date.parse(invoiceCursorTimestamp(r)) > Date.parse(initialWatermark)
    );
  }
  const afterTime = rows.length;
  rows = skipFinishedRows(rows);
  const afterSkip = rows.length;
  rows = sortByCursor(rows);

  // eslint-disable-next-line no-console
  console.log(
    `DMS worker: fromApi=${fromApi} afterTime=${afterTime} afterSkipId=${afterSkip} (processing ${rows.length})`
  );

  if (rows.length === 0) {
    return;
  }

  const ms = new MetasonicClient(metasonicConfigFromEnv());
  if (!DRY) {
    await ms.connect();
  }

  let maxCursor = initialWatermark || new Date(0).toISOString();
  for (const row of rows) {
    const outcome = await processOneWithFailure(comstruct, ms, row);
    if (outcome !== 'skipped_no_pdf') {
      const c = invoiceCursorTimestamp(row);
      maxCursor = maxIso(maxCursor, c);
    }
  }

  if (!DRY) {
    ms.disconnect();
  }

  if (!DRY) {
    writeStateFile(stateFile, { lastCheckedAt: maxCursor });
    // eslint-disable-next-line no-console
    console.log(`DMS worker: state written ${stateFile} lastCheckedAt=${maxCursor}`);
  }
}

main().catch(err => {
  // eslint-disable-next-line no-console
  console.error(err instanceof Error ? err.message : String(err));
  process.exit(1);
});
