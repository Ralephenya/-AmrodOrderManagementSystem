import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { clearAccessToken } from '../api/auth'
import { server } from './server'

// Pages are lazy-loaded chunks, and the first import of one (the dashboard pulls in recharts) can take over the
// default 1s under Vitest, so findBy* queries wait a little longer.
configure({ asyncUtilTimeout: 5000 })

// Any request without a handler fails the test, so a test can't silently depend on a real API.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  cleanup()
  server.resetHandlers()
  clearAccessToken()
})
afterAll(() => server.close())
