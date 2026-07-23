package com.baeldung.reactive.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.verify;

import java.util.List;

import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.SpringBootTest.WebEnvironment;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.http.MediaType;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.test.context.bean.override.mockito.MockitoBean;
import org.springframework.boot.webtestclient.autoconfigure.AutoConfigureWebTestClient;
import org.springframework.test.web.reactive.server.WebTestClient;
import org.testcontainers.junit.jupiter.Container;
import org.testcontainers.junit.jupiter.Testcontainers;
import org.testcontainers.mongodb.MongoDBContainer;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.LineItem;
import com.baeldung.domain.Order;
import com.baeldung.reactive.repository.OrderRepository;

@Testcontainers
@AutoConfigureWebTestClient
@SpringBootTest(webEnvironment = WebEnvironment.RANDOM_PORT)
class OrderServiceIntegrationTest {

    @Container
    @ServiceConnection
    static final MongoDBContainer MONGO_DB_CONTAINER = new MongoDBContainer("mongo:4.4");

    @DynamicPropertySource
    static void disableKafkaListenerAutoStart(DynamicPropertyRegistry registry) {
        registry.add("spring.kafka.listener.auto-startup", () -> "false");
    }

    @Autowired
    private WebTestClient webTestClient;

    @Autowired
    private OrderRepository orderRepository;

    @MockitoBean
    private OrderProducer orderProducer;

    @Test
    void givenValidOrder_whenCreateOrder_thenOrderIsPersistedWithInitiationSuccessStatus() {
        Order order = new Order().setLineItems(List.of(
            new LineItem().setQuantity(3),
            new LineItem().setQuantity(0)));

        Order result = webTestClient.post()
            .uri("/api/orders")
            .headers(headers -> headers.setBasicAuth("test", "test-only-not-a-real-credential"))
            .contentType(MediaType.APPLICATION_JSON)
            .bodyValue(order)
            .exchange()
            .expectStatus().isOk()
            .expectBody(Order.class)
            .returnResult()
            .getResponseBody();

        assertThat(result).isNotNull();
        assertThat(result.getId()).isNotNull();
        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        assertThat(result.getLineItems()).hasSize(1);

        Order persisted = orderRepository.findById(result.getId()).block();
        assertThat(persisted).isNotNull();
        assertThat(persisted.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        verify(orderProducer).sendMessage(any(Order.class));
    }

    @Test
    void whenGetOrders_thenAllPersistedOrdersAreReturned() {
        orderRepository.deleteAll().block();
        Order first = orderRepository.save(new Order().setOrderStatus(OrderStatus.INITIATION_SUCCESS)).block();
        Order second = orderRepository.save(new Order().setOrderStatus(OrderStatus.SUCCESS)).block();

        List<Order> result = webTestClient.get()
            .uri("/api/orders")
            .headers(headers -> headers.setBasicAuth("test", "test-only-not-a-real-credential"))
            .accept(MediaType.APPLICATION_JSON)
            .exchange()
            .expectStatus().isOk()
            .expectBodyList(Order.class)
            .returnResult()
            .getResponseBody();

        assertThat(result).extracting(Order::getId)
            .containsExactlyInAnyOrder(first.getId(), second.getId());
    }

}
