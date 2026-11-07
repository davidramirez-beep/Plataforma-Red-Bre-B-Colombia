#!/usr/bin/env bash
# Prepara PostgreSQL y RabbitMQ en este Ubuntu y escribe api.env para Breb.Cuentas.
# No abre puertos públicos. No imprime contraseñas.
#
#   ./levantar.sh --comprobar    valida .env y el compose, no levanta contenedores
#   ./levantar.sh                docker compose up -d y escribe api.env
#
# Después, en la raíz del repositorio:
#   set -a; source deploy/ubuntu/api.env; set +a
#   dotnet run --project src/Breb.Cuentas/Breb.Cuentas.csproj --urls http://0.0.0.0:5080

set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DIR"

MODO="levantar"
if [[ "${1:-}" == "--comprobar" ]]; then
  MODO="comprobar"
elif [[ -n "${1:-}" ]]; then
  echo "Uso: $0 [--comprobar]" >&2
  exit 2
fi

if [[ ! -f .env ]]; then
  echo "Falta deploy/ubuntu/.env. Copie .env.example y cambie los secretos." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source .env
set +a

rechazar() {
  echo "$1" >&2
  exit 1
}

exigir() {
  local nombre="$1" valor="$2" minimo="$3"
  [[ -n "$valor" ]] || rechazar "Falta $nombre en .env."
  [[ "$valor" != *CAMBIE* ]] || rechazar "$nombre sigue con el texto de ejemplo CAMBIE."
  [[ "$valor" != "guest" && "$valor" != "dev_only_password" ]] || rechazar "$nombre no puede ser un secreto de laboratorio."
  [[ "$valor" != "clave-de-laboratorio-no-usar-en-produccion-32+" ]] || rechazar "$nombre es la clave JWT publicada en el repositorio."
  (( ${#valor} >= minimo )) || rechazar "$nombre tiene que tener al menos $minimo caracteres."
}

exigir POSTGRES_PASSWORD "${POSTGRES_PASSWORD:-}" 16
exigir RABBITMQ_PASSWORD "${RABBITMQ_PASSWORD:-}" 16
exigir JWT_CLAVE_FIRMA "${JWT_CLAVE_FIRMA:-}" 32
[[ -n "${POSTGRES_USER:-}" && "${POSTGRES_USER}" != "guest" ]] || rechazar "Defina POSTGRES_USER."
[[ -n "${RABBITMQ_USER:-}" && "${RABBITMQ_USER}" != "guest" ]] || rechazar "RABBITMQ_USER no puede ser guest."
POSTGRES_DB="${POSTGRES_DB:-brebcuentas}"

if grep -E '^[[:space:]]+- "' docker-compose.yml | grep -v '127.0.0.1:' >/dev/null; then
  rechazar "docker-compose.yml publica algún puerto fuera de 127.0.0.1."
fi

umask 077
{
  printf 'ASPNETCORE_ENVIRONMENT=Production\n'
  printf 'ConnectionStrings__Cuentas=%s\n' \
    "Host=127.0.0.1;Port=5433;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};SSL Mode=Disable;Timeout=30;Command Timeout=60;Maximum Pool Size=25;Minimum Pool Size=5"
  printf 'RabbitMq__Host=127.0.0.1\n'
  printf 'RabbitMq__Port=5673\n'
  printf 'RabbitMq__VirtualHost=/\n'
  printf 'RabbitMq__User=%s\n' "$RABBITMQ_USER"
  printf 'RabbitMq__Password=%s\n' "$RABBITMQ_PASSWORD"
  printf 'Jwt__ClaveFirma=%s\n' "$JWT_CLAVE_FIRMA"
} > api.env
chmod 600 api.env
echo "api.env escrito (modo 600). No lo copie al repositorio ni lo pegue en el chat."

if [[ "$MODO" == "comprobar" ]]; then
  if command -v docker >/dev/null 2>&1; then
    docker compose --env-file .env config >/dev/null
    echo "Compose válido. No se levantaron contenedores."
  else
    echo "Compose revisado en el archivo. docker no está en esta máquina: no se interpoló con el daemon."
  fi
  exit 0
fi

command -v docker >/dev/null 2>&1 || rechazar "No está Docker Engine. En Ubuntu: sudo apt-get install -y docker.io docker-compose-v2"
docker compose --env-file .env up -d
echo "Contenedores en 127.0.0.1. La API todavía no está corriendo: vea el comentario de arriba."
