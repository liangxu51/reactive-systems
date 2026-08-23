using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Moq;
using MongoDB.Bson;
using OrderService.Api.Constants;
using OrderService.Api.Domain;
using OrderService.Api.Producers;
using OrderService.Api.Serialization;
using Xunit;

namespace OrderService.Api.Tests.Producers;

public class OrderProducerTests
{
    [Fact]
    public void SendMessage_PublishesToOrdersTopic_KeyedByOrderIdHex_WithSerializedValue()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? captured = null;
        string? capturedTopic = null;
        producerMock
            .Setup(p => p.Produce(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<Action<DeliveryReport<string, string>>>()))
            .Callback<string, Message<string, string>, Action<DeliveryReport<string, string>>>(
                (topic, message, _) =>
                {
                    capturedTopic = topic;
                    captured = message;
                });

        var order = new Order
        {
            Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
            UserId = "user-42",
            OrderStatus = OrderStatus.RESERVE_INVENTORY,
            ResponseMessage = "reserve inventory",
        };

        var producer = new OrderProducer(producerMock.Object, Mock.Of<ILogger<OrderProducer>>());
        producer.SendMessage(order);

        Assert.Equal("orders", capturedTopic);
        Assert.NotNull(captured);
        Assert.Equal("507f1f77bcf86cd799439011", captured!.Key);
        Assert.Equal(JsonSerializer.Serialize(order, OrderJsonOptions.Default), captured.Value);
    }

    [Fact]
    public void SendMessage_DeliveryFailure_LogsError_DoesNotThrow()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        producerMock
            .Setup(p => p.Produce(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<Action<DeliveryReport<string, string>>>()))
            .Callback<string, Message<string, string>, Action<DeliveryReport<string, string>>>(
                (topic, message, deliveryHandler) =>
                {
                    deliveryHandler(new DeliveryReport<string, string>
                    {
                        Topic = topic,
                        Message = message,
                        Error = new Error(ErrorCode.Local_MsgTimedOut, "timed out"),
                    });
                });
        var loggerMock = new Mock<ILogger<OrderProducer>>();

        var order = new Order
        {
            Id = ObjectId.Parse("507f1f77bcf86cd799439011"),
            OrderStatus = OrderStatus.PREPARE_SHIPPING,
        };

        var producer = new OrderProducer(producerMock.Object, loggerMock.Object);
        var exception = Record.Exception(() => producer.SendMessage(order));

        Assert.Null(exception);
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void PublishRaw_SendsValueUnmodified_ToGivenTopicAndKey()
    {
        var producerMock = new Mock<IProducer<string, string>>();
        Message<string, string>? captured = null;
        string? capturedTopic = null;
        producerMock
            .Setup(p => p.Produce(
                It.IsAny<string>(),
                It.IsAny<Message<string, string>>(),
                It.IsAny<Action<DeliveryReport<string, string>>>()))
            .Callback<string, Message<string, string>, Action<DeliveryReport<string, string>>>(
                (topic, message, _) =>
                {
                    capturedTopic = topic;
                    captured = message;
                });

        var producer = new OrderProducer(producerMock.Object, Mock.Of<ILogger<OrderProducer>>());
        const string rawValue = "{ this is not valid json but must be forwarded unmodified }";

        producer.PublishRaw("orders.DLT", "507f1f77bcf86cd799439011", rawValue);

        Assert.Equal("orders.DLT", capturedTopic);
        Assert.NotNull(captured);
        Assert.Equal("507f1f77bcf86cd799439011", captured!.Key);
        Assert.Equal(rawValue, captured.Value);
    }
}
