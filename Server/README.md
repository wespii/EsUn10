# Servidor online

Servidor ASP.NET Core con login de Discord, salas y juego por turnos autoritativo. Incluye una interfaz web de cartas servida desde `/`.

## Ejecutar en local

1. Instala el SDK de .NET 8.
2. Registra `http://localhost:5080/auth/discord/callback` como redirect URI en tu aplicación de Discord.
3. Copia `appsettings.example.json` a `appsettings.json` y rellena las credenciales de Discord. No subas ese archivo.
4. Ejecuta `dotnet run --project .\Server\EsUn10Pero.Server.csproj` y abre `http://localhost:5080`.

## Publicar sin coste fijo

`render.yaml` y `Server/Dockerfile` preparan un Web Service Free en Render. Para publicarlo, conecta un repositorio **privado** a Render usando Blueprint. En las variables secretas de Render configura:

- `Discord__ClientId`
- `Discord__ClientSecret`
- `Discord__RedirectUri` con `https://<servicio>.onrender.com/auth/discord/callback`

Registra esa misma callback URL en el portal de Discord. Al desplegar, la página en `https://<servicio>.onrender.com` permite vincular Discord, crear una mesa, compartir el código, marcarse listo, repartir cartas y jugar con intentos, pistas, categorías y reloj. La llamada de voz se realiza por Discord.

El plan gratuito duerme el servicio tras 15 minutos sin tráfico y el primer acceso puede tardar cerca de un minuto. Render puede reiniciarlo y las salas en memoria se pierden al reiniciar; es una opción gratuita para prototipos y grupos pequeños, no para producción.

## API de juego

- `POST /rooms`, `POST /rooms/{code}/join`, `GET /rooms/{code}`
- `POST /rooms/{code}/ready`, `POST /rooms/{code}/start`
- `POST /rooms/{code}/guess` con `{ "number": 1 }`
- `POST /rooms/{code}/clue` con `{ "text": "..." }`
- `POST /rooms/{code}/hint` con `{ "category": "amigos" }`
- Hub SignalR: `/realtime`, evento `roomChanged`

El secreto de Discord permanece en el servidor. Mantén fuera del repositorio `Server/appsettings.json`, `Server/bin` y `Server/obj`. El secreto que aparecía en el archivo de ejemplo anterior debe rotarse en el portal de Discord si llegó a subirse a un repositorio.
