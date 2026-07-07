package com.baeldung.reactive.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.LineItem;
import com.baeldung.domain.Order;
import com.baeldung.reactive.repository.OrderRepository;

import reactor.core.publisher.Flux;
import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

@ExtendWith(MockitoExtension.class)
class OrderServiceUnitTest {

    @Mock
    private OrderRepository orderRepository;

    @Mock
    private OrderProducer orderProducer;

    @InjectMocks
    private OrderService orderService;

    private Order order;

    @BeforeEach
    void setUp() {
        order = new Order().setLineItems(List.of(
            new LineItem().setQuantity(2),
            new LineItem().setQuantity(0)));
    }

    @Test
    void givenOrderWithZeroQuantityLineItems_whenCreateOrder_thenLineItemsAreFiltered() {
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(orderService.createOrder(order))
            .assertNext(result -> {
                assertThat(result.getLineItems()).hasSize(1);
                assertThat(result.getLineItems().get(0).getQuantity()).isEqualTo(2);
            })
            .verifyComplete();
    }

    @Test
    void givenValidOrder_whenCreateOrder_thenSavedTwiceAndPublishedAsInitiationSuccess() {
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(orderService.createOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS))
            .verifyComplete();

        verify(orderRepository, times(2)).save(any(Order.class));
        verify(orderProducer, times(1)).sendMessage(any(Order.class));
    }

    @Test
    void givenRepositorySaveThrows_whenCreateOrder_thenOrderMarkedAsFailureAndNotPublished() {
        when(orderRepository.save(any(Order.class)))
            .thenReturn(Mono.error(new RuntimeException("db unavailable")))
            .thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(orderService.createOrder(order))
            .assertNext(result -> {
                assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.FAILURE);
                assertThat(result.getResponseMessage()).isEqualTo("db unavailable");
            })
            .verifyComplete();

        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void whenGetOrders_thenRepositoryFindAllIsReturned() {
        when(orderRepository.findAll()).thenReturn(Flux.just(order));

        StepVerifier.create(orderService.getOrders())
            .expectNext(order)
            .verifyComplete();
    }

}
