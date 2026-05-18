import { describe, expect, it } from 'vitest';
import { normalizeResponseFieldList } from './field-list';

describe('normalizeResponseFieldList', () => {
  it('returns array as-is', () => {
    const rows = [{ ID: '1', Value: 'a' }];
    expect(normalizeResponseFieldList(rows)).toEqual(rows);
  });

  it('unwraps ResponseDocumentField', () => {
    const inner = [
      { ID: '223', Value: 'x' },
      { ID: '89900202', Value: 'y' },
    ];
    expect(
      normalizeResponseFieldList({ ResponseDocumentField: inner })
    ).toEqual(inner);
  });

  it('returns empty for undefined or unknown shape', () => {
    expect(normalizeResponseFieldList(undefined)).toEqual([]);
    expect(normalizeResponseFieldList({})).toEqual([]);
  });
});
