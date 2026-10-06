import { execSync } from 'node:child_process'

/** Removes the RabbitMQ container the e2e run started. A broker that was already running is left alone. */
export default function globalTeardown() {
  try {
    execSync('docker rm -f om-e2e-rabbit', { stdio: 'ignore' })
  } catch {
    // Not started by this run, or already gone.
  }
}
