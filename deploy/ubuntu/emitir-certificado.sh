#!/usr/bin/env bash
# Crea una CA y un certificado de servidor para HTTPS sin dominio público.
# El argumento es la IP en la que el teléfono va a abrir la API. No hay un
# valor por defecto: inventar una IP o un nombre firmaría el certificado mal.
#
#   ./deploy/ubuntu/emitir-certificado.sh 203.0.113.10
#
# Escribe en deploy/ubuntu/certs/ (no se commitea):
#   ca.crt  ca.key  server.crt  server.key
# Instale ca.crt en el teléfono como CA. server.crt y server.key van a nginx.
# ca.key no sale del servidor.

set -euo pipefail

IP="${1:-}"
if [[ ! "$IP" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
  echo "Uso: $0 <ip-del-servidor>" >&2
  echo "Ejemplo: $0 203.0.113.10" >&2
  exit 2
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/certs"
mkdir -p "$DIR"
chmod 700 "$DIR"

if [[ -f "$DIR/ca.key" || -f "$DIR/server.key" ]]; then
  echo "Ya hay claves en $DIR. Bórrelas a mano si de verdad quiere emitir otra CA." >&2
  exit 1
fi

umask 077
openssl req -x509 -newkey rsa:2048 -nodes -days 825 \
  -keyout "$DIR/ca.key" -out "$DIR/ca.crt" \
  -subj "/CN=Bre-B laboratorio CA"

openssl req -newkey rsa:2048 -nodes \
  -keyout "$DIR/server.key" -out "$DIR/server.csr" \
  -subj "/CN=$IP"

cat > "$DIR/server.ext" <<EOF
subjectAltName=IP:${IP},DNS:localhost
extendedKeyUsage=serverAuth
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
EOF

openssl x509 -req -in "$DIR/server.csr" -days 825 \
  -CA "$DIR/ca.crt" -CAkey "$DIR/ca.key" -CAcreateserial \
  -out "$DIR/server.crt" -extfile "$DIR/server.ext"

rm -f "$DIR/server.csr" "$DIR/server.ext" "$DIR/ca.srl"
chmod 600 "$DIR/ca.key" "$DIR/server.key"
chmod 644 "$DIR/ca.crt" "$DIR/server.crt"

echo "CA y certificado emitidos para IP ${IP}."
echo "  CA del teléfono: $DIR/ca.crt"
echo "  nginx:           $DIR/server.crt y $DIR/server.key"
echo "No suba esta carpeta. Copie ca.crt al teléfono e instálela como CA."
