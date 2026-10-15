# Option B: real `comstruct-nest` + seeded demo data

This is the closest-to-production way to test HTTP calls from the MetaSonic tooling **without** a live DMS network: run the Comstruct stack locally and use the same invoice API the integration uses in production.

## 1. Comstruct monorepo (`comstruct-one`)

Follow the root **README** in **`comstruct-one`** (§§ 1–5) (Docker Postgres + Hasura + Redis, `hasura metadata/migrate/seed apply`, `npm run dev:nest` on **port 8082** with `GOOGLE_APPLICATION_CREDENTIALS` and empty `DELIVERY_PUBSUB_SUBSCRIPTION`).

Seeds include:

- `apps/hasura/seeds/default/demo_invoices_delivery_notes.sql` — demo tenant `11111111-1111-1111-1111-111111111111`, invoices e.g. `10001`…`10050` (integer ids).
- `apps/hasura/seeds/default/zz_metasonic_dev_api_key.sql` — dev API key for that tenant (see below; `zz_` so it runs after `demo_invoices_delivery_notes.sql`).

## 2. API key (local only)

| Field | Notes |
|--------|--------|
| **Header** | Send `x-api-key` using the plaintext value defined in the Hasura seed **zz_metasonic_dev_api_key.sql** |
| **Tenant** | Key is bound to customer `11111111-1111-1111-1111-111111111111` (demo “Bauunternehmung Müller AG”) |

`comstruct-nest` stores **SHA-256 (hex)** of the plaintext; the seed matches that.

## 3. Smoke test from this folder

With Nest on `http://localhost:8082` (see `PORT` in `apps/comstruct-nest/.env`):

```bash
cd metasonic-cli-ts
npm run build
export COMSTRUCT_NEST_URL=http://localhost:8082
export COMSTRUCT_API_KEY='<plaintext key matching the Hasura seed zz_metasonic_dev_api_key.sql>'
npm run comstruct:smoke
```

This calls `GET /v1/invoices?limit=3&status=CHECKED` (Nest global prefix `v1`) and prints the JSON (or an error). It uses the **real** `JwtOrApiKeyGuard` + `INVOICES_READ` path, not a mock. Override path with `COMSTRUCT_API_PREFIX` if your deployment uses another prefix.

## 4. PDF endpoint note

`GET /v1/invoices/:id/pdf` needs an invoice with a **linked document** in storage. The numeric demo rows may not all have files; if PDF returns 404, create/link a test document via the app or ask backend for a seed extension. Listing invoices still validates Option B for the read path.

## 5. MetaSonic DMS side

Unchanged: without network/VPN access to Scanview, the CLI DMS commands do not connect to a live server (use `npm run metasonic:mock` for local SOAP testing). Option B covers **Comstruct API** parity only.

## 6. Troubleshooting: Nest logs `GraphQL Error (Code: 404)` / HTTP 500 on `/v1/invoices`

If `comstruct:smoke` returns **500** and Nest logs show **`GraphQL Error (Code: 404)`** with an **empty** `response.headers` for `CheckApiKey`, `GetConnectionsWhere`, etc., Hasura is almost certainly not reachable at the URL Nest is using.

**Fix in `apps/comstruct-nest/.env`:**

1. **`GRAPHQL_ENDPOINT`** must be the **full GraphQL API URL**, not only the host:
   - Correct: `http://localhost:8080/v1/graphql`
   - Wrong: `http://localhost:8080` (POSTs to the wrong path → **404**)
2. **`HASURA_ADMIN_SECRET`** must match **`HASURA_GRAPHQL_ADMIN_SECRET`** in `apps/hasura/.env`.
3. **Ports:** Hasura often uses **8080**; Nest is often **8081** or **8082** (`PORT`). They must be different processes; do not point `GRAPHQL_ENDPOINT` at Nest’s own port.

Quick check (replace admin secret):

```bash
curl -s -X POST http://localhost:8080/v1/graphql \
  -H "Content-Type: application/json" \
  -H "x-hasura-admin-secret: YOUR_ADMIN_SECRET" \
  -d '{"query":"query { __typename }"}'
```

You should get HTTP **200** and JSON with `data`, not 404. After that, restart Nest and run `comstruct:smoke` again.
