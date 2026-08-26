package com.baeldung;

import org.springframework.boot.SpringApplication;

// Dev launcher: runs the real application with Mongo and Kafka supplied by
// Testcontainers, so the inner loop needs no separately installed
// infrastructure and no environment for the repo to drift out of sync with.
//
//   mvn spring-boot:test-run -pl inventory-service
//
// Containers are torn down when the process exits. Run it from the IDE
// instead (Run/Debug this class' main) to get a debugger on the app while
// its backing services stay real.
public class TestAsyncApplication {

    public static void main(String[] args) {
        SpringApplication.from(AsyncApplication::main)
            .with(TestcontainersConfiguration.class)
            .withAdditionalProfiles("local")
            .run(args);
    }

}
