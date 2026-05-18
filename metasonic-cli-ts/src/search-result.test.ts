import { describe, expect, it } from 'vitest';
import { normalizeSearchDocumentsResult } from './search-result';

describe('normalizeSearchDocumentsResult', () => {
  it('unwraps SearchResult array', () => {
    const a = { ID: '1', FieldList: [] };
    const b = { ID: '2', FieldList: [] };
    expect(
      normalizeSearchDocumentsResult({ SearchResult: { ResponseDocument: [a, b] } })
    ).toEqual([a, b]);
  });

  it('returns empty for missing', () => {
    expect(normalizeSearchDocumentsResult({})).toEqual([]);
  });
});
