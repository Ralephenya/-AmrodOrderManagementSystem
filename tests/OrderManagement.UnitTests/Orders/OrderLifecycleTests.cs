using ErrorOr;
using OrderManagement.Domain.Orders;
using static OrderManagement.UnitTests.TestData;

namespace OrderManagement.UnitTests.Orders;

public class OrderLifecycleTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, new[] { OrderStatus.Paid, OrderStatus.Cancelled })]
    [InlineData(OrderStatus.Paid, new[] { OrderStatus.Fulfilled, OrderStatus.Cancelled })]
    [InlineData(OrderStatus.Fulfilled, new OrderStatus[0])]
    [InlineData(OrderStatus.Cancelled, new OrderStatus[0])]
    public void NextStatusesFrom_MatchesTheLifecycle(OrderStatus from, OrderStatus[] expected)
    {
        Order.NextStatusesFrom(from).ShouldBe(expected);
    }

    [Fact]
    public void Pending_ToPaid_Succeeds()
    {
        var order = PendingOrder();

        order.TransitionTo(OrderStatus.Paid).IsError.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Paid)]
    public void PendingOrPaid_ToCancelled_Succeeds(OrderStatus from)
    {
        var order = OrderIn(from);

        order.TransitionTo(OrderStatus.Cancelled).IsError.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void Pending_ToFulfilled_SkippingPayment_IsRejected()
    {
        var order = PendingOrder();

        var result = order.TransitionTo(OrderStatus.Fulfilled);

        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        result.FirstError.Code.ShouldBe("Order.InvalidStatusTransition");
        result.FirstError.Description.ShouldBe(
            "A Pending order can't be moved to Fulfilled. It can only be moved to Paid or Cancelled.");
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Theory]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Fulfilled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    public void TerminalStatuses_CannotChange(OrderStatus terminal, OrderStatus target)
    {
        var order = OrderIn(terminal);

        var result = order.TransitionTo(target);

        result.FirstError.Code.ShouldBe("Order.InvalidStatusTransition");
        result.FirstError.Description.ShouldBe($"A {terminal} order can't be changed any more.");
        order.Status.ShouldBe(terminal);
    }

    [Fact]
    public void Paid_BackToPending_IsRejected()
    {
        var order = OrderIn(OrderStatus.Paid);

        order.TransitionTo(OrderStatus.Pending).FirstError.Code.ShouldBe("Order.InvalidStatusTransition");
    }

    [Fact]
    public void TransitionToCurrentStatus_IsAConflict()
    {
        var order = OrderIn(OrderStatus.Paid);

        var result = order.TransitionTo(OrderStatus.Paid);

        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        result.FirstError.Description.ShouldBe("This order is already Paid.");
    }

    [Fact]
    public void UndefinedStatusValue_IsAValidationError()
    {
        var result = PendingOrder().TransitionTo((OrderStatus)99);

        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        result.FirstError.Code.ShouldBe("Order.InvalidStatus");
    }

    [Fact]
    public void Paid_ToFulfilled_BeforeAllocation_IsRejected()
    {
        var order = OrderIn(OrderStatus.Paid);

        order.TransitionTo(OrderStatus.Fulfilled).FirstError.Code.ShouldBe("Order.NotAllocated");
        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public void Allocate_PendingOrder_StampsUtcTime_AndStaysPending()
    {
        var order = PendingOrder();

        order.Allocate(Clock()).IsError.ShouldBeFalse();

        order.AllocatedAt.ShouldBe(Now.UtcDateTime);
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void Allocate_PaidOrder_FulfilsIt()
    {
        var order = OrderIn(OrderStatus.Paid);

        order.Allocate(Clock());

        order.Status.ShouldBe(OrderStatus.Fulfilled);
    }

    [Fact]
    public void AllocatedThenPaid_FulfilIfReady_FulfilsOnce()
    {
        var order = PendingOrder();
        order.Allocate(Clock());
        order.TransitionTo(OrderStatus.Paid);

        order.FulfilIfReady().ShouldBeTrue();
        order.Status.ShouldBe(OrderStatus.Fulfilled);
        order.FulfilIfReady().ShouldBeFalse();
    }

    [Fact]
    public void FulfilIfReady_PaidButNotAllocated_DoesNothing()
    {
        var order = OrderIn(OrderStatus.Paid);

        order.FulfilIfReady().ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Paid);
    }

    [Fact]
    public void Allocate_IsIdempotent_ForMessageRedelivery()
    {
        var clock = Clock();
        var order = PendingOrder();
        order.Allocate(clock);
        var firstAllocation = order.AllocatedAt;

        clock.Advance(TimeSpan.FromMinutes(5));
        order.Allocate(clock).IsError.ShouldBeFalse();

        order.AllocatedAt.ShouldBe(firstAllocation);
    }

    [Fact]
    public void Allocate_FulfilledOrder_IsANoOp()
    {
        var order = OrderIn(OrderStatus.Fulfilled);

        order.Allocate(Clock()).IsError.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Fulfilled);
    }

    [Fact]
    public void Allocate_CancelledOrder_IsRejected()
    {
        var order = OrderIn(OrderStatus.Cancelled);

        order.Allocate(Clock()).FirstError.Code.ShouldBe("Order.Cancelled");
        order.AllocatedAt.ShouldBeNull();
    }

    private static Order OrderIn(OrderStatus status)
    {
        var order = PendingOrder();
        switch (status)
        {
            case OrderStatus.Paid:
                order.TransitionTo(OrderStatus.Paid);
                break;
            case OrderStatus.Fulfilled:
                order.TransitionTo(OrderStatus.Paid);
                order.Allocate(Clock());
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled);
                break;
        }

        order.Status.ShouldBe(status);
        return order;
    }
}
