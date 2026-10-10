# Seguridad y límites

## Versión online

El servidor genera y conserva la carta. La vista del adivinador omite el número mientras juega y lo revela al terminar el turno. Los eventos SignalR solo notifican cambios: nunca difunden una vista privada al grupo. Cada acción comprueba identidad, pertenencia, fase y turno. Las adivinanzas duplicadas y los números repetidos no consumen intentos.

Hay límites de solicitudes, ayudas y mensajes, y caducidad de salas, tokens y solicitudes de login de dispositivos. Los nombres y pistas se insertan como texto o se escapan antes de mostrarse. No publiques credenciales, archivos de configuración locales, claves de protección de datos ni salidas de compilación.

## Discord y sesión recordada

OAuth solicita únicamente `identify`, valida `state` y mantiene el secreto de la aplicación en el servidor. Una cookie `HttpOnly`, `Secure` en producción y `SameSite=Lax` conserva la identidad del navegador. Se firma con HMAC-SHA256 y una clave derivada del secreto de Discord con un propósito específico; el secreto y los tokens OAuth nunca se incluyen en ella.

La cookie dura un año y se renueva al volver a usar el juego después de un día. Así puede mantenerse la sesión mientras se use ese navegador. «Cerrar sesión» borra la cookie y la sesión de ese navegador; borrar cookies también exige volver a entrar. Rotar el secreto de Discord invalida todas las cookies recordadas. No hay todavía un panel de revocación individual de otros dispositivos.

La identidad firmada conserva nombre y avatar del último login. Para actualizarlos tras cambiarlos en Discord, cierra sesión y vuelve a vincular. Las mesas se guardan en memoria y no sobreviven a los reinicios del servicio; se informa al usuario y se permite crear otra conservando el login.

El acceso de desarrollo `/dev/login` solo se registra en Development. El despliegue debe usar Production. Los logs de valoración contienen índices del catálogo y votos, no nombres ni pistas privadas.

## Prototipo Unity

El modo local en `Assets/` es una simulación distinta. Sus números existen en la memoria del cliente y no ofrece protección antitrampas. La lógica online autoritativa está en `Server/`.
