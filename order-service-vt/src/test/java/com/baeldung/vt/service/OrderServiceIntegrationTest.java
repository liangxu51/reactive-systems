package com.baeldung.vt.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.verify;

import java.util.List;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.SpringBootTest.WebEnvironment;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.testcontainers.mongodb.MongoDBContainer;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.LineItem;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;

@Testcontainers
@SpringBootTest(webEnvironment = WebEnvironment.NONE)
class OrderServiceIntegrationTest {

    @Container
    @ServiceConnection
    static final MongoDBContainer MONGO_DB_CONTAINER = new MongoDBContainer("mongo:7.0");

    @DynamicPropertySource
    static void disableKafkaListenerAutoStart(DynamicPropertyRegistry registry) {
        registry.add("spring.kafka.listener.auto-startup", () -> "false");
    }

    @Autowired
    private OrderService orderService;

    @Autowired
    private OrderRepository orderRepository;

    @MockitoBean
    private OrderProducer orderProducer;

    @Test
    void givenValidOrder_whenCreateOrder_thenOrderIsPersistedWithInitiationSuccessStatus() {
        Order order = new Order().setLineItems(List.of(
            new LineItem().setQuantity(3),
            new LineItem().setQuantity(0)));

        Order result = orderService.createOrder(order);

        assertThat(result.getId()).isNotNull();
        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        assertThat(result.getLineItems()).hasSize(1);

        Order persisted = orderRepository.findById(result.getId()).orElseThrow();
        assertThat(persisted.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        verify(orderProducer).sendMessage(any(Order.class));
    }

    @Test
    void whenGetOrders_thenAllPersistedOrdersAreReturned() {
        orderRepository.deleteAll();
        Order first = orderRepository.save(new Order().setOrderStatus(OrderStatus.INITIATION_SUCCESS));
        Order second = orderRepository.save(new Order().setOrderStatus(OrderStatus.SUCCESS));

        List<Order> result = orderService.getOrders();

        assertThat(result).extracting(Order::getId)
            .containsExactlyInAnyOrder(first.getId(), second.getId());
    }

}
