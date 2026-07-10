package com.baeldung.async.consumer;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.times;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.bson.types.ObjectId;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.ArgumentCaptor;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;

import com.baeldung.async.producer.OrderProducer;
import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.Address;
import com.baeldung.domain.LineItem;
import com.baeldung.domain.Order;
import com.baeldung.reactive.repository.OrderRepository;

import reactor.core.publisher.Mono;

@ExtendWith(MockitoExtension.class)
class OrderConsumerUnitTest {

    @Mock
    private OrderRepository orderRepository;

    @Mock
    private OrderProducer orderProducer;

    @InjectMocks
    private OrderConsumer orderConsumer;

    private ObjectId orderId;
    private Order existing;

    @BeforeEach
    void setUp() {
        orderId = new ObjectId();
        existing = new Order();
        existing.setId(orderId);
        existing.setUserId("user-1");
        existing.setTotal(100L);
        existing.setLineItems(List.of(new LineItem().setProductId(new ObjectId()).setQuantity(2)));
        Address address = new Address();
        address.setName("Test User");
        address.setCity("Testville");
        existing.setShippingAddress(address);

        when(orderRepository.findById(orderId)).thenReturn(Mono.just(existing));
        when(orderRepository.save(any(Order.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));
    }

    @Test
    void givenInventoryFailure_whenConsume_thenOrderStatusSavedButNoFurtherMessagePublished() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INVENTORY_FAILURE);
        incoming.setResponseMessage("Product is out of stock");

        orderConsumer.consume(incoming);

        verify(orderRepository).save(existing);
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void givenInventoryRevertFailure_whenConsume_thenOrderStatusSavedButNoFurtherMessagePublished() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INVENTORY_REVERT_FAILURE);
        incoming.setResponseMessage("Failed to restore stock");

        orderConsumer.consume(incoming);

        verify(orderRepository).save(existing);
        verify(orderProducer, never()).sendMessage(any(Order.class));
    }

    @Test
    void givenInitiationSuccess_whenConsume_thenNextStatusMessagePublished() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INITIATION_SUCCESS);

        orderConsumer.consume(incoming);

        verify(orderProducer, times(1)).sendMessage(any(Order.class));
    }

    @Test
    void givenInitiationSuccess_whenConsume_thenOutboundMessageCarriesFullOrderData() {
        Order incoming = new Order();
        incoming.setId(orderId);
        incoming.setOrderStatus(OrderStatus.INITIATION_SUCCESS);

        orderConsumer.consume(incoming);

        ArgumentCaptor<Order> outboundCaptor = ArgumentCaptor.forClass(Order.class);
        verify(orderProducer).sendMessage(outboundCaptor.capture());

        Order outbound = outboundCaptor.getValue();
        assertThat(outbound.getOrderStatus()).isEqualTo(OrderStatus.RESERVE_INVENTORY);
        assertThat(outbound.getId()).isEqualTo(orderId);
        assertThat(outbound.getUserId()).isEqualTo(existing.getUserId());
        assertThat(outbound.getTotal()).isEqualTo(existing.getTotal());
        assertThat(outbound.getLineItems()).isEqualTo(existing.getLineItems());
        assertThat(outbound.getShippingAddress()).isEqualTo(existing.getShippingAddress());
    }

}
