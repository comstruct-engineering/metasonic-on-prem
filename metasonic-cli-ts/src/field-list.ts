import type { ResponseField } from './types';

/**
 * SOAP `node-soap` may deserialize `FieldList` as a plain array or as
 * `{ ResponseDocumentField: ResponseField[] }` (WCF / .NET). Normalize to an array.
 */
export function normalizeResponseFieldList(
  fieldList: unknown
): ResponseField[] {
  if (!fieldList) {
    return [];
  }
  if (Array.isArray(fieldList)) {
    return fieldList as ResponseField[];
  }
  if (typeof fieldList === 'object' && fieldList !== null) {
    const o = fieldList as Record<string, unknown>;
    const nested = o.ResponseDocumentField ?? o.ResponseField;
    if (Array.isArray(nested)) {
      return nested as ResponseField[];
    }
  }
  return [];
}
