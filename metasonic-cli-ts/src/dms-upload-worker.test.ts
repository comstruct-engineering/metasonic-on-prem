import { describe, expect, it } from 'vitest';
import {
  invoiceCursorTimestamp,
  invoiceRowToMetasonicFields,
  resolveMetasonicCorrelationValue,
  withComstructInvoiceCorrelation,
} from './invoice-to-metasonic-fields';
import { QUALIFIERS } from './types';

describe('invoiceRowToMetasonicFields', () => {
  it('maps supplier, invoice number, and amounts to qualifier IDs', () => {
    const fields = invoiceRowToMetasonicFields({
      id: '42',
      invoice_number: 'INV-1',
      supplier_name: 'Acme Ltd',
      net_amount: 100.5,
      currency: 'EUR',
    });
    const byId = Object.fromEntries(fields.map(f => [f.ID, f.Value]));
    expect(byId[QUALIFIERS.KREDITOR]).toBe('Acme Ltd');
    expect(byId[QUALIFIERS.BELEGNR]).toBe('INV-1');
    expect(byId[QUALIFIERS.BETRAG]).toBe('100.5');
    expect(byId[QUALIFIERS.SACHLICH]).toBe('ungeprüft');
    expect(byId[QUALIFIERS.PREISLICH]).toBe('ungeprüft');
  });

  it('uses updated_at for cursor when present', () => {
    const t = '2026-01-10T12:00:00.000Z';
    expect(
      invoiceCursorTimestamp({ created_at: '2020-01-01T00:00:00.000Z', updated_at: t })
    ).toBe(t);
  });
});

describe('resolveMetasonicCorrelationValue', () => {
  it('uses external_id when key is external_id', () => {
    expect(
      resolveMetasonicCorrelationValue(
        { id: 'uuid-1', external_id: 'EXT-100' },
        'external_id'
      )
    ).toBe('EXT-100');
  });

  it('uses id when key is id', () => {
    expect(resolveMetasonicCorrelationValue({ id: 'uuid-1', external_id: 'EXT-100' }, 'id')).toBe('uuid-1');
  });

  it('returns null when external_id is missing and key is external_id', () => {
    expect(
      resolveMetasonicCorrelationValue({ id: 'uuid-1' } as { id: string; external_id?: string }, 'external_id')
    ).toBeNull();
  });
});

describe('withComstructInvoiceCorrelation', () => {
  it('appends correlation qualifier with the given value', () => {
    const f = withComstructInvoiceCorrelation(
      [{ ID: '89900202', Value: 'A' }],
      'EXT-42',
      '99900001'
    );
    expect(f.find(x => x.ID === '99900001')?.Value).toBe('EXT-42');
  });

  it('leaves fields unchanged when qualifier is unset', () => {
    const base = [{ ID: '1', Value: 'v' }];
    expect(withComstructInvoiceCorrelation(base, '42', undefined)).toEqual(base);
  });
});
