/**
 * Calls comstruct-nest GET /invoices with a real API key (Option B smoke test).
 * Requires a running local stack: see docs/COMSTRUCT_OPTION_B.md
 */

import * as http from 'http';
import * as https from 'https';

function requestText(
  urlString: string,
  headers: Record<string, string>
): Promise<{ status: number; body: string }> {
  return new Promise((resolve, reject) => {
    const u = new URL(urlString);
    const isHttps = u.protocol === 'https:';
    const lib = isHttps ? https : http;
    const port =
      u.port || (isHttps ? 443 : 80);
    const req = lib.request(
      {
        hostname: u.hostname,
        port,
        path: `${u.pathname}${u.search}`,
        method: 'GET',
        headers,
      },
      res => {
        const chunks: Buffer[] = [];
        res.on('data', (c: Buffer) => chunks.push(c));
        res.on('end', () => {
          resolve({
            status: res.statusCode ?? 0,
            body: Buffer.concat(chunks).toString('utf8'),
          });
        });
      }
    );
    req.on('error', reject);
    req.end();
  });
}

async function main(): Promise<void> {
  const base =
    process.env.COMSTRUCT_NEST_URL?.replace(/\/$/, '') ||
    'http://localhost:8082';
  const keyRaw = process.env.COMSTRUCT_API_KEY;
  const key =
    typeof keyRaw === 'string' && keyRaw.trim() !== '' ? keyRaw.trim() : undefined;
  if (!key) {
    // eslint-disable-next-line no-console
    console.error('Set COMSTRUCT_API_KEY (see docs/COMSTRUCT_OPTION_B.md)');
    process.exit(1);
  }
  const status = process.env.COMSTRUCT_INVOICE_STATUS || 'CHECKED';
  const limit = process.env.COMSTRUCT_INVOICE_LIMIT || '3';
  const prefix = process.env.COMSTRUCT_API_PREFIX || 'v1';
  const url = `${base}/${prefix}/invoices?limit=${encodeURIComponent(limit)}&status=${encodeURIComponent(status)}`;

  // eslint-disable-next-line no-console
  console.log(`GET ${url}`);

  const { status: code, body } = await requestText(url, {
    'x-api-key': key,
    Accept: 'application/json',
  });

  // eslint-disable-next-line no-console
  console.log(`HTTP ${code}`);
  // eslint-disable-next-line no-console
  console.log(body);
  if (code < 200 || code >= 300) {
    process.exit(1);
  }
}

main().catch(err => {
  console.error(String(err));
  process.exit(1);
});
