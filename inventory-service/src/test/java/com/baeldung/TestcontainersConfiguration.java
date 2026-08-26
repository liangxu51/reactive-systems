package com.baeldung;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.context.annotation.Bean;
import org.testcontainers.kafka.ConfluentKafkaContainer;
import org.testcontainers.mongodb.MongoDBContainer;
import org.testcontainers.utility.DockerImageName;

// Backing services for the local dev run (see TestAsyncApplication). The
// images match what the Helm chart deploys, so a developer's Mongo and Kafka
// behave like the cluster's without anything to keep in sync by hand.
//
// @ServiceConnection rather than explicit property overrides: it derives the
// connection properties from the container itself, so this keeps working
// across the Spring Boot property renames that a hardcoded spring.*.uri key
// would silently miss.
@TestConfiguration(proxyBeanMethods = false)
public class TestcontainersConfiguration {

    // MongoDBContainer starts a single-node replica set, which this service
    // needs regardless of clustering: the reactive driver's change streams
    // and transactions are replica-set-only features.
    @Bean
    @ServiceConnection
    MongoDBContainer mongoDbContainer() {
        return new MongoDBContainer(DockerImageName.parse("mongo:4.4"));
    }

    @Bean
    @ServiceConnection
    ConfluentKafkaContainer kafkaContainer() {
        return new ConfluentKafkaContainer(DockerImageName.parse("confluentinc/cp-kafka:7.4.0"));
    }

}
