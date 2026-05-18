/**
 * Live Metasonic DMS smoke: Login + GetDocument.
 * Needs network access to the Scanview endpoint (VPN/DNS/hosts as in your PoC).
 *
 *   METASONIC_DOC_ID=1527018 npm run metasonic:smoke
 */

import { MetasonicClient } from './metasonic-client';
import { ResponseField } from './types';

function env(): 'test' | 'live' {
  const v = (process.env.METASONIC_ENV || 'test').toLowerCase();
  return v === 'live' ? 'live' : 'test';
}

function summarizeFields(fields: ResponseField[] | undefined): { id: string; value: string }[] {
  if (!fields?.length) {
    return [];
  }
  const nonEmpty = fields.filter(
    f => f.Value !== undefined && String(f.Value).trim() !== ''
  );
  return nonEmpty.slice(0, 20).map(f => ({ id: f.ID, value: f.Value }));
}

async function main(): Promise<void> {
  const docId = process.env.METASONIC_DOC_ID?.trim();
  if (!docId) {
    // eslint-disable-next-line no-console
    console.error(
      'Set METASONIC_DOC_ID to a real document id (e.g. METASONIC_DOC_ID=1527018 npm run metasonic:smoke).'
    );
    process.exit(1);
  }

  const environment = env();
  const client = new MetasonicClient({
    environment,
    username: process.env.METASONIC_USER,
    password: process.env.METASONIC_PASSWORD,
    database: process.env.METASONIC_DB,
    endpoint: process.env.METASONIC_ENDPOINT,
  });

  // eslint-disable-next-line no-console
  console.log(`env=${client.getEnvironment()} endpoint=${client.getEndpoint()}`);
  // eslint-disable-next-line no-console
  console.log(`GetDocument ${docId} …`);

  await client.connect();
  const doc = await client.getDocument(docId);
  client.disconnect();

  if (!doc?.ID) {
    // eslint-disable-next-line no-console
    console.error('Document not found or empty response.');
    process.exit(1);
  }

  const payload = {
    id: doc.ID,
    docClassId: doc.DocClassID ?? null,
    mainUserFolderId: doc.MainUserFolderID ?? null,
    fieldCount: doc.FieldList?.length ?? 0,
    nonEmptyFieldSample: summarizeFields(doc.FieldList),
  };

  // eslint-disable-next-line no-console
  console.log('OK');
  // eslint-disable-next-line no-console
  console.log(JSON.stringify(payload, null, 2));
}

main().catch(err => {
  // eslint-disable-next-line no-console
  console.error(err instanceof Error ? err.message : String(err));
  process.exit(1);
});
