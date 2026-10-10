# Es un 10, pero…

Juego social para 2–4 personas, con login de Discord y mesas privadas.
La versión multijugador actual es la web servida por `Server/`. `Assets/` conserva el prototipo Unity local; sus reglas e interfaz no son la versión online.

## Jugar

1. Vincula Discord. El navegador recuerda la sesión; puedes cerrarla desde la cabecera.
2. Crea una mesa o entra con su código.
3. Cuando todos están listos empieza una cuenta atrás de 10 segundos, con sonido opcional.
4. Quien adivina tiene 90 segundos y tres intentos distintos. Los demás ven la carta y dan pistas por una llamada externa o por el chat.
5. «Dame una pista» envía una frase de categoría aleatoria, adecuada al número. Hay tres ayudas compartidas por turno, separadas por ocho segundos.
6. Se revela la carta durante cuatro segundos, incluso al agotarse el tiempo. Tras tres vueltas completas hay clasificación y revancha.

**Puntuación:** acertar da 3 puntos en cualquier intento. Solo al gastar los tres intentos se evalúa el último: si queda a una unidad por arriba o abajo, da 2 puntos; a dos unidades, da 1; más lejos, 0. El tiempo agotado da 0.

**Reparto:** los números del 1 al 10 se barajan y se reparten sin repetirse hasta agotar la baraja. Al volver a mezclar no se repite inmediatamente la última carta. La baraja continúa entre revanchas de la misma mesa.

## Recuperación y conexiones

- Recargar vuelve a la misma mesa con la misma identidad. El código se guarda en el navegador, nunca el número secreto ni credenciales de Discord.
- Tras 25 segundos sin señales se muestra «Reconectando». Hay 60 segundos desde la última señal para volver antes de omitir el turno; si quedan menos de dos participantes disponibles termina la partida.
- Se puede salir en cualquier fase. El anfitrión se transfiere si deja de estar disponible.
- Las mesas sin actividad caducan en 30 minutos. Un reinicio del servidor termina las mesas en memoria y la interfaz explica cómo crear otra.

Consulta [Server/README.md](Server/README.md) para ejecutar y desplegar. Consulta [SECURITY.md](SECURITY.md) para el alcance de la sesión persistente y las protecciones del servidor.

## Comprobaciones

```powershell
dotnet run --project Server.Tests/Server.Tests.csproj -c Release
```

La suite usa un reloj simulado y verifica turnos, repetición de comandos, reconexión, revelación, puntuación, final, revancha, ayudas, desconexiones y sesión persistente. No requiere paquetes de pruebas externos.
