FROM nginx:stable-alpine

# The stock entrypoint processes docker-entrypoint.d/* in lexical order and
# *sources* executable *.envsh files: 15-encode-basic-auth.envsh exports
# API_AUTH_B64 before 20-envsubst-on-templates.sh renders
# templates/default.conf.template into /etc/nginx/conf.d/default.conf.
COPY docker-entrypoint.d/15-encode-basic-auth.envsh /docker-entrypoint.d/
COPY templates/default.conf.template /etc/nginx/templates/

RUN chmod +x /docker-entrypoint.d/15-encode-basic-auth.envsh

EXPOSE 8080
