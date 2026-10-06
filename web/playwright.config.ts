import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests drive the real app in a browser, against the real API, worker, SQL Server and RabbitMQ.
 *
 * - Set E2E_BASE_URL to test a stack that's already running (Aspire, docker compose, CI). Nothing is started.
 * - Otherwise this starts an isolated local stack: RabbitMQ in Docker (reused if one is on 5672), the API on 5150
 *   and the worker against their own LocalDB database (OrderManagement_E2E), and Vite on 5174. Your dev database
 *   and ports are left alone. `npm run e2e` builds the .NET solution first.
 */
const external = process.env.E2E_BASE_URL
const WEB_PORT = 5174
const API_PORT = 5150
const baseURL = external ?? `http://localhost:${WEB_PORT}`

const e2eDb =
  'Server=(localdb)\\MSSQLLocalDB;Database=OrderManagement_E2E;Trusted_Connection=True;TrustServerCertificate=True'
const dotnetEnv = {
  ConnectionStrings__OrdersDb: e2eDb,
  ConnectionStrings__messaging: 'amqp://guest:guest@localhost:5672',
}

export default defineConfig({
  testDir: './e2e',
  // The journeys share one database and one worker; running them one at a time keeps them independent.
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  globalTeardown: external ? undefined : './e2e/global-teardown.ts',
  webServer: external
    ? undefined
    : [
        {
          name: 'rabbitmq',
          command: 'docker run --rm --name om-e2e-rabbit -p 5672:5672 rabbitmq:4.3-management',
          port: 5672,
          reuseExistingServer: true,
          timeout: 120_000,
        },
        {
          name: 'api',
          command: `dotnet run --no-build --no-launch-profile --project ../src/OrderManagement.Api`,
          url: `http://localhost:${API_PORT}/readiness`,
          reuseExistingServer: false,
          timeout: 180_000,
          env: {
            ...dotnetEnv,
            ASPNETCORE_ENVIRONMENT: 'Development',
            ASPNETCORE_URLS: `http://localhost:${API_PORT}`,
            Cors__AllowedOrigins__0: `http://localhost:${WEB_PORT}`,
          },
        },
        {
          name: 'worker',
          command: 'dotnet run --no-build --no-launch-profile --project ../src/OrderManagement.Worker',
          wait: { stdout: /Bus started/ },
          reuseExistingServer: false,
          timeout: 180_000,
          env: { ...dotnetEnv, DOTNET_ENVIRONMENT: 'Development' },
        },
        {
          name: 'web',
          command: `npx vite --port ${WEB_PORT} --strictPort`,
          url: baseURL,
          reuseExistingServer: false,
          timeout: 60_000,
          env: { VITE_API_BASE_URL: `http://localhost:${API_PORT}` },
        },
      ],
})
