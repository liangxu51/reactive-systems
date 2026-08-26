package com.baeldung.vt;

import org.springframework.boot.SpringApplication;

// Dev launcher: runs the real application with Mongo and Kafka supplied by
// Testcontainers, so the inner loop needs no separately installed
// infrastructure and no environment for the repo to drift out of sync with.
//
//   mvn spring-boot:test-run -pl order-service-vt
//
// Listens on 8083 (see application.properties), so it can run alongside
// order-service on 8080 for comparing the virtual-thread and reactive stacks
// side by side. Containers are torn down when the process exits.
public class TestVirtualThreadOrderApplication {

    public static void main(String[] args) {
        SpringApplication.from(VirtualThreadOrderApplication::main)
            .with(TestcontainersConfiguration.class)
            .withAdditionalProfiles("local")
            .run(args);
    }

}
