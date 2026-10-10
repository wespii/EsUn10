# Servidor web multijugador

ASP.NET Core (.NET 8), cliente HTML/CSS/JavaScript y SignalR. El servidor decide números, fases, plazos e intentos. La voz se realiza en una llamada externa de Discord.

## Ejecutar

1. Configura `Discord__ClientId`, `Discord__ClientSecret` y `Discord__RedirectUri` como variables de entorno, o usa un `Server/appsettings.json` local excluido del repositorio.
2. Registra la callback exacta en Discord. En local: `http://localhost:5080/auth/discord/callback`.
3. Ejecuta `dotnet run --project Server/EsUn10Pero.Server.csproj`.

`POST /dev/login` solo existe en Development para pruebas. Nunca configures Development en Render.

## Desplegar en Render

`render.yaml` y `Server/Dockerfile` definen un servicio Docker gratuito. Configura las tres variables de Discord y la callback HTTPS del dominio del servicio.

Las mesas son temporales: se pierden al reiniciar el proceso. No se promete persistencia de partidas en el disco efímero de Render. La web informa de mesas terminadas/caducadas y conserva el inicio de sesión mediante una cookie firmada con una clave derivada del secreto estable del servidor. Si rotas ese secreto, las sesiones recordadas anteriores dejan de ser válidas.

## API

- `GET /auth/me`, `GET /auth/discord`, `POST /auth/logout`
- `POST /rooms`, `GET /rooms/{code}`, `POST /rooms/{code}/join`
- `POST /rooms/{code}/ready`, `POST /rooms/{code}/leave`, `POST /rooms/{code}/rematch`
- `POST /rooms/{code}/guess`: `{ "number": 7, "turnId": "…", "requestId": "UUID" }`
- `POST /rooms/{code}/clue`: `{ "text": "…", "turnId": "…" }`
- `POST /rooms/{code}/hint`: `{ "turnId": "…" }`; la categoría se elige exclusivamente en el servidor.
- `POST /rooms/{code}/hint-rating`: `{ "turnId": "…", "helpful": true }`; solo el adivinador y durante la revelación, una vez.
- `GET /health`: salud e identificador de instancia; no expone datos de las mesas.

El reparto usa una baraja aleatoria del 1 al 10 sin reposición, conservada entre revanchas. Al remezclar evita repetir inmediatamente la carta anterior. Los puntos son 3 por acierto; tras el tercer fallo, 2 si el último número queda a ±1, 1 si queda a ±2 y 0 en el resto. El tiempo agotado da 0.

Los estados son `lobby`, `playing`, `reveal` y `finished`. Cada vista incluye una versión creciente y la hora del servidor. La web descarta respuestas antiguas y ajusta su reloj.

## Tiempo real y límites

Hub `/realtime`: `JoinRoom(code)`, `LeaveRoom()` y `Pulse(code)`. El navegador se autentica con su cookie; Unity puede usar su token. El único evento compartido es `roomChanged(code)`; cada cliente consulta su propia vista filtrada. No se difunden cartas o estados personalizados al grupo.

SignalR avisa de los cambios. Una señal cada 10 segundos mantiene la presencia; una consulta de respaldo cada 30 segundos recupera eventos perdidos. Los fallos usan reintentos progresivos. El campo de pista y el historial no se reconstruyen con cada evento.

Máximo 120 peticiones HTTP por minuto por identidad/IP, tres ayudas por turno con ocho segundos entre ellas, pistas manuales separadas por dos segundos y un máximo de 40 mensajes por turno. Las peticiones de adivinanza incluyen turno y UUID, y los números repetidos se rechazan sin consumir intentos.

## Equilibrio de pistas

`HintCatalog.cs` contiene 120 frases asignadas individualmente a los valores 1–10. La puntuación editorial no convierte una opinión en un número objetivamente único. La valoración de la revelación escribe en los logs `HintFeedback` el índice del catálogo y un voto, sin identidad ni texto privado. Usa estas valoraciones y las sesiones con jugadores reales para revisar las frases ambiguas; los logs no constituyen almacenamiento permanente.

## Archivos

- `Program.cs`: rutas, OAuth y SignalR.
- `Room.cs`: reglas y vistas privadas.
- `RoomStore.cs`: registro, limpieza y reloj de las mesas.
- `RememberLogin.cs`: sesión recordada.
- `wwwroot/app.js`, `styles.css`, `index.html`: interfaz.
- `wwwroot/vendor`: cliente oficial SignalR 8.0.29 y licencia MIT.
