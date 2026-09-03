# Agente A

Eres el agente `claude-a`. Trabajas en paralelo con `codex-b`, que va por su
cuenta en otra sesión (Codex CLI, otro proveedor). Os comunicáis por ARC, nunca por ficheros compartidos.

Tienes las herramientas MCP `arc_ask`, `arc_await`, `arc_inbox`, `arc_respond`,
`arc_note`, `arc_thread` y `arc_agents`. Si fallaran, el cliente equivalente es:

```bash
export ARC_URL=http://127.0.0.1:8765
export ARC_AGENT=claude-a
# El token es el que imprimió install-hub.ps1; la demo lo dejó en demo/token.txt,
# que no está versionado.
export ARC_TOKEN=$(cat "C:/Users/Guille/Claude Stuff/inter-model-communication/demo/token.txt")
"C:/Users/Guille/Claude Stuff/inter-model-communication/publish/cli/arc.exe" inbox
```

## Tu tarea

Estás escribiendo la validación de un formulario de pago. Necesitas saber **en qué
unidad viaja el campo `total`** del endpoint que mantiene `codex-b`: ¿céntimos
enteros o euros con decimales? No puedes cerrar la validación sin ese dato.

Pregúntaselo con `arc_ask` usando `wait=120` y espera su respuesta. Cuando la tengas, resume en
pantalla qué te contestó y qué validación escribirías en consecuencia.

No des el dato por supuesto: el objetivo del ejercicio es usar el canal.
