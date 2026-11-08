#!/usr/bin/env bash
# Reenvía UNA llave que ya existe, por el POST /retener real.
#
# No inserta otra fila y no elige un id nuevo. Si la llave no está en la
# base, no llama a la API. /retener exige rol operador; el saldo lo lee
# ese mismo rol (LecturaCuentas). El rol banco no puede retener.
#
#   ./scripts/reintentar-llave.sh <transferenciaId> [montoUVB]
#
# API en 127.0.0.1:5080. Lectura con sudo docker al contenedor
# breb-postgres, usuario breb, base brebcuentas. No reinicia nada.

set -euo pipefail

LLAVE="${1:-}"
API="${BREB_API:-http://127.0.0.1:5080}"
CONT="${BREB_PG_CONTAINER:-breb-postgres}"
DB="${BREB_PG_DB:-brebcuentas}"
PGUSER="${BREB_PG_USER:-breb}"
DOCKER="${BREB_DOCKER:-sudo docker}"
MONTO_ARG="${2:-}"

uuid_ok() {
  [[ "$1" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]
}

if ! uuid_ok "$LLAVE"; then
  echo "Uso: $0 <transferenciaId> [montoUVB]" >&2
  exit 2
fi

sql() {
  # shellcheck disable=SC2086
  $DOCKER exec "$CONT" psql -X -q -U "$PGUSER" -d "$DB" \
    -v ON_ERROR_STOP=1 -t -A -c "$1"
}

antes="$(sql "SELECT \"CuentaId\"::text || '|' || \"MontoUVB\"::text || '|' || \"Liberada\"::text || '|' || \"Liquidada\"::text FROM \"Retenciones\" WHERE \"TransferenciaId\" = '${LLAVE}'::uuid")"
if [[ -z "$antes" ]]; then
  echo "No existe ${LLAVE}. No llamo a /retener: crearía una retención nueva." >&2
  exit 2
fi

cuenta="${antes%%|*}"
resto="${antes#*|}"
monto_fila="${resto%%|*}"
resto="${resto#*|}"
liberada_antes="${resto%%|*}"
liquidada_antes="${resto#*|}"
filas_antes="$(sql 'SELECT count(*) FROM "Retenciones";')"

if [[ -n "$MONTO_ARG" ]]; then
  monto_envio="$MONTO_ARG"
else
  monto_envio="$monto_fila"
fi

echo "ANTES filas=${filas_antes} cuenta=${cuenta} monto=${monto_fila} liberada=${liberada_antes} liquidada=${liquidada_antes}"

python3 - "$API" "$cuenta" "$LLAVE" "$monto_envio" << 'PY'
import json, sys, urllib.request
api, cuenta, llave, monto = sys.argv[1:5]

def post(url, body=None, token=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=20) as resp:
            raw = resp.read().decode()
            return resp.status, json.loads(raw) if raw else {}
    except urllib.error.HTTPError as ex:
        raw = ex.read().decode()
        try:
            parsed = json.loads(raw) if raw else raw
        except json.JSONDecodeError:
            parsed = raw
        return ex.code, parsed

def get(url, token):
    req = urllib.request.Request(url)
    req.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(req, timeout=20) as resp:
        return json.load(resp)

# Las mismas identidades de laboratorio que scripts/confirmar-abono.sh.
# /retener exige operador. El banco no puede retener ni leer el saldo.
codigo, tok = post(api + "/token", {"usuario": "ana.operadora", "clave": "Operadora-2026"})
if codigo != 200 or not isinstance(tok, dict) or tok.get("rol") != "operador":
    print(f"TOKEN operador HTTP {codigo}. No reintento.", file=sys.stderr)
    sys.exit(6)
token = tok["token"]

saldo = get(f"{api}/cuentas/{cuenta}/saldo", token)
print(f"SALDO_ANTES disponible={saldo['saldoDisponible']} retenido={saldo['saldoRetenido']}")

codigo, cuerpo = post(
    f"{api}/cuentas/{cuenta}/retener?montoUVB={monto}&transferenciaId={llave}",
    token=token)
if not isinstance(cuerpo, dict):
    print(f"RETENER HTTP {codigo} cuerpo={cuerpo}", file=sys.stderr)
    sys.exit(6)
print(
    f"RETENER HTTP {codigo} repetida={str(cuerpo.get('repetida')).lower()} "
    f"estado={cuerpo.get('estado')} montoUVB={cuerpo.get('montoUVB')} "
    f"transferenciaId={cuerpo.get('transferenciaId')}"
)
saldo2 = get(f"{api}/cuentas/{cuenta}/saldo", token)
print(f"SALDO_DESPUES disponible={saldo2['saldoDisponible']} retenido={saldo2['saldoRetenido']}")
open("/tmp/breb-reintento-campos", "w").write("\n".join([
    str(cuerpo.get("repetida")).lower(),
    str(cuerpo.get("estado") or ""),
    str(cuerpo.get("transferenciaId") or ""),
    str(saldo["saldoDisponible"]),
    str(saldo["saldoRetenido"]),
    str(saldo2["saldoDisponible"]),
    str(saldo2["saldoRetenido"]),
    str(codigo),
]))
PY

mapfile -t CAMPOS < /tmp/breb-reintento-campos
rm -f /tmp/breb-reintento-campos
repetida="${CAMPOS[0]}"
estado="${CAMPOS[1]}"
id_resp="${CAMPOS[2]}"
disp_antes="${CAMPOS[3]}"
ret_antes="${CAMPOS[4]}"
disp_despues="${CAMPOS[5]}"
ret_despues="${CAMPOS[6]}"
http_codigo="${CAMPOS[7]}"

despues="$(sql "SELECT \"Liberada\"::text || '|' || \"Liquidada\"::text FROM \"Retenciones\" WHERE \"TransferenciaId\" = '${LLAVE}'::uuid")"
filas_despues="$(sql 'SELECT count(*) FROM "Retenciones";')"
echo "DESPUES filas=${filas_despues} estado_fila=${despues}"

igual_num() {
  python3 - "$1" "$2" << 'PY'
import sys
from decimal import Decimal
sys.exit(0 if Decimal(sys.argv[1]) == Decimal(sys.argv[2]) else 1)
PY
}

fallo=0
[[ "$http_codigo" == "200" ]] || fallo=1
[[ "$repetida" == "true" ]] || fallo=1
[[ "$id_resp" == "$LLAVE" ]] || fallo=1
[[ "$filas_antes" == "$filas_despues" ]] || fallo=1
[[ "$despues" == "${liberada_antes}|${liquidada_antes}" ]] || fallo=1
igual_num "$disp_antes" "$disp_despues" || fallo=1
igual_num "$ret_antes" "$ret_despues" || fallo=1

if [[ "$fallo" == "0" ]]; then
  echo "PRUEBA: misma fila, repetida=true, saldo sin débito nuevo."
  exit 0
fi
echo "PRUEBA FALLIDA: la repetición no quedó inocua." >&2
exit 3
