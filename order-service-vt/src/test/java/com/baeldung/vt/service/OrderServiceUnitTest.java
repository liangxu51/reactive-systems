package com.baeldung.vt.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.doThrow;
import static org.mockito.Mockito.inOrder;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InOrder;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import com.baeldung.vt.constants.OrderStatus;
import com.baeldung.vt.domain.LineItem;
import com.baeldung.vt.domain.Order;
import com.baeldung.vt.producer.OrderProducer;
import com.baeldung.vt.repository.OrderRepository;

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
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> invocation.getArgument(0));

        Order result = orderService.createOrder(order);

        assertThat(result.getLineItems()).hasSize(1);
        assertThat(result.getLineItems().get(0).getQuantity()).isEqualTo(2);
    }

    @Test
    void givenValidOrder_whenCreateOrder_thenSavedTwiceAndPublishedAsInitiationSuccess() {
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> invocation.getArgument(0));

        Order result = orderService.createOrder(order);

        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        verify(orderRepository, times(2)).save(any(Order.class));
        verify(orderProducer, times(1)).sendMessage(any(Order.class));

        // The Kafka publish must happen only after the order is durably persisted as
        // INITIATION_SUCCESS - i.e. after both save() calls, never before.
        InOrder inOrder = inOrder(orderRepository, orderProducer);
        inOrder.verify(orderRepository, times(2)).save(any(Order.class));
        inOrder.verify(orderProducer).sendMessage(any(Order.class));
    }

    @Test
    void givenRepositorySaveThrows_whenCreateOrder_thenOrderMarkedAsFailureAndNotPublished() {
        when(orderRepository.save(any(Order.class)))
            .thenThrow(new RuntimeException("db unavailable"))
            .thenAnswer(invocation -> invocation.getArgument(0));

        Order result = orderService.createOrder(order);

        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.FAILURE);
        assertThat(result.getResponseMessage()).isEqualTo("db unavailable");
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void givenSecondRepositorySaveThrows_whenCreateOrder_thenOrderMarkedAsFailureAndNotPublished() {
        when(orderRepository.save(any(Order.class)))
            .thenAnswer(invocation -> invocation.getArgument(0))
            .thenThrow(new RuntimeException("db conflict on status update"))
            .thenAnswer(invocation -> invocation.getArgument(0));

        Order result = orderService.createOrder(order);

        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.FAILURE);
        assertThat(result.getResponseMessage()).isEqualTo("db conflict on status update");
        verify(orderRepository, times(3)).save(any(Order.class));
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void givenBothSavesSucceedButSendMessageThrows_whenCreateOrder_thenOrderRemainsInitiationSuccess() {
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> invocation.getArgument(0));
        doThrow(new RuntimeException("kafka broker unreachable")).when(orderProducer)
            .sendMessage(any(Order.class));

        Order result = orderService.createOrder(order);

        assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.INITIATION_SUCCESS);
        verify(orderRepository, times(2)).save(any(Order.class));
        verify(orderProducer, times(1)).sendMessage(any(Order.class));
    }

    @Test
    void whenGetOrders_thenRepositoryFindAllIsReturned() {
        when(orderRepository.findAll()).thenReturn(List.of(order));

        List<Order> result = orderService.getOrders();

        assertThat(result).containsExactly(order);
    }

}
