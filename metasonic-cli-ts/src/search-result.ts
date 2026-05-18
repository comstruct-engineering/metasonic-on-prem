import type { ResponseDocument } from './types';

/**
 * Normalizes `Search` / `FastSearch` SOAP payloads (node-soap shape varies).
 */
export function normalizeSearchDocumentsResult(raw: unknown): ResponseDocument[] {
  if (raw == null) {
    return [];
  }
  const top = raw as Record<string, unknown>;
  const sr = top.SearchResult ?? top.FastSearchResult;
  if (sr == null) {
    return [];
  }
  if (Array.isArray(sr)) {
    return sr as ResponseDocument[];
  }
  if (typeof sr === 'object') {
    const o = sr as { ResponseDocument?: unknown };
    if (Array.isArray(o.ResponseDocument)) {
      return o.ResponseDocument as ResponseDocument[];
    }
    if (o.ResponseDocument && typeof o.ResponseDocument === 'object') {
      return [o.ResponseDocument as ResponseDocument];
    }
    if ((sr as ResponseDocument).ID) {
      return [sr as ResponseDocument];
    }
  }
  return [];
}
