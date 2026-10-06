import { describe, expect, it } from 'vitest'
import { ApiError, NetworkError, describeError } from './problem'

describe('ApiError', () => {
  it('maps API field paths onto form paths and keeps the first message', () => {
    const error = new ApiError(400, {
      title: 'Some details need fixing',
      errors: {
        customerId: ['Please choose the customer this order is for.'],
        'lineItems[0].unitPrice': ['Line 1: ZAR prices can have at most 2 decimal places.', 'second message'],
        lineItems: ['SKU A appears on more than one line.'],
      },
    })

    expect(error.fieldErrors).toEqual({
      customerId: 'Please choose the customer this order is for.',
      'lineItems.0.unitPrice': 'Line 1: ZAR prices can have at most 2 decimal places.',
      lineItems: 'SKU A appears on more than one line.',
    })
  })

  it("carries the API's code and correlation ID", () => {
    const error = new ApiError(409, { title: 'Conflict', code: 'email_already_exists', correlationId: 'abc' })

    expect(error.code).toBe('email_already_exists')
    expect(error.correlationId).toBe('abc')
  })

  it('has a friendly title even when the response had no body', () => {
    expect(new ApiError(403, undefined).title).toBe("You don't have access to this")
    expect(new ApiError(503, undefined).title).toBe('Something went wrong on our side')
  })
})

describe('describeError', () => {
  it('uses the problem title, detail and correlation ID', () => {
    const summary = describeError(new ApiError(412, { title: 'This changed since you loaded it', detail: 'Refresh.', correlationId: 'c1' }))

    expect(summary).toEqual({ title: 'This changed since you loaded it', detail: 'Refresh.', reference: 'c1' })
  })

  it('explains network failures without technical detail', () => {
    expect(describeError(new NetworkError(new TypeError('Failed to fetch'))).title).toMatch(/couldn't reach the server/)
  })

  it('never shows raw exception text for unknown errors', () => {
    expect(describeError(new Error('Cannot read properties of undefined'))).toEqual({
      title: 'Something went wrong',
      detail: 'Please try again.',
    })
  })
})
