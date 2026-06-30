FROM eclipse-temurin:21-jdk-alpine
COPY target/shipping-service-0.0.1-SNAPSHOT.jar app.jar
ENTRYPOINT ["java","-jar","-Dspring.profiles.active=docker","/app.jar"]
