#!/usr/bin/env bash
# Banco destino, en la terminal, para UNA transferencia del teléfono.
#
# Arrancarlo ANTES de pulsar Transferir. No copia la llave a mano.
# Lee la base, no escribe en ella. No acorta los 15 s ni confirma como
# operador: cuando aparece una retención nueva y todavía viva, llama a
# scripts/confirmar-abono.sh (rol banco). Las que ya existían al armar,
# y las que ya están liberadas o liquidadas, no cuentan.
#
#   ./scripts/vigilar-confirmacion.sh
#
# Depende de: bash, sudo, docker, el contenedor breb-postgres (usuario breb,
# base brebcuentas) y la API ya escuchando en 127.0.0.1:5080. No reinicia
# la API ni pide otro APK.

set -euo pipefail

API="${BREB_API:-http://127.0.0.1:5080}"
CONT="${BREB_PG_CONTAINER:-breb-postgres}"
DB="${BREB_PG_DB:-brebcuentas}"
PGUSER="${BREB_PG_USER:-breb}"
ESPERA="${BREB_ESPERA_SEG:-90}"
# Margen bajo los 15 s oficiales: si al verla ya es más vieja, no se confirma.
MAX_EDAD="${BREB_MAX_EDAD_SEG:-8}"
DOCKER="${BREB_DOCKER:-sudo docker}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIRMAR="${BREB_CONFIRMAR:-$ROOT/scripts/confirmar-abono.sh}"

sql() {
  # shellcheck disable=SC2086
  $DOCKER exec "$CONT" psql -X -q -U "$PGUSER" -d "$DB" \
    -v ON_ERROR_STOP=1 -t -A -c "$1"
}

uuid_ok() {
  [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]
}

es_si() { [[ "$1" == "t" || "$1" == "true" || "$1" == "1" ]]; }

if ! sql 'SELECT 1' >/dev/null; then
  echo "No pude leer ${CONT} / ${DB} como ${PGUSER}." >&2
  exit 6
fi

mapfile -t VIEJAS < <(sql 'SELECT "TransferenciaId"::text FROM "Retenciones";' | sed '/^$/d')
for id in "${VIEJAS[@]:-}"; do
  uuid_ok "$id" || { echo "Id ilegible en la base: $id" >&2; exit 6; }
done

lista="ARRAY[]::text[]"
if ((${#VIEJAS[@]} > 0)); then
  lista="ARRAY["
  for id in "${VIEJAS[@]}"; do
    lista+="'${id}',"
  done
  lista="${lista%,}]"
fi

echo "Armada. Retenciones ya existentes: ${#VIEJAS[@]}. No se tocarán."
echo "Pulsa Transferir UNA vez en el teléfono. Espero ${ESPERA} s."

consulta_nuevas='SELECT "TransferenciaId"::text || '"'"'|'"'"' ||
  round(EXTRACT(EPOCH FROM (clock_timestamp() - "CreadaEn")))::text
  FROM "Retenciones"
  WHERE "Liberada" = false AND "Liquidada" = false
    AND NOT ("TransferenciaId"::text = ANY('"$lista"'))
  ORDER BY "CreadaEn"'

limite=$((SECONDS + ESPERA))
elegida=""
while ((SECONDS < limite)); do
  mapfile -t VIVAS < <(sql "$consulta_nuevas" | sed '/^$/d')
  if ((${#VIVAS[@]} > 1)); then
    echo "Hay ${#VIVAS[@]} retenciones nuevas y vivas. No confirmo ninguna." >&2
    printf '  %s\n' "${VIVAS[@]}" >&2
    exit 2
  fi
  if ((${#VIVAS[@]} == 1)); then
    id="${VIVAS[0]%%|*}"
    edad="${VIVAS[0]##*|}"
    uuid_ok "$id" || { echo "Id nuevo ilegible." >&2; exit 6; }
    if awk -v e="$edad" -v m="$MAX_EDAD" 'BEGIN { exit !(e+0 <= m+0) }'; then
      elegida="$id"
      echo "Nueva y viva: $id (edad ${edad} s). Pido confirmación al banco."
      break
    fi
    echo "Vi $id pero ya tiene ${edad} s. Los 15 s oficiales no dan margen. No confirmo." >&2
    exit 3
  fi
  sleep 0.4
done

if [[ -z "$elegida" ]]; then
  echo "No apareció una retención nueva y viva. No se confirmó nada." >&2
  exit 1
fi

set +e
"$CONFIRMAR" "$elegida" "$API"
conf_rc=$?
set -e
echo "confirmar-abono.sh terminó con código ${conf_rc}. El 202 no es el final."

estado=""
hasta=$((SECONDS + 8))
while ((SECONDS < hasta)); do
  estado="$(sql "SELECT \"Liberada\"::text || '|' || \"Liquidada\"::text FROM \"Retenciones\" WHERE \"TransferenciaId\" = '${elegida}'::uuid")"
  liberada="${estado%%|*}"
  liquidada="${estado##*|}"
  if es_si "$liquidada" || es_si "$liberada"; then
    break
  fi
  sleep 0.4
done

echo
if es_si "$liquidada" && ! es_si "$liberada"; then
  echo "FINAL: liquidada. El dinero de $elegida salió una vez."
  echo "En el teléfono: Actualizar saldo. Retenido 0 y el disponible se queda abajo."
  exit 0
fi
if es_si "$liberada"; then
  echo "FINAL: compensada. $elegida no se completó: el dinero volvió." >&2
  echo "El reloj de 15 s ganó, o la confirmación llegó tarde. No es un éxito." >&2
  exit 4
fi
echo "FINAL: sigue pendiente ($estado). No afirmo que se haya liquidado." >&2
exit 5
