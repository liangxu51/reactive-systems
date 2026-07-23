package com.baeldung.vt.config;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.config.Customizer;
import org.springframework.security.config.annotation.web.builders.HttpSecurity;
import org.springframework.security.config.annotation.web.configuration.EnableWebSecurity;
import org.springframework.security.config.http.SessionCreationPolicy;
import org.springframework.security.web.SecurityFilterChain;
import org.springframework.security.web.savedrequest.NullRequestCache;

// SEC-001: every endpoint was reachable with zero authentication. Require an
// authenticated principal for every request. No credential is hardcoded here:
// with spring-boot-starter-security on the classpath and no
// spring.security.user.password configured, Spring Boot generates a random
// password each startup and logs it (see console on boot). For a stable
// identity, set SPRING_SECURITY_USER_NAME / SPRING_SECURITY_USER_PASSWORD.
@Configuration
@EnableWebSecurity
public class SecurityConfig {

    @Bean
    SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
        return http
            .csrf(csrf -> csrf.disable())
            // This is a stateless JSON API authenticated per-request via HTTP
            // Basic - without this, the default session/request-cache filters
            // still mint a JSESSIONID on every anonymous request even though
            // it's never used to authenticate.
            .sessionManagement(session -> session.sessionCreationPolicy(SessionCreationPolicy.STATELESS))
            .requestCache(cache -> cache.requestCache(new NullRequestCache()))
            .authorizeHttpRequests(auth -> auth.anyRequest().authenticated())
            .httpBasic(Customizer.withDefaults())
            .build();
    }
}
