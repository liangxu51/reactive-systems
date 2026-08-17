package com.baeldung.reactive.service;

import static org.assertj.core.api.Assertions.assertThat;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.lenient;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

import java.util.List;

import org.bson.types.ObjectId;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.mockito.InjectMocks;
import org.mockito.Mock;
import org.mockito.junit.jupiter.MockitoExtension;
import org.springframework.dao.DuplicateKeyException;

import com.baeldung.constants.OrderStatus;
import com.baeldung.domain.LineItem;
import com.baeldung.domain.Order;
import com.baeldung.domain.Product;
import com.baeldung.domain.ProcessedEvent;
import com.baeldung.reactive.repository.ProcessedEventRepository;
import com.baeldung.reactive.repository.ProductRepository;

import reactor.core.publisher.Mono;
import reactor.test.StepVerifier;

@ExtendWith(MockitoExtension.class)
class ProductServiceUnitTest {

    @Mock
    private ProductRepository productRepository;

    @Mock
    private ProcessedEventRepository processedEventRepository;

    @InjectMocks
    private ProductService productService;

    private ObjectId orderId;
    private ObjectId productId;
    private Product product;
    private Order order;

    @BeforeEach
    void setUp() {
        orderId = new ObjectId();
        productId = new ObjectId();
        product = new Product();
        product.setId(productId);
        product.setStock(5);

        order = new Order();
        order.setId(orderId);
        order.setLineItems(List.of(
            new LineItem().setProductId(productId).setQuantity(3)));

        // lenient: the two duplicate-event tests below override this with an error stub,
        // which would otherwise make Mockito's strict stubbing flag this one as unused there.
        lenient().when(processedEventRepository.insert(any(ProcessedEvent.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));
    }

    @Test
    void givenSufficientStock_whenHandleOrder_thenStockIsDecrementedAndOrderMarkedSuccess() {
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.handleOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(2);
        verify(productRepository).save(product);
    }

    @Test
    void givenInsufficientStock_whenHandleOrder_thenErrorsAndStockIsUnchanged() {
        product.setStock(1);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));

        StepVerifier.create(productService.handleOrder(order))
            .expectErrorMessage("Product is out of stock: " + productId)
            .verify();

        assertThat(product.getStock()).isEqualTo(1);
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenExactStockMatchesQuantity_whenHandleOrder_thenStockReachesZeroAndOrderMarkedSuccess() {
        product.setStock(3);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.handleOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(0);
    }

    @Test
    void givenReservedOrder_whenRevertOrder_thenStockIsRestoredAndOrderMarkedSuccess() {
        product.setStock(2);
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));
        when(productRepository.save(any(Product.class))).thenAnswer(invocation -> Mono.just(invocation.getArgument(0)));

        StepVerifier.create(productService.revertOrder(order))
            .assertNext(result -> assertThat(result.getOrderStatus()).isEqualTo(OrderStatus.SUCCESS))
            .verifyComplete();

        assertThat(product.getStock()).isEqualTo(5);
        verify(productRepository).save(product);
    }

    @Test
    void givenNegativeQuantity_whenHandleOrder_thenErrorsAndStockIsUnchanged() {
        order.setLineItems(List.of(new LineItem().setProductId(productId).setQuantity(-3)));
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));

        StepVerifier.create(productService.handleOrder(order))
            .expectErrorMessage("Invalid quantity for product " + productId + ": -3")
            .verify();

        assertThat(product.getStock()).isEqualTo(5);
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenZeroQuantity_whenHandleOrder_thenErrorsAndStockIsUnchanged() {
        order.setLineItems(List.of(new LineItem().setProductId(productId).setQuantity(0)));
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));

        StepVerifier.create(productService.handleOrder(order))
            .expectErrorMessage("Invalid quantity for product " + productId + ": 0")
            .verify();

        assertThat(product.getStock()).isEqualTo(5);
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenNegativeQuantity_whenRevertOrder_thenErrorsAndStockIsUnchanged() {
        order.setLineItems(List.of(new LineItem().setProductId(productId).setQuantity(-3)));
        when(productRepository.findById(productId)).thenReturn(Mono.just(product));

        StepVerifier.create(productService.revertOrder(order))
            .expectErrorMessage("Invalid quantity for product " + productId + ": -3")
            .verify();

        assertThat(product.getStock()).isEqualTo(5);
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenDuplicateReserveInventoryEvent_whenHandleOrder_thenErrorsWithDuplicateKeyAndStockIsUnchanged() {
        order.setOrderStatus(OrderStatus.RESERVE_INVENTORY);
        when(processedEventRepository.insert(any(ProcessedEvent.class))).thenReturn(Mono.error(new DuplicateKeyException("duplicate key")));

        // Deliberately NOT resumed to empty here - see OrderConsumer, which catches this
        // *after* handleOrder returns, outside the @Transactional boundary. Catching it
        // inside would make Spring attempt to commit an already-aborted Mongo transaction.
        StepVerifier.create(productService.handleOrder(order))
            .expectError(DuplicateKeyException.class)
            .verify();

        verify(productRepository, never()).findById(any(ObjectId.class));
        verify(productRepository, never()).save(any(Product.class));
    }

    @Test
    void givenDuplicateRevertInventoryEvent_whenRevertOrder_thenErrorsWithDuplicateKeyAndStockIsUnchanged() {
        order.setOrderStatus(OrderStatus.REVERT_INVENTORY);
        when(processedEventRepository.insert(any(ProcessedEvent.class))).thenReturn(Mono.error(new DuplicateKeyException("duplicate key")));

        StepVerifier.create(productService.revertOrder(order))
            .expectError(DuplicateKeyException.class)
            .verify();

        verify(productRepository, never()).findById(any(ObjectId.class));
        verify(productRepository, never()).save(any(Product.class));
    }

}
