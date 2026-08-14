{{- define "reactive-systems.labels" -}}
app.kubernetes.io/part-of: reactive-systems
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/*
imagePullSecrets for pulling app-service images from a real registry (#58)
- empty by default (values.yaml), zero behavior change for the local-
minikube flow where images are built straight into minikube's own Docker
daemon and never need a pull secret. Only CI's ephemeral-cluster smoke test
and a real deploy-target cluster set this, via
--set imagePullSecrets[0].name=....
*/}}
{{- define "reactive-systems.imagePullSecrets" -}}
{{- if .Values.imagePullSecrets -}}
imagePullSecrets:
  {{- toYaml .Values.imagePullSecrets | nindent 0 }}
{{- end }}
{{- end -}}

{{/*
Env vars for the shared app-service Mongo user (see mongodb.yaml/mongo-secret.yaml,
issue #33). SPRING_MONGODB_URI overrides application-docker.properties'
credential-free default via Spring's relaxed env-var binding. MONGO_APP_PASSWORD
must come first in the list so Kubernetes' $(VAR) expansion can reference it.
authSource=admin because the app user is created in the admin db with roles
scoped to reactive-systems, not created inside reactive-systems itself.
*/}}
{{- define "reactive-systems.mongoUriEnv" -}}
- name: MONGO_APP_PASSWORD
  valueFrom:
    secretKeyRef:
      name: mongo-credentials
      key: app-password
- name: SPRING_MONGODB_URI
  value: "mongodb://{{ .Values.mongodb.auth.appUsername }}:$(MONGO_APP_PASSWORD)@{{ .Values.mongodb.serviceName }}:{{ .Values.mongodb.port }}/reactive-systems?replicaSet=rs0&authSource=admin"
{{- end -}}

{{/*
Env vars for the shared HTTP Basic API credential (SEC-001, PR #72) - without
this, Spring Boot's default security auto-config generates a new random
password every boot, logged once and unrecoverable after. Setting these two
env vars overrides that default with a stable identity shared across
order-service/order-service-vt/inventory-service (see MODERNIZATION_BRIEF.md's
target architecture note on a shared operator credential). See
templates/api-secret.yaml for how the password is generated and persisted.
*/}}
{{- define "reactive-systems.apiAuthEnv" -}}
- name: SPRING_SECURITY_USER_NAME
  value: {{ .Values.api.auth.username | quote }}
- name: SPRING_SECURITY_USER_PASSWORD
  valueFrom:
    secretKeyRef:
      name: api-credentials
      key: password
{{- end -}}
