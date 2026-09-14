using FluentAssertions;
using MassTransit;
using Moq;
using TrezzeCloud.Contracts.Events;
using TrezzeCloud.Payments.Application.Consumers;

namespace TrezzeCloud.Payments.UnitTests;

public sealed class OrderPlacedConsumerTests
{
    [Fact]
    public async Task Consume_PublishesExactlyOneApprovedPaymentWithOrderData()
    {
        var order = CreateOrder(123.45m);
        var (context, published) = CreateContext(order);

        await new OrderPlacedConsumer().Consume(context.Object);

        var payment = published.Should().ContainSingle().Subject;
        payment.Status.Should().Be("Approved");
        AssertOrderData(payment, order);
        context.Verify(c => c.Publish(It.IsAny<PaymentProcessedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_SetsProcessedAtInUtcWithinExecutionInterval()
    {
        var (context, published) = CreateContext(CreateOrder(10m));
        var consumer = new OrderPlacedConsumer();
        var before = DateTime.UtcNow;

        await consumer.Consume(context.Object);

        var after = DateTime.UtcNow;
        var payment = published.Should().ContainSingle().Subject;
        payment.ProcessedAt.Should().NotBe(default);
        payment.ProcessedAt.Kind.Should().Be(DateTimeKind.Utc);
        payment.ProcessedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public async Task Consume_DifferentMessagesOnSameConsumerKeepTheirOwnData()
    {
        var firstOrder = CreateOrder(19.99m);
        var secondOrder = CreateOrder(249.90m);
        var (firstContext, firstPublished) = CreateContext(firstOrder);
        var (secondContext, secondPublished) = CreateContext(secondOrder);
        var consumer = new OrderPlacedConsumer();

        await consumer.Consume(firstContext.Object);
        await consumer.Consume(secondContext.Object);

        var firstPayment = firstPublished.Should().ContainSingle().Subject;
        var secondPayment = secondPublished.Should().ContainSingle().Subject;
        AssertOrderData(firstPayment, firstOrder);
        AssertOrderData(secondPayment, secondOrder);
        firstPayment.Should().NotBeSameAs(secondPayment);
        firstPayment.Status.Should().Be("Approved");
        secondPayment.Status.Should().Be("Approved");
        firstContext.Verify(c => c.Publish(It.IsAny<PaymentProcessedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        secondContext.Verify(c => c.Publish(It.IsAny<PaymentProcessedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static OrderPlacedEvent CreateOrder(decimal price) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), price, DateTime.UtcNow.AddMinutes(-5));

    private static (Mock<ConsumeContext<OrderPlacedEvent>> Context, List<PaymentProcessedEvent> Published)
        CreateContext(OrderPlacedEvent order)
    {
        var published = new List<PaymentProcessedEvent>();
        var context = new Mock<ConsumeContext<OrderPlacedEvent>>(MockBehavior.Strict);
        context.SetupGet(c => c.Message).Returns(order);
        context.Setup(c => c.Publish(It.IsAny<PaymentProcessedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentProcessedEvent, CancellationToken>((payment, _) => published.Add(payment))
            .Returns(Task.CompletedTask);
        return (context, published);
    }

    private static void AssertOrderData(PaymentProcessedEvent payment, OrderPlacedEvent order)
    {
        payment.OrderId.Should().Be(order.OrderId);
        payment.UserId.Should().Be(order.UserId);
        payment.GameId.Should().Be(order.GameId);
        payment.Price.Should().Be(order.Price);
    }
}
