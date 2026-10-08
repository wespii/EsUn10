# Es un 10 pero...

Juego social de cartas con un prototipo local de Unity y un servidor web multijugador ASP.NET Core.

## Requisitos

- Unity 2022.3 LTS o posterior.
- Módulo de Windows/Mac/Linux según la plataforma donde se quiera probar.

## Ejecutar

1. Abre esta carpeta desde Unity Hub.
2. Abre `Assets/Scenes/Prototype.unity`.
3. Pulsa Play.
4. En el menú selecciona el número de jugadores, pulsa **CREAR LOBBY** y luego **INICIAR PARTIDA**.

Para jugar online desde el navegador, consulta [Server/README.md](Server/README.md). El servidor ofrece login de Discord y mesas multijugador; el despliegue gratuito requiere una cuenta Render y una aplicación OAuth de Discord.

Esta primera versión usa jugadores simulados para poder probar el flujo sin servidor:

- El jugador local no puede ver su propio número.
- Los números de los demás jugadores sí son visibles.
- Se generan pistas de ejemplo para cada turno.
- Los jugadores simulados resuelven automáticamente sus turnos.
- La interfaz usa una mesa verde tipo fieltro, cartas ilustradas en crema y coral, fichas de jugadores y composición adaptable a la resolución.
- El jugador activo puede elegir un número del 1 al 10.
- La interfaz del turno muestra una carta central: el jugador activo no ve su número y el resto sí.
- El jugador activo tiene hasta 3 intentos y 90 segundos para adivinar; los demás dan pistas mediante una llamada de voz externa.
- Cuando le toca dar pistas a una persona, puede pulsar **PISTA** y elegir entre **Hot**, **Pareja**, **Amigos** o **Broma** para recibir una frase sugerida basada en el número de la carta. La sugerencia aparece solo en la vista de quienes dan pistas.
- Si el jugador local no responde en 90 segundos, su turno se omite y no obtiene puntos.
- La puntuación se calcula con +3 por acierto exacto, +1 por diferencia de uno y 0 en los demás casos.
- El turno avanza y el resultado se muestra brevemente en la misma pantalla.

El cliente Unity existente conserva el modo local simulado; la partida online está en el cliente web servido por el backend. La voz se hace en una llamada externa de Discord.
