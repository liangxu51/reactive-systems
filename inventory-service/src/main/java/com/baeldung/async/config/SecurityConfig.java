package com.baeldung.async.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.web.reactive.EnableWebFluxSecurity;
import org.springframework.security.config.web.server.ServerHttpSecurity;
import org.springframework.security.web.server.SecurityWebFilterChain;

// SEC-001: every endpoint was reachable with zero authentication. Require an
// authenticated principal for every request. No credential is hardcoded here:
// with spring-boot-starter-security on the classpath and no
// spring.security.user.password configured, Spring Boot generates a random
// password each startup and logs it (see console on boot). For a stable
// identity, set SPRING_SECURITY_USER_NAME / SPRING_SECURITY_USER_PASSWORD.
@Configuration
@EnableWebFluxSecurity
public class SecurityConfig {

    @Bean
    SecurityWebFilterChain securityWebFilterChain(ServerHttpSecurity http) {
        return http
            .csrf(ServerHttpSecurity.CsrfSpec::disable)
            .authorizeExchange(exchanges -> exchanges.anyExchange().authenticated())
            .httpBasic(Customizer.withDefaults())
            .build();
    }
}
