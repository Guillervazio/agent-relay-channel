#!/usr/bin/env bash
# Lo que los smokes necesitan tener delante antes de empezar. Se carga con
# `source`; no se ejecuta.
#
# Existe por un fallo concreto: jget() manda stderr a /dev/null, así que un
# intérprete ausente no daba un error, daba un campo vacío, y la comprobación
# fallaba quejándose del contenido de una respuesta que estaba bien. Una suite que
# miente sobre por qué falla cuesta más que una que no corre.

# Deja en PY un intérprete de Python 3, y no da por hecho cómo se llama: en la
# máquina donde se escribió esto sólo existe `python`, y en un runner de Ubuntu
# sólo `python3`. Se comprueba también la versión, porque los smokes usan
# sys.stdout.reconfigure, que es de 3.7 en adelante.
require_python() {
  local candidate
  for candidate in python3 python; do
    if command -v "$candidate" > /dev/null 2>&1 &&
       "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 7) else 1)' \
         > /dev/null 2>&1; then
      PY="$candidate"
      return 0
    fi
  done
  echo "$(basename "$0"): no encuentro Python 3.7 o posterior." >&2
  echo "  Los smokes leen JSON con él. Probé 'python3' y 'python' en el PATH." >&2
  return 1
}

# El resto de lo que hace falta y no es Python. El segundo argumento es para qué,
# porque «falta curl» sin más deja al que lo lee buscando dónde.
require_cmd() {
  if command -v "$1" > /dev/null 2>&1; then
    return 0
  fi
  echo "$(basename "$0"): falta $1, que hace falta para $2." >&2
  return 1
}
