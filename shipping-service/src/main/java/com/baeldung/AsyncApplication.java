package com.baeldung;

import java.time.Clock;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.context.annotation.Bean;
import org.springframework.data.mongodb.ReactiveMongoDatabaseFactory;
import org.springframework.data.mongodb.ReactiveMongoTransactionManager;

@SpringBootApplication
public class AsyncApplication {

    public static void main(String[] args) {
        SpringApplication.run(AsyncApplication.class, args);
    }

    @Bean
    public Clock clock() {
        return Clock.systemDefaultZone();
    }

    // Issue #48: ShippingService.handleOrder's dedup-marker insert and its
    // shipment insert need to commit or roll back together - without a
    // transaction, a crash between the two would mark PREPARE_SHIPPING as
    // "already handled" while silently never having created the shipment.
    // Mirrors inventory-service's identical bean (AsyncApplication there),
    // which needs the same replica-set-backed Mongo this service already
    // connects to (see application-docker.properties' ?replicaSet=rs0).
    @Bean
    ReactiveMongoTransactionManager transactionManager(ReactiveMongoDatabaseFactory dbFactory) {
        return new ReactiveMongoTransactionManager(dbFactory);
    }

}
