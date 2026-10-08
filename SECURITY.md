# Seguridad del prototipo

## Estado actual

Este prototipo funciona completamente dentro del cliente Unity. Por ello, el número secreto está oculto en la interfaz, pero sigue existiendo en la memoria del proceso local. Una persona con acceso al cliente puede inspeccionarlo o modificarlo. Esto es una limitación inevitable del modo offline y no debe considerarse una protección antitrampas.

El script aplica validaciones básicas para evitar estados inválidos:

- Solo se aceptan números del 1 al 10.
- Solo el jugador local activo puede enviar una adivinanza o pista.
- Las pistas se recortan a 120 caracteres.
- Se ignoran acciones cuando no hay una partida activa.
- Los turnos simulados se resuelven internamente y no aceptan entradas del usuario.

## Reglas para la versión online

La versión multijugador debe usar un servidor autoritativo. El cliente enviará únicamente comandos, por ejemplo `SubmitClue` o `SubmitGuess`; nunca decidirá el número secreto, los puntos, el turno ni el resultado.

El servidor debe:

1. Generar y conservar los números secretos.
2. Validar identidad, sala, fase, turno y límites de cada comando.
3. Enviar una vista filtrada del estado: el propietario recibe su carta oculta y los demás reciben el número visible.
4. Calcular la puntuación y emitir el resultado.
5. Rechazar comandos duplicados, fuera de tiempo o fuera de turno.
6. Limitar frecuencia y tamaño de mensajes para evitar abuso.

La interfaz nunca debe ocultar un secreto que ya fue enviado al cliente propietario; la información sensible debe omitirse desde el servidor.

## Identidad Discord

La integración usa Discord OAuth2 con `state` aleatorio guardado en una cookie de sesión. La página web no recibe el `ClientSecret`; las credenciales se configuran solo en el servidor como variables de entorno. `Server/appsettings.json` está excluido del control de versiones.

El archivo de ejemplo tuvo credenciales OAuth reales y se sustituyeron por marcadores. Si ese secreto estuvo en un repositorio remoto o se compartió, rótalo en Discord Developer Portal y actualiza `Discord__ClientSecret` en el servidor.
