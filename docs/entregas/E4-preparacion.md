# E4 — preparación para el miércoles 11 de noviembre

La app de E3 se conserva: Entrar, Saldo y Transferir por botones, el JWT en
SecureStorage, y el 403 de Transferir con el texto exacto
«No tienes permisos para transferir, hace falta el rol operador».

Encima de eso queda lo que pide el evaluable: la transferencia se escribe en
disco antes de salir a la red, el reintento manda la misma llave, y la URL de
la API se cambia en Entrar sin recompilar.

Esto no es una demo hecha en un teléfono. Aquí no hay aparato Android ni se
compiló el APK.

## Qué se probó, y qué no

En esta máquina (Ubuntu 24.04, sin teléfono):

| Prueba | Resultado |
|---|---|
| `dotnet test src/Breb.App.Tests` | 54 pasaron. Cubren el 403 literal, el 400, la cola que sobrevive a otra instancia sobre la misma carpeta, el reintento con la misma llave, que un 401 o un corte de red no borra la pendiente, y que la URL escrita en Entrar se guarda. |
| `dotnet test src/Breb.Cuentas.Tests` | 3 pasaron. Fuera de Development no arranca sin cadena, y rechaza `dev_only_password` y `guest`. |
| `Breb.ColaPrueba` en dos procesos | El segundo proceso leyó la llave que encoló el primero y, tras confirmar, quedó en cero. Un archivo corrupto no tumba la lectura. |
| API real contra PostgreSQL 16 y RabbitMQ, compose de `deploy/ubuntu`, puertos solo en `127.0.0.1` | Una retención de 15 UVB con llave fija dejó disponible 4985 y retenido 15. La segunda llamada con la misma llave respondió `repetida: true` y no movió el saldo. `scripts/confirmar-abono.sh` no se usó en esa corrida; el POST `/confirmar-abono` con rol `banco`, dentro de los 15 s, dejó `Liquidada = true`, retenido 0 y disponible 4985. Un 400 de saldo insuficiente y un 403 de `luis.consulta` siguieron igual. |
| Arranque `ASPNETCORE_ENVIRONMENT=Production` sin variables | Se niega a salir: falta `ConnectionStrings__Cuentas`. |
| `deploy/ubuntu/levantar.sh --comprobar` con el `.env.example` | Lo rechaza porque las claves dicen `CAMBIE`. |
| APK, emulador y teléfono físico | No. Esta máquina no tiene el workload de MAUI Android. |
| Azure, SSH y un servidor ya desplegado | No se hizo. La conexión a Azure está bloqueada y no se desplegó nada. |

La corrida con dos retenciones a la vez chocó con el `40001` de PostgreSQL que
ya está documentado en el curso. Las dos terminaron `Liberada` (la saga
compensó) porque la confirmación llegó mientras el reintento de la saga todavía
no había asentado. En la demo hay que hacer **una** transferencia y confirmarla
antes de 15 segundos, no dos juntas.

`ss` mostró `127.0.0.1:5433`, `127.0.0.1:5673` y `127.0.0.1:15673`. Nada de eso
escuchaba en `0.0.0.0`.

## Servidor Ubuntu 22.04 (2 vCPU, 8 GB)

Hágalo en la VM cuando exista. No use el `docker-compose.yml` de la raíz: ese
es el laboratorio y publica 5433 y 5673 en todas las interfaces, con
`dev_only_password` y `guest`. En la VM use solo `deploy/ubuntu`.

En la VM, Docker Engine (no Docker Desktop):

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker "$USER"
```

.NET 8, que es el de `Breb.Cuentas`. El SDK 10 hace falta en el PC donde se
compila la app, no en esta VM:

```bash
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0
```

Secretos, en la VM y solo ahí:

```bash
cp deploy/ubuntu/.env.example deploy/ubuntu/.env
# edite .env. Claves largas, distintas, que no digan CAMBIE ni guest
# ni dev_only_password ni la clave JWT del laboratorio.
chmod 600 deploy/ubuntu/.env
./deploy/ubuntu/levantar.sh
```

Eso levanta Postgres y RabbitMQ atados a `127.0.0.1` y escribe
`deploy/ubuntu/api.env` con modo 600. Ese archivo no se commitea.

Tablas y cuentas, con el usuario que puso en `.env` (el ejemplo usa `breb`,
no `postgres`):

```bash
set -a; source deploy/ubuntu/api.env; set +a
cd src/Breb.Cuentas
dotnet tool install --global dotnet-ef --version 9.0.19
dotnet ef database update
cd ../..
docker exec -i breb-postgres psql -U breb -d brebcuentas < scripts/cuentas-base.sql
dotnet run --project src/Breb.Cuentas/Breb.Cuentas.csproj --no-launch-profile --urls http://0.0.0.0:5080
```

`--no-launch-profile` importa: sin eso Visual Studio dejaría el ambiente en
Development y aceptaría la clave de laboratorio. Con `api.env` el ambiente es
Production.

Compruebe en la propia VM, no desde fuera:

```bash
ss -ltn | awk '/:5433|:5673|:15673|:5080/'
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5080/swagger/index.html
```

5433, 5673 y 15673 tienen que decir `127.0.0.1`. El 5080 es el único que
escucha en `0.0.0.0`, porque el teléfono tiene que llegar a la API.

## Lo que falta hacer en Azure, a mano

No está hecho. Cuando la política deje entrar a la suscripción:

1. Una VM Ubuntu 22.04, 2 vCPU y 8 GB. Nada más: ni base gestionada ni
   RabbitMQ público.
2. En el grupo de seguridad de red, permitir TCP 22 solo desde su IP y TCP
   5080 solo desde la IP del teléfono. No abrir 5432, 5433, 5672, 5673 ni
   15672. Aunque alguien los abriera, el compose no los publica fuera de
   localhost; el NSG es la segunda cerradura, no la única.
3. `ufw` en el mismo sentido, si lo activa.
4. Anotar la IP pública. Esa es la que se escribe en la app:
   `http://ESA-IP:5080`. La API del curso es HTTP, sin TLS. El APK tiene que
   ser **Debug**: en Release, Android bloquea el HTTP en claro
   (`MainApplication.cs`). No deje el 5080 abierto a todo internet.

## Teléfono

En el PC donde ya compiló el E3 (hace falta el workload MAUI; en esta máquina
no está):

```bash
dotnet build src/Breb.App/Breb.App.csproj -c Debug -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true
```

Instale el APK Debug, con depuración USB ya probada. En Entrar, en
«Dirección de la API», escriba `http://IP-DEL-SERVIDOR:5080` y pulse Entrar.
Queda guardada en el teléfono. Cerrar sesión no la borra. `10.0.2.2` solo
sirve dentro del emulador.

Si la IP ya se conoce al compilar, también se puede poner en
`ConfiguracionApi.IpEnLaWifi`. Con la caja de Entrar no hace falta
recompilar.

Antes de culpar a la app, abra `http://ESA-IP:5080/swagger` en el **navegador
del teléfono**. Si esa página no carga, es red o firewall.

## Guion de la demo, cuando el teléfono y la VM existan

1. Entre como `ana.operadora` / `Operadora-2026`. `luis.consulta` ve el saldo
   y, al transferir, el 403 literal. No puede mover plata.
2. Saldo. Tiene que decir 5000 disponible y 0 retenido si acaba de cargar
   `cuentas-base.sql`.
3. Transferir, monto 10. La pantalla muestra la llave. El dinero queda
   apartado, no salió: disponible baja, retenido sube. Si nadie confirma, a
   los 15 segundos vuelve solo.
4. Modo avión a mitad. La app tiene que decir que la operación quedó guardada.
   Cierre la app por completo y ábrala otra vez: Saldo avisa que hay una
   pendiente. Entre de nuevo si hizo falta y abra Transferir: se reenvía sola
   con la misma llave. También puede quitar el modo avión con Transferir
   abierta: al volver la red se reenvía sin otro toque.
5. Antes de que pasen 15 segundos, en la VM:

   ```bash
   ./scripts/confirmar-abono.sh LA-LLAVE-QUE-MUESTRA-LA-APP http://127.0.0.1:5080
   ```

   El script entra como `core-bancario` (rol `banco`). La app no puede
   confirmar: un operador recibiría 403, y está bien que sea así.
6. En el teléfono, «Actualizar saldo (ver confirmación)». Retenido en 0 y
   disponible que **se quedó** en el monto ya descontado: la plata salió
   (`Liquidada = true`). Si el disponible volvió a 5000, ganó el reloj y la
   saga compensó. No lo presente como una transferencia completada.

La cuenta de las demos es `11111111-1111-1111-1111-111111111111`.

## Pendiente exacto

- VM Ubuntu 22.04 creada, con Docker Engine y .NET 8, y el `levantar.sh` ya
  corrido con un `.env` real.
- NSG y, si aplica, `ufw`: solo 5080 hacia el teléfono, SSH hacia quien
  administra. Base y broker sin puerto público.
- `dotnet ef database update` y `scripts/cuentas-base.sql` en esa VM.
- API en `http://0.0.0.0:5080` con `api.env` cargado, y Swagger abierto desde
  el navegador del teléfono.
- APK **Debug** instalado (`net10.0-android`, `EmbedAssembliesIntoApk=true`).
- Dirección de la API escrita en Entrar con la IP pública de la VM.
- Un ensayo del guion, incluido modo avión y cierre de la app, el mismo día
  y con el mismo teléfono de la sustentación.
