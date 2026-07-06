{{- define "reactive-systems.labels" -}}
app.kubernetes.io/part-of: reactive-systems
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}
