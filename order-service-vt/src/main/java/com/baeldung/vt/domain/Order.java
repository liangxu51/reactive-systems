package com.baeldung.vt.domain;

import java.util.Date;
import java.util.List;

import org.bson.types.ObjectId;
import org.springframework.data.annotation.Id;
import org.springframework.data.mongodb.core.mapping.Document;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.serdeser.ObjectIdSerializer;
import com.baeldung.vt.serdeser.ObjectIdValueSerializer;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;

import lombok.Data;

@Data
@Document
public class Order {

    @Id
    // Spring Kafka's JSON (de)serializer still runs on Jackson 2, while Spring MVC runs on Jackson 3 - both annotations are needed.
    @JsonSerialize(using = ObjectIdSerializer.class)
    @tools.jackson.databind.annotation.JsonSerialize(using = ObjectIdValueSerializer.class)
    private ObjectId id;
    private String userId;
    private List<LineItem> lineItems;
    private Long total;
    private String paymentMode;
    private Address shippingAddress;
    private Date shippingDate;
    private OrderStatus orderStatus;
    private String responseMessage;

    public Order setLineItems(List<LineItem> lineItems) {
        this.lineItems = lineItems;
        return this;
    }

    public Order setShippingDate(Date shippingDate) {
        this.shippingDate = shippingDate;
        return this;
    }

    public Order setOrderStatus(OrderStatus orderStatus) {
        this.orderStatus = orderStatus;
        return this;
    }

    public Order setResponseMessage(String responseMessage) {
        this.responseMessage = responseMessage;
        return this;
    }

    // SEC-004: Lombok's @Data toString() recursed into shippingAddress
    // (name/house/street/city/zip) and userId, so every log.info(..., order)
    // call across the saga wrote unredacted customer PII to plaintext logs.
    // Override toString() to log only non-PII fields Lombok would otherwise
    // include automatically.
    @Override
    public String toString() {
        return "Order[id=" + id + ", orderStatus=" + orderStatus
            + ", lineItems=" + (lineItems == null ? 0 : lineItems.size()) + " items]";
    }

}
