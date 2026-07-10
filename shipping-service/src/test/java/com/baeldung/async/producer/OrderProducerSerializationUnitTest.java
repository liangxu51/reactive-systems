package com.baeldung.async.producer;

import static org.assertj.core.api.Assertions.assertThat;

import java.nio.charset.StandardCharsets;
import java.util.Date;

import org.junit.jupiter.api.Test;
import org.springframework.kafka.support.serializer.JsonSerializer;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Order;

class OrderProducerSerializationUnitTest {

    // order-service's copy of Order declares shippingDate as java.util.Date. If shipping-service
    // ever serializes it as a JSON array (e.g. by switching back to a java.time type without
    // disabling WRITE_DATES_AS_TIMESTAMPS), order-service can no longer deserialize the message,
    // which poisons its Kafka consumer and stalls every order, not just this one.
    @Test
    void givenOrderWithShippingDate_whenSerialize_thenShippingDateIsNotAJsonArray() {
        Order order = new Order().setOrderStatus(OrderStatus.SHIPPING_SUCCESS);
        order.setShippingDate(new Date());

        try (JsonSerializer<Order> serializer = new JsonSerializer<>()) {
            byte[] bytes = serializer.serialize("orders", order);
            String json = new String(bytes, StandardCharsets.UTF_8);
            assertThat(json).doesNotContain("\"shippingDate\":[");
        }
    }

}
