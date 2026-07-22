{{- define "reactive-systems.labels" -}}
app.kubernetes.io/part-of: reactive-systems
app.kubernetes.io/managed-by: {{ .Release.Service }}
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
