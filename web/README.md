# Order Management web app

React + TypeScript (Vite) front end for the Order Management API.

## Run it

The easiest way is the Aspire AppHost, which starts SQL, RabbitMQ, the API, the worker and this app together:

```bash
dotnet run --project src/OrderManagement.AppHost
```

The app is at http://localhost:5173, the origin the API's Development CORS policy allows.

To run the app on its own against an API you started yourself:

```bash
npm install
npm run dev
```

It calls `VITE_API_BASE_URL`, or `http://localhost:5050` (the API's `http` launch profile) when that isn't set.
See `.env.example`.

## Sign-in

In Development the API runs in mock-Entra mode. The app gets a token from `POST /api/v1/dev/token` with the
`Orders.Read`, `Orders.Write` and `Orders.Admin` roles, and refreshes it before it expires. For a real Entra tenant,
only `getAccessToken` in `src/api/auth.ts` changes (to MSAL).

## API types

`src/api/schema.d.ts` is generated from the API's OpenAPI document, which is checked in at `openapi/v1.json`, so
the app builds without a running API. After changing an API contract, with the API running:

```bash
npm run api:pull
```

That downloads the document and regenerates the types. `npm run api:types` regenerates from the checked-in file.
A type error after regenerating means the app is out of step with the API.

## Scripts

| Script | What it does |
|---|---|
| `npm run dev` | Dev server on port 5173 |
| `npm test` | Vitest + React Testing Library, API mocked with MSW |
| `npm run typecheck` | `tsc` in strict mode |
| `npm run lint` | oxlint |
| `npm run build` | Typecheck and production build to `dist/` |

## Look and feel

- **UI kit:** [shadcn/ui](https://ui.shadcn.com) (new-york style, neutral base) on Tailwind CSS v4, with lucide icons,
  sonner toasts and the Geist font. Components live in `src/components/ui` (add more with `npx shadcn@latest add`).
  Selects are native `<select>`s styled to match (`native-select.tsx`), so keyboards, screen readers and phone pickers
  work without extra code.
- **Theme:** colour tokens in `src/styles.css` (oklch), light and dark with a toggle in the top bar. The choice is
  remembered, otherwise the OS setting is used, and it's applied before first paint so dark mode doesn't flash.
- **Shell:** a dark sidebar (collapsible on desktop, a drawer on phones), breadcrumbs, and a thin progress bar while
  a page loads. Pages are lazy-loaded, so the charting library only downloads with the dashboard.
- **Dashboard:** order counts per status (each links to that filtered list), recent orders, and the top-spenders
  report (brief Q13) as a bar chart plus a ranked list, for one currency at a time.

## How it works

- **Data:** TanStack Query. Lists keep the previous page on screen while the next one loads. Reference data
  (countries and currencies) is fetched once per session.
- **Errors:** every API error is an RFC 7807 ProblemDetails. The app shows its friendly `title` and `detail`, puts
  field errors on the matching form fields, and shows the `correlationId` as a reference to quote to support.
- **Money:** amounts are formatted with `Intl.NumberFormat` using each currency's minor units (KMF has none).
  The currencies offered for an order come from the customer's country.
- **Status changes:** the details page offers only the transitions the API lists in `allowedTransitions`. Each click
  sends a new `Idempotency-Key`, plus the ETag it showed as `If-Match`, so a change made elsewhere meanwhile
  returns 412 and the page reloads. A paid order can't be fulfilled until the worker allocates its stock; the page
  polls until it has.
- **State in the URL:** list filters, sort and page live in the query string, so refreshes and links keep them.
- **Accessibility:** labelled controls, errors linked with `aria-describedby`, toasts in live regions, a skip link,
  visible focus, and reduced motion respected.

## Layout

```
src/
  api/          typed client, auth, ProblemDetails errors, TanStack Query hooks, generated schema
  app/          routes, query client, providers
  components/   app shell, theme, toasts, alerts, pagination, skeletons, form field
  components/ui shadcn/ui primitives
  features/     dashboard, customers and orders pages (tests sit next to them)
  lib/          money, dates, idempotency keys, form and URL helpers, theme
  test/         MSW server, fixtures, render helper
```
