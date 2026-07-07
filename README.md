## Reactive Systems in Java

This module contains services for article about reactive systems in Java. Please note that these services comprise parts of a full stack application to demonstrate the capabilities of a reactive system. Unless there is an article which extends on this concept, this is probably not a suitable module to add other code.

## Local development

Run infra (MongoDB + Kafka) in Docker and the service under active development on the host, for fast iteration with full debugger support:

```bash
docker-compose up mongodb mongo-init-replicaset zookeeper kafka
mvn spring-boot:run -pl order-service -Dspring-boot.run.arguments=--spring.kafka.bootstrap-servers=localhost:29092
```

`application.properties` defaults `spring.kafka.bootstrap-servers` to `localhost:9092`, which only resolves inside the Docker network (container-to-container). From the host, Kafka is only reachable on the `29092` port that docker-compose publishes, so the override above is required when running the service outside Docker — otherwise the Kafka consumer/producer silently fail to connect and order creation fails with "Send failed".

`order-service` has `spring-boot-devtools` on the classpath, so it auto-restarts once a source change is recompiled — most IDEs recompile on save automatically; if running from the CLI, rerun `mvn compile -pl order-service` in another terminal to trigger it.

To exercise the API manually:
- Use `order-service/requests.http` with the VS Code REST Client extension or IntelliJ's built-in HTTP Client.
- Or browse `http://localhost:8080/swagger-ui.html` (springdoc-openapi, auto-generated from the controllers).

To run the test suite (`mvn test -pl order-service`), Docker must be running — integration tests spin up a real MongoDB via Testcontainers.
