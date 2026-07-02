package com.baeldung.vt.serdeser;

import static org.assertj.core.api.Assertions.assertThat;

import org.bson.types.ObjectId;
import org.junit.jupiter.api.Test;

import com.baeldung.vt.domain.LineItem;
import com.baeldung.vt.domain.Order;

import tools.jackson.databind.json.JsonMapper;

class ObjectIdValueSerializerUnitTest {

    // Spring MVC serializes responses with Jackson 3 (tools.jackson) under Spring Boot 4; this confirms
    // ObjectIdValueSerializer (not the legacy Jackson-2 ObjectIdSerializer used for Kafka) handles that path.
    private final JsonMapper jsonMapper = JsonMapper.builder().build();

    @Test
    void givenOrderWithObjectIdFields_whenSerializedWithJackson3_thenIdsRenderAsPlainHexStrings() {
        ObjectId orderId = new ObjectId();
        ObjectId productId = new ObjectId();
        Order order = new Order();
        order.setId(orderId);
        order.setLineItems(java.util.List.of(new LineItem().setProductId(productId).setQuantity(1)));

        String json = jsonMapper.writeValueAsString(order);

        assertThat(json).contains("\"id\":\"" + orderId + "\"");
        assertThat(json).contains("\"productId\":\"" + productId + "\"");
    }

}
