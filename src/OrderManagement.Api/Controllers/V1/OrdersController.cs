using Asp.Versioning;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using OrderManagement.Api.Auth;
using OrderManagement.Api.Common;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Http;
using OrderManagement.Api.Common.Idempotency;
using OrderManagement.Api.Common.Paging;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Services.Interfaces;

namespace OrderManagement.Api.Controllers.V1;

/// <summary>Orders with line items, priced in a currency permitted for the customer's SADC country.</summary>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/orders")]
public sealed class OrdersController(IOrderService orders) : ApiControllerBase
{
    private const string IdempotentReplayedHeader = "Idempotent-Replayed";

    /// <summary>Create an order.</summary>
    /// <remarks>
    /// The server validates the customer and the currency (it must be permitted for the customer's country), checks each
    /// line, and computes <c>totalAmount</c>. New orders start <c>Pending</c>. Returns the order with its <c>ETag</c>.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.OrdersWrite)]
    [EnableRateLimiting(ApiSetup.WritesRateLimit)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var result = await orders.CreateAsync(request, ct);

        return result.Match(
            created =>
            {
                SetETag(created.ETag);
                return CreatedAtAction(nameof(GetById), new { id = created.Order.Id, version = RouteVersion }, created.Order);
            },
            errors => Problem(errors));
    }

    /// <summary>Get an order with its line items.</summary>
    /// <remarks>
    /// Responses carry an <c>ETag</c>. Send it back as <c>If-None-Match</c> to get <c>304 Not Modified</c> (no body) when
    /// the order hasn't changed.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthPolicies.OrdersRead)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        // Cheap path first: compare ETags without loading line items.
        var etag = await orders.GetETagAsync(id, ct);
        if (etag is not null && ETags.IfNoneMatchHits(Request, etag))
        {
            SetETag(etag);
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var result = await orders.GetByIdAsync(id, ct);

        return result.Match(
            found =>
            {
                SetETag(found.ETag);
                return Ok(found.Order);
            },
            errors => Problem(errors));
    }

    /// <summary>List orders, filtered and sorted, paged.</summary>
    /// <remarks>
    /// Filter by <c>customerId</c> and/or <c>status</c>. <c>sort</c> is <c>createdAt</c> or <c>total</c>; prefix with
    /// <c>-</c> for descending (default <c>-createdAt</c>, newest first). <c>pageSize</c> is at most 100.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.OrdersRead)]
    [ProducesResponseType<PagedResult<OrderSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] OrderListQuery query, CancellationToken ct) =>
        Ok(await orders.ListAsync(query, ct));

    /// <summary>Change an order's status (idempotent).</summary>
    /// <remarks>
    /// <para>Allowed: Pending → Paid | Cancelled, Paid → Fulfilled (once stock is allocated) | Cancelled.</para>
    /// <para>
    /// <b>Idempotency-Key</b> (required): a unique value such as a UUID, chosen per intended change. Retrying with the
    /// same key and body returns the original response (header <c>Idempotent-Replayed: true</c>) without applying it
    /// again. Reusing a key for a different request returns 422. Keys are kept for 24 hours.
    /// </para>
    /// <para><b>If-Match</b> (optional): the ETag you last saw. If the order changed since, you get 412.</para>
    /// </remarks>
    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.OrdersWrite)]
    [EnableRateLimiting(ApiSetup.WritesRateLimit)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        UpdateOrderStatusRequest request,
        [FromHeader(Name = ApiHeaders.IdempotencyKey)] string? idempotencyKey,
        CancellationToken ct)
    {
        _ = idempotencyKey; // bound for the OpenAPI document; read (and validated) from the request below
        if (!OrderStatusParser.TryParse(request.Status, out var target))
        {
            throw new InvalidOperationException("UpdateOrderStatusRequestValidator should have rejected this status.");
        }

        var ifMatch = Request.Headers.IfMatch.ToString();

        // Everything that changes the outcome goes into the fingerprint, so reusing a key for a different change is caught.
        var fingerprint = $"orders/{id}/status\n{target}\n{ifMatch}";
        if (!IdempotencyRequest.TryCreate(HttpContext, fingerprint, out var idempotency, out var keyError))
        {
            return Problem([Error.Validation("Idempotency.KeyMissing", keyError!,
                Domain.Common.ErrorMetadata.ForField(ApiHeaders.IdempotencyKey))]);
        }

        var result = await orders.ChangeStatusAsync(id, target, ifMatch, idempotency!, ct);
        if (result.Replayed)
        {
            Response.Headers[IdempotentReplayedHeader] = "true";
        }

        return result.Outcome.Match(
            changed =>
            {
                SetETag(changed.ETag);
                return Ok(changed.Order);
            },
            errors => Problem(errors));
    }

    private void SetETag(string? etag)
    {
        if (etag is null)
        {
            return;
        }

        Response.Headers.ETag = etag;
        // Clients may keep a copy but must revalidate (If-None-Match) before reusing it.
        Response.Headers.CacheControl = $"{CacheControlHeaderValue.PrivateString}, {CacheControlHeaderValue.NoCacheString}";
    }
}
