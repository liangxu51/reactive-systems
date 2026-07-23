package com.baeldung.domain;

import java.util.List;

import org.bson.types.ObjectId;
import org.springframework.data.annotation.Id;
import org.springframework.data.mongodb.core.mapping.Document;

import com.baeldung.constants.OrderStatus;
import com.baeldung.serdeser.ObjectIdSerializer;
import com.baeldung.serdeser.ObjectIdValueSerializer;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.databind.annotation.JsonSerialize;

import lombok.Data;

@Data
@Document
@JsonIgnoreProperties(ignoreUnknown = true)
public class Order {

    @Id
    // Spring Kafka's JSON (de)serializer still runs on Jackson 2, while Spring WebFlux runs on Jackson 3 - both annotations are needed.
    @JsonSerialize(using = ObjectIdSerializer.class)
    @tools.jackson.databind.annotation.JsonSerialize(using = ObjectIdValueSerializer.class)
    private ObjectId id;
    private String userId;
    private List<LineItem> lineItems;
    private Long total;
    private OrderStatus orderStatus;
    private String responseMessage;

    public Order setOrderStatus(OrderStatus orderStatus) {
        this.orderStatus = orderStatus;
        return this;
    }

    public Order setResponseMessage(String responseMessage) {
        this.responseMessage = responseMessage;
        return this;
    }

    // SEC-004: Lombok's @Data toString() included userId, so every
    // log.info(..., order) call across the saga wrote a customer identifier
    // to plaintext logs. Override toString() to log only non-PII fields
    // Lombok would otherwise include automatically.
    @Override
    public String toString() {
        return "Order[id=" + id + ", orderStatus=" + orderStatus
            + ", lineItems=" + (lineItems == null ? 0 : lineItems.size()) + " items]";
    }

}
