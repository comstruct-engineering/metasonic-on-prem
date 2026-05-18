import * as fs from 'fs';
import * as path from 'path';

const RELATIVE_TO_PACKAGE_ROOT = path.join(
  'METASONIC-WEBSERVICE_DEMO',
  'Connected Services',
  'ScanviewWebservice',
  'ScanviewWebservice1.wsdl'
);

/**
 * Resolves the full path to the Scanview WSDL shipped in this monorepo (MetasonicFormular root).
 * Override with {@link SCANVIEW_MOCK_WSDL} for custom layouts or CI.
 */
export function resolveScanviewWsdlPath(): string {
  if (process.env.SCANVIEW_MOCK_WSDL) {
    return path.resolve(process.env.SCANVIEW_MOCK_WSDL);
  }

  const candidates = [
    path.join(__dirname, '..', '..', '..', RELATIVE_TO_PACKAGE_ROOT),
    path.join(process.cwd(), RELATIVE_TO_PACKAGE_ROOT),
    path.join(process.cwd(), '..', RELATIVE_TO_PACKAGE_ROOT),
  ];

  for (const c of candidates) {
    if (fs.existsSync(c)) {
      return c;
    }
  }

  throw new Error(
    `Scanview WSDL not found. Set SCANVIEW_MOCK_WSDL to the absolute path of ScanviewWebservice1.wsdl, ` +
      `or run from the monorepo where ${RELATIVE_TO_PACKAGE_ROOT} exists.`
  );
}
