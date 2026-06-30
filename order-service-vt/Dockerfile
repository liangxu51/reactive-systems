FROM eclipse-temurin:21-jdk-alpine
COPY target/order-service-vt-0.0.1-SNAPSHOT.jar app.jar
ENTRYPOINT ["java","-jar","-Dspring.profiles.active=docker","/app.jar"]
