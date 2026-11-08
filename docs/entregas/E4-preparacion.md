# E4 — preparación para el miércoles 11 de noviembre

Esto no es un merge con el repositorio del profesor. La rama sale del HEAD de
E3 de David (`bdcfdf9`) y reimplementa, encima de esas tres pantallas, lo que
el evaluable pide. `upstream/main` (`24ef1a0`) no se mezcló.

La app de E3 se conserva: Entrar, Saldo y Transferir por botones, el JWT en
SecureStorage, y el 403 de Transferir con el texto exacto
«No tienes permisos para transferir, hace falta el rol operador».

## Regla oficial que no se tocó

El reloj de 15 segundos es el post-it de la Semana 1, escrito en
`TransferenciaSaga`: si no llega confirmación del banco, se compensa y el
dinero vuelve. No se alargó ni se apagó. Un intervalo offline más largo que
esa ventana, cuando el servidor ya alcanzó a retener, **termina en
compensación**. Eso es el resultado correcto, no un éxito a medias.

La idempotencia sigue siendo la de la Semana 14: la misma llave responde 200
(no 409, porque un 409 dejaría a la app reintentando para siempre) con el
monto original y `repetida: true`. No se aparta otra vez.

Lo que estaba mal, y sí se corrigió:

- La app decía «ya estaba hecha» y «la plata quedó apartada» también cuando
  la saga había compensado. Ahora el JSON trae `estado`: `pendiente`,
  `liquidada` o `liberada`. Liberada se muestra como compensación, en tono de
  error, y no como transferencia completada. La cola se suelta solo en un
  final (salió o volvió). Mientras sigue pendiente, la llave permanece en el
  teléfono y al reabrir se vuelve a preguntar el mismo id.
- `AbonoConfirmado` que llega antes de que exista la saga (típico si
  `FondosRetenidos` está reintentando un `40001`) ya no se tira. La saga lo
  recuerda y, cuando nace, liquida sin programar el reloj. El `40001` sigue
  siendo el aislamiento serializable de PostgreSQL: se reintenta, no se
  esconde. Una confirmación que llega cuando la compensación ya empezó no
  liquida: el invariante del dominio es que el dinero no puede haber salido
  y además haber vuelto. `Liberar()` sobre una retención ya liquidada no
  devuelve el saldo.

## Qué se probó

En esta máquina, contra PostgreSQL 16 y RabbitMQ del compose de
`deploy/ubuntu`, puertos solo en `127.0.0.1`:

| Prueba | Resultado |
|---|---|
| `dotnet test src/Breb.App.Tests` | 55 pasaron. Incluyen cierre y reapertura de la cola, 401 que no la borra y al reabrir repite la misma llave, corte de red, y que `estado: liberada` no se presenta como éxito. |
| `dotnet test src/Breb.Cuentas.Tests` | 5 pasaron. Production sin cadena no arranca; rechaza `dev_only_password` y `guest`; liquidar y luego liberar no devuelve el dinero. |
| `dotnet test src/Breb.Cuentas.Integracion` | 3 pasaron, en 18 s, con la API y el broker de verdad. Hace falta definir `BREB_TEST_CONNECTION`, `BREB_TEST_RABBIT_USER` y `BREB_TEST_RABBIT_PASSWORD`; no hay claves en el código. |
| Ocho `POST /retener` a la vez con la **misma** llave | Una sola fila, el saldo baja una vez, siete respuestas `repetida: true`. Después de tirar el host y levantar otro, el mismo id sigue en una fila y el saldo no se mueve. |
| Ocho transferencias **distintas** a la vez, y `confirmar-abono` de las ocho sin esperar a la saga | Las ocho quedan `Liquidada`, ninguna `Liberada`. El disponible baja 80 y el retenido vuelve a 0. Reintentar las ocho no mueve el saldo. |
| Una retención sin confirmación | A los 15 s oficiales queda `Liberada`, el saldo vuelve, y el reintento responde `estado: liberada` sin un segundo débito. |
| APK y teléfono | No hay teléfono. El workload `maui-android` sí se instaló. El build Release no terminó: sin `-r` falla `NU1102` porque `Microsoft.NETCore.App.Runtime.Mono.linux-x64` 10.0.14 no está en nuget.org; con `-r android-arm64` restaura y luego `XA5300` (no hay Android SDK). `InstallAndroidDependencies` cancela la descarga de `https://aka.ms/AndroidManifestFeed/d18-0` aunque `curl` reciba 200. En el PC de David, donde el SDK ya está, el comando de Release de más abajo es el que hay que usar. |
| Azure | No se desplegó nada. |

No hace falta «hacer una sola operación» para que el resultado sea correcto.
Hacer varias a la vez no duplica. No confirmar a tiempo compensa, y la app lo
dice.

## Servidor Ubuntu 22.04 (2 vCPU, 8 GB)

Cuando exista la VM. No use el `docker-compose.yml` de la raíz: publica 5433
y 5673. Use `deploy/ubuntu`.

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2 nginx openssl
sudo usermod -aG docker "$USER"
```

.NET 8 en la VM (el SDK 10 es del PC donde se compila la app):

```bash
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

```bash
cp deploy/ubuntu/.env.example deploy/ubuntu/.env
# claves largas, que no digan CAMBIE, guest, dev_only_password
# ni la clave JWT del laboratorio
chmod 600 deploy/ubuntu/.env
./deploy/ubuntu/levantar.sh
set -a; source deploy/ubuntu/api.env; set +a
cd src/Breb.Cuentas
dotnet tool install --global dotnet-ef --version 9.0.19
dotnet ef database update
cd ../..
docker exec -i breb-postgres psql -U breb -d brebcuentas < scripts/cuentas-base.sql
dotnet run --project src/Breb.Cuentas/Breb.Cuentas.csproj --no-launch-profile --urls http://127.0.0.1:5080
```

`--no-launch-profile` deja el ambiente en Production, que es el de `api.env`.
La API no se publica: solo `127.0.0.1:5080`.

## HTTPS, sin dominio

No hay un nombre público que firmar, y no se inventa uno. En la VM, con la
IP que el teléfono va a usar:

```bash
./deploy/ubuntu/emitir-certificado.sh ESA-IP
sudo mkdir -p /etc/breb
sudo cp deploy/ubuntu/certs/server.crt deploy/ubuntu/certs/server.key /etc/breb/
sudo chmod 600 /etc/breb/server.key
sudo cp deploy/ubuntu/nginx-breb.conf /etc/nginx/sites-available/breb
sudo ln -sf /etc/nginx/sites-available/breb /etc/nginx/sites-enabled/breb
sudo nginx -t && sudo systemctl reload nginx
```

El script crea una CA y un certificado con `subjectAltName` de esa IP y de
`localhost`. `ca.key` no sale del servidor. La carpeta `deploy/ubuntu/certs/`
está en `.gitignore`.

nginx escucha **443** y hace de proxy hacia `127.0.0.1:5080`. No hay sitio en
el puerto 80. El JWT no viaja en claro hacia el teléfono.

Compruebe en la VM:

```bash
ss -ltn | awk '/:5433|:5673|:15673|:5080|:443/'
curl -sk -o /dev/null -w '%{http_code}\n' https://127.0.0.1/swagger/index.html
```

5433, 5673, 15673 y 5080 tienen que decir `127.0.0.1`. El 443 es el único
hacia afuera.

En el grupo de seguridad de red, cuando la política deje entrar a Azure:
TCP 443 solo desde la IP del teléfono, TCP 22 solo desde quien administra.
No abrir 80, 5080, 5432, 5433, 5672, 5673 ni 15672. Esto no está hecho.

## Teléfono

El APK de la demo es **Release**. Debug sigue permitiendo HTTP hacia el
emulador (`10.0.2.2`); eso es el laboratorio de la Semana 11, no el camino
del teléfono. Release no permite claro (`network_security_config.xml`).

La app usa el manejador HTTP de Android (`UseNativeHttpHandler`), así que
respeta las CA del sistema y las que el usuario instale. Hay que instalar
`ca.crt` en el teléfono:

1. Copie `deploy/ubuntu/certs/ca.crt` al aparato.
2. Ajustes → Seguridad → Cifrado y credenciales → Instalar un certificado →
   Certificado de CA.
3. Android avisa que una CA puede ver el tráfico. Esta CA es la del
   laboratorio; no la use en otro sitio.
4. En Entrar borre la dirección entera y escriba `https://ESA-IP` (sin puerto).
   Al pulsar Entrar tiene que quedar `https://ESA-IP:443`. Si vuelve a `:5080`,
   el APK es el anterior: ese puerto es el del emulador y el enlace del campo
   no alcanzaba a guardar lo escrito. No abra el 5080 en la VM.

Sin ese certificado instalado, Release no abre la API. No hay forma de
generarlo aquí: la IP de la VM no existe todavía y no se inventa.

En el PC donde ya compiló el E3:

```bash
dotnet build src/Breb.App/Breb.App.csproj -c Release -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true
```

## Guion, cuando existan la VM, el certificado y el teléfono

1. Entre como `ana.operadora`. `luis.consulta` ve el saldo y, al transferir,
   el 403 literal.
2. Transferir. La llave queda en pantalla y en el teléfono hasta que el
   servidor diga un final.
3. Modo avión a mitad, cierre total de la app, volver a abrir. La misma llave
   se reenvía. Si el servidor nunca la vio, la retiene una vez y arranca el
   reloj de 15 s en ese momento. Si ya la vio y sigue pendiente, no aparta
   otra vez. Si ya compensó, la pantalla lo dice en rojo y no hay un segundo
   débito. Para enviarla de nuevo hay que pulsar Transferir otra vez: es otra
   llave, no un duplicado de la que volvió.
4. Repetir una llave ya liquidada, sin débito nuevo y sin otro APK:

   ```bash
   ./scripts/reintentar-llave.sh 55d6aeb1-121e-4da8-9244-c792f26c440a 10
   ```

   Tiene que decir `repetida=true`, `estado=liquidada`, las mismas filas y el
   mismo saldo. Si la llave no existe, no llama a `/retener`.

5. Confirmación sin copiar la llave. En la VM, antes de pulsar Transferir:

   ```bash
   git pull
   ./scripts/vigilar-confirmacion.sh
   ```

   Cuando diga «Armada», en el teléfono pulse Transferir una sola vez.
   El vigilante llama a `scripts/confirmar-abono.sh` (rol banco, localhost).
   No reinicia la API ni hace falta otro APK. No abre el 5080 hacia afuera.
6. «Actualizar saldo (ver confirmación)». `liquidada`: el dinero salió una
   vez. `liberada`: volvió. No se presente el segundo como éxito.

Varias transferencias a la vez son válidas. Cada una tiene su llave. El
saldo baja la suma, y confirmarlas no las compensa por llegar antes que la
saga.

## Modo avión a mitad de la transferencia

El ensayo en el que la app encoló 10 UVB, se cerró y luego se reconectó
demuestra la cola de antes de aceptar. No es el modo avión a mitad: ahí el
toque ya salió a la red.

Lo observable, sin adivinar el instante:

1. Arme `./scripts/vigilar-confirmacion.sh`.
2. En el teléfono pulse Transferir y, enseguida, active el modo avión.
3. Mire la terminal antes de quitar el modo avión.
   - Si ya imprimió `Nueva y viva`, el servidor aceptó la petición y el
     teléfono no alcanzó a ver la respuesta. Es aceptación perdida. El
     vigilante confirma esa misma fila.
   - Si sigue en `Espero`, la petición no llegó. Es cola previa. Al quitar
     el modo avión el reintento crea una sola fila y el vigilante la confirma.
4. Si no anotó en qué momento apareció `Nueva y viva` respecto del modo
   avión, no afirme cuál de los dos caminos fue. El saldo que baja una sola
   vez sí se puede afirmar cuando el `FINAL` es `liquidada`.

Después de liquidar, Actualizar saldo saca la operación de la cola. Reenviar
dice que no hay pendientes: eso no prueba el rechazo de un duplicado. Esa
prueba es `reintentar-llave.sh`.

## Arranque durable de la API

No haga `set -a; source deploy/ubuntu/api.env`. El shell ejecuta ese archivo:
un `$()` en una clave correría, y los secretos quedan exportados en la
sesión. `systemd` con `EnvironmentFile` no lo ejecuta.

Apache sigue en el puerto 80. No lo pare ni ponga la API ahí. nginx sigue en
el 443. La API solo en `127.0.0.1:5080`.

Cuando quiera que sobreviva al cierre de la terminal, copie
`deploy/ubuntu/breb-api.service`, sustituya `RUTA` y actívela. Hasta entonces
puede dejar el `dotnet` que ya está corriendo. No hace falta reiniciarlo
para el reintento de la llave ni para el vigilante.

## Pendiente que solo puede hacer David

- VM Ubuntu 22.04, Docker Engine, nginx, .NET 8, `.env` real, `levantar.sh`,
  `dotnet ef database update` y `cuentas-base.sql`.
- `./deploy/ubuntu/emitir-certificado.sh` con la IP real, nginx con
  `nginx-breb.conf`, `ca.crt` instalada en el teléfono como CA.
- NSG: 443 desde el teléfono, 22 desde quien administra. Nada de base, broker
  ni 5080 hacia afuera.
- APK Release instalado y un ensayo en ese aparato: modo avión, cierre,
  reapertura, una compensación dicha en voz alta y una liquidación.
